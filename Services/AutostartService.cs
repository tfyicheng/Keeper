using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Keeper.Helpers;
using Keeper.Models;

namespace Keeper.Services
{
    /// <summary>
    /// 启动项操作失败（含权限不足）时抛出。
    /// </summary>
    public class AutostartOperationException : Exception
    {
        public AutostartOperationException(string message) : base(message)
        {
        }

        public AutostartOperationException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// 扫描并管理 Windows 开机启动项（注册表 Run 与启动文件夹）。
    /// </summary>
    public class AutostartService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedRoot = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

        private static readonly string[] StartupFileExtensions =
            { ".lnk", ".url", ".exe", ".bat", ".cmd", ".vbs" };

        /// <summary>
        /// 扫描当前系统中全部开机启动项。
        /// </summary>
        public IList<AutostartEntry> Scan()
        {
            var entries = new List<AutostartEntry>();

            AddRegistryEntries(entries, RegistryHive.CurrentUser, RegistryView.Registry64, AutostartSource.UserRun, false);
            AddRegistryEntries(entries, RegistryHive.LocalMachine, RegistryView.Registry64, AutostartSource.MachineRun, true);
            AddRegistryEntries(entries, RegistryHive.LocalMachine, RegistryView.Registry32, AutostartSource.MachineRun32, true);

            AddStartupFolderEntries(entries, Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                AutostartSource.UserStartupFolder, false);
            AddStartupFolderEntries(entries, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                AutostartSource.CommonStartupFolder, true);

            return entries
                .OrderByDescending(entry => entry.IsEnabled)
                .ThenBy(entry => entry.Source)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCulture)
                .ToList();
        }

        /// <summary>
        /// 启用或禁用一个启动项。
        /// </summary>
        public void SetEnabled(AutostartEntry entry, bool enabled)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            try
            {
                WriteApprovedState(entry, enabled);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new AutostartOperationException("修改该项需要管理员权限，请以管理员身份运行 Keeper。", ex);
            }
            catch (System.Security.SecurityException ex)
            {
                throw new AutostartOperationException("修改该项需要管理员权限，请以管理员身份运行 Keeper。", ex);
            }
            catch (AutostartOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AutostartOperationException("修改启动项失败：" + ex.Message, ex);
            }
        }

        /// <summary>
        /// 彻底删除一个启动项。
        /// </summary>
        public void Delete(AutostartEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(entry.FilePath))
                {
                    File.Delete(entry.FilePath);
                    DeleteApprovedValue(entry);
                    return;
                }

                using (var baseKey = RegistryKey.OpenBaseKey(GetHive(entry), GetRunView(entry)))
                using (var runKey = baseKey.OpenSubKey(RunKeyPath, true))
                {
                    if (runKey == null)
                    {
                        throw new AutostartOperationException("未找到对应的注册表启动项。");
                    }

                    runKey.DeleteValue(entry.Name, false);
                }

                DeleteApprovedValue(entry);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new AutostartOperationException("删除该项需要管理员权限，请以管理员身份运行 Keeper。", ex);
            }
            catch (System.Security.SecurityException ex)
            {
                throw new AutostartOperationException("删除该项需要管理员权限，请以管理员身份运行 Keeper。", ex);
            }
            catch (AutostartOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AutostartOperationException("删除启动项失败：" + ex.Message, ex);
            }
        }

        /// <summary>
        /// 把任意格式的文件追加为当前用户的开机启动项（在启动文件夹创建快捷方式），
        /// 返回新建的快捷方式路径。
        /// </summary>
        public IList<string> CreateStartupShortcuts(IEnumerable<string> paths)
        {
            var created = new List<string>();
            if (paths == null)
            {
                return created;
            }

            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (string.IsNullOrWhiteSpace(folder))
            {
                throw new AutostartOperationException("未找到当前用户的启动文件夹。");
            }

            try
            {
                Directory.CreateDirectory(folder);
                var existingTargets = ReadStartupTargets(folder);

                foreach (var path in paths)
                {
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        continue;
                    }

                    var fullPath = Path.GetFullPath(path);
                    // 选中的是快捷方式时按真实目标处理，避免生成“快捷方式指向快捷方式”的套娃启动项
                    var target = ShortcutHelper.ResolveTarget(fullPath) ?? fullPath;
                    if (!File.Exists(target))
                    {
                        continue;
                    }

                    // 去重必须和 ReadStartupTargets 一致，按解析后的真实目标比较
                    if (!existingTargets.Add(target))
                    {
                        continue;
                    }

                    var shortcutPath = BuildShortcutPath(folder, Path.GetFileNameWithoutExtension(target));
                    ShortcutHelper.CreateShortcut(shortcutPath, target, "由 Keeper 添加的开机启动项");
                    DeleteStartupFolderApprovedValue(Path.GetFileName(shortcutPath));
                    created.Add(shortcutPath);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new AutostartOperationException("添加启动项需要写入启动文件夹的权限，请以管理员身份运行 Keeper。", ex);
            }
            catch (System.Security.SecurityException ex)
            {
                throw new AutostartOperationException("添加启动项需要写入启动文件夹的权限，请以管理员身份运行 Keeper。", ex);
            }
            catch (AutostartOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new AutostartOperationException("添加启动项失败：" + ex.Message, ex);
            }

            return created;
        }

        private static HashSet<string> ReadStartupTargets(string folder)
        {
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var file in Directory.GetFiles(folder, "*.lnk"))
                {
                    var target = ShortcutHelper.ResolveTarget(file);
                    if (!string.IsNullOrWhiteSpace(target))
                    {
                        targets.Add(target);
                    }
                }
            }
            catch
            {
                // 读取已有快捷方式失败时按“没有”处理
            }

            return targets;
        }

        private static string BuildShortcutPath(string folder, string baseName)
        {
            var name = string.IsNullOrWhiteSpace(baseName) ? "启动项" : baseName;
            var candidate = Path.Combine(folder, name + ".lnk");
            var index = 2;

            while (File.Exists(candidate))
            {
                candidate = Path.Combine(folder, name + " (" + index + ").lnk");
                index++;
            }

            return candidate;
        }

        private static void DeleteStartupFolderApprovedValue(string fileName)
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                    using (var key = baseKey.OpenSubKey(ApprovedRoot + "\\StartupFolder", true))
                    {
                        key?.DeleteValue(fileName, false);
                    }
                }
                catch
                {
                    // 状态值清理失败不影响主流程
                }
            }
        }

        private void AddRegistryEntries(List<AutostartEntry> entries, RegistryHive hive, RegistryView view,
            AutostartSource source, bool requiresAdmin)
        {
            Dictionary<string, bool> states;
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                using (var runKey = baseKey.OpenSubKey(RunKeyPath))
                {
                    if (runKey == null)
                    {
                        return;
                    }

                    states = ReadApprovedStates(hive, GetApprovedSubKeyName(source));

                    foreach (var valueName in runKey.GetValueNames())
                    {
                        if (string.IsNullOrWhiteSpace(valueName))
                        {
                            continue;
                        }

                        var command = runKey.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                        if (string.IsNullOrWhiteSpace(command))
                        {
                            continue;
                        }

                        bool enabled;
                        if (!states.TryGetValue(valueName, out enabled))
                        {
                            enabled = true;
                        }

                        entries.Add(new AutostartEntry
                        {
                            Name = valueName,
                            Command = command,
                            Source = source,
                            IsEnabled = enabled,
                            RequiresAdmin = requiresAdmin,
                            RegistryKeyPath = RunKeyPath,
                            TargetPath = CommandLineHelper.GetExecutablePath(command)
                        });
                    }
                }
            }
            catch (Exception)
            {
                // 单个位置扫描失败不影响其它位置
            }
        }

        private void AddStartupFolderEntries(List<AutostartEntry> entries, string folder,
            AutostartSource source, bool requiresAdmin)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return;
            }

            var states = ReadApprovedStates(RegistryHive.CurrentUser, "StartupFolder");
            MergeStates(states, ReadApprovedStates(RegistryHive.LocalMachine, "StartupFolder"));

            try
            {
                foreach (var file in Directory.GetFiles(folder))
                {
                    var extension = Path.GetExtension(file);
                    if (!StartupFileExtensions.Any(ext => string.Equals(ext, extension, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var fileName = Path.GetFileName(file);
                    bool enabled;
                    if (!states.TryGetValue(fileName, out enabled))
                    {
                        enabled = true;
                    }

                    entries.Add(new AutostartEntry
                    {
                        Name = Path.GetFileNameWithoutExtension(file),
                        Command = file,
                        Source = source,
                        IsEnabled = enabled,
                        RequiresAdmin = requiresAdmin,
                        FilePath = file,
                        TargetPath = ShortcutHelper.ResolveTarget(file) ?? file
                    });
                }
            }
            catch (Exception)
            {
                // 忽略无权限目录
            }
        }

        private static void MergeStates(Dictionary<string, bool> target, Dictionary<string, bool> source)
        {
            foreach (var pair in source)
            {
                if (!target.ContainsKey(pair.Key))
                {
                    target[pair.Key] = pair.Value;
                }
            }
        }

        private Dictionary<string, bool> ReadApprovedStates(RegistryHive hive, string subKeyName)
        {
            var states = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            using (var key = OpenApprovedKey(hive, subKeyName, false))
            {
                if (key == null)
                {
                    return states;
                }

                foreach (var valueName in key.GetValueNames())
                {
                    states[valueName] = IsEnabledState(key.GetValue(valueName) as byte[]);
                }
            }

            return states;
        }

        private void WriteApprovedState(AutostartEntry entry, bool enabled)
        {
            var subKeyName = GetApprovedSubKeyName(entry.Source);
            var valueName = GetApprovedValueName(entry);

            using (var key = OpenApprovedKey(GetHive(entry), subKeyName, true) ?? CreateApprovedKey(GetHive(entry), subKeyName))
            {
                if (key == null)
                {
                    throw new AutostartOperationException("无法写入启动项状态，可能需要管理员权限。");
                }

                var data = new byte[12];
                data[0] = enabled ? (byte)0x02 : (byte)0x03;
                if (!enabled)
                {
                    BitConverter.GetBytes(DateTime.Now.ToFileTimeUtc()).CopyTo(data, 4);
                }

                key.SetValue(valueName, data, RegistryValueKind.Binary);
            }
        }

        private void DeleteApprovedValue(AutostartEntry entry)
        {
            try
            {
                using (var key = OpenApprovedKey(GetHive(entry), GetApprovedSubKeyName(entry.Source), true))
                {
                    key?.DeleteValue(GetApprovedValueName(entry), false);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // 状态值清理失败不影响主流程
            }
        }

        private RegistryKey OpenApprovedKey(RegistryHive hive, string subKeyName, bool writable)
        {
            var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };
            foreach (var view in views)
            {
                try
                {
                    var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    var key = baseKey.OpenSubKey(ApprovedRoot + "\\" + subKeyName, writable);
                    baseKey.Dispose();

                    if (key != null)
                    {
                        return key;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // 尝试下一个视图
                }
            }

            return null;
        }

        private RegistryKey CreateApprovedKey(RegistryHive hive, string subKeyName)
        {
            try
            {
                var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                var key = baseKey.CreateSubKey(ApprovedRoot + "\\" + subKeyName);
                baseKey.Dispose();
                return key;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsEnabledState(byte[] data)
        {
            if (data == null || data.Length == 0)
            {
                return true;
            }

            // 首位 0x02 表示启用，0x03 表示禁用（Windows 任务管理器使用同一套标记）
            return (data[0] & 0x01) == 0;
        }

        private static string GetApprovedSubKeyName(AutostartSource source)
        {
            switch (source)
            {
                case AutostartSource.MachineRun32:
                    return "Run32";
                case AutostartSource.UserStartupFolder:
                case AutostartSource.CommonStartupFolder:
                    return "StartupFolder";
                default:
                    return "Run";
            }
        }

        private static string GetApprovedValueName(AutostartEntry entry)
        {
            return string.IsNullOrWhiteSpace(entry.FilePath)
                ? entry.Name
                : Path.GetFileName(entry.FilePath);
        }

        private static RegistryHive GetHive(AutostartEntry entry)
        {
            switch (entry.Source)
            {
                case AutostartSource.MachineRun:
                case AutostartSource.MachineRun32:
                case AutostartSource.CommonStartupFolder:
                    return RegistryHive.LocalMachine;
                default:
                    return RegistryHive.CurrentUser;
            }
        }

        private static RegistryView GetRunView(AutostartEntry entry)
        {
            return entry.Source == AutostartSource.MachineRun32
                ? RegistryView.Registry32
                : RegistryView.Registry64;
        }
    }
}
