using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Keeper.Data;
using Keeper.Helpers;
using Keeper.Models;

namespace Keeper.Services
{
    /// <summary>
    /// 采集当前正在运行、且带窗口（含托盘隐藏窗口）的桌面程序。
    /// </summary>
    public class RunningAppService
    {
        private static readonly HashSet<string> IgnoredProcessNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ApplicationFrameHost",
                "TextInputHost",
                "ShellExperienceHost",
                "StartMenuExperienceHost",
                "SearchHost",
                "SearchApp",
                "RuntimeBroker",
                "LockApp",
                "WidgetService",
                "Widgets",
                "SystemSettings",
                "WeChatAppEx",
                "wetype_renderer",
                "wetype_update",
                "TGitCache"
            };

        /// <summary>
        /// 采集当前运行的窗口程序，按可执行文件路径去重。
        /// </summary>
        public IList<RunningApp> Capture()
        {
            var currentProcess = Process.GetCurrentProcess();
            var currentId = currentProcess.Id;
            var selfName = currentProcess.ProcessName;
            var selfPath = AppPaths.ExecutablePath;
            var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var explorerPath = Path.Combine(windowsDirectory, "explorer.exe");
            // 微信、Claunch 等最小化到托盘的程序主窗口被隐藏，MainWindowHandle 为空，
            // 需要借助顶层窗口枚举（含隐藏窗口）来识别。
            var windowProcessIds = WindowEnumerator.GetProcessIdsWithWindows();
            var trayHostProcessIds = WindowEnumerator.GetTrayHostProcessIds();
            // 白名单中的程序在保存快照时直接忽略
            var whitelist = new WhitelistRepository().GetAll();
            var result = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);

            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id == currentId)
                    {
                        continue;
                    }

                    // 自身（包括从其它位置启动的副本）不写入快照
                    if (string.Equals(process.ProcessName, selfName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (process.SessionId == 0)
                    {
                        continue;
                    }

                    if (process.MainWindowHandle == IntPtr.Zero && !windowProcessIds.Contains(process.Id))
                    {
                        continue;
                    }

                    if (IgnoredProcessNames.Contains(process.ProcessName))
                    {
                        continue;
                    }

                    var path = ProcessPathHelper.GetExecutablePath(process);
                    if (string.IsNullOrWhiteSpace(path) ||
                        !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(path, selfPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // 系统目录下的进程不纳入采集，但资源管理器是用户日常使用的程序，需要保留
                    if (IsUnderDirectory(path, windowsDirectory) &&
                        !string.Equals(path, explorerPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var launchTarget = string.Empty;
                    if (IsPackagedApp(path))
                    {
                        // 打包应用的后台进程很多，只记录打开了窗口或常驻托盘的，
                        // 避免把 Photos、显卡服务等后台组件误写进快照。
                        if (process.MainWindowHandle == IntPtr.Zero && !trayHostProcessIds.Contains(process.Id))
                        {
                            continue;
                        }

                        // 打包应用无法直接启动 exe，只能通过 shell:AppsFolder 打开；
                        // 取不到应用模型 ID 时无法恢复，直接跳过。
                        var aumid = ProcessPathHelper.GetApplicationUserModelId(process);
                        if (string.IsNullOrWhiteSpace(aumid))
                        {
                            continue;
                        }

                        launchTarget = "shell:AppsFolder\\" + aumid;
                    }

                    if (IsWhitelisted(path, whitelist))
                    {
                        continue;
                    }

                    // 打包应用统一按启动目标去重，避免同一应用的多个进程重复出现
                    var key = string.IsNullOrEmpty(launchTarget) ? path : launchTarget;
                    if (result.ContainsKey(key))
                    {
                        continue;
                    }

                    result[key] = new RunningApp
                    {
                        ProcessName = process.ProcessName,
                        ExecutablePath = path,
                        DisplayName = GetDisplayName(path, process.ProcessName),
                        MainWindowTitle = process.MainWindowTitle,
                        LaunchTarget = launchTarget
                    };
                }
                catch
                {
                    // 忽略无权限或已退出的进程
                }
                finally
                {
                    process.Dispose();
                }
            }

            return result.Values
                .OrderBy(app => app.DisplayName, StringComparer.CurrentCulture)
                .ToList();
        }

        /// <summary>
        /// 获取当前已运行的进程可执行文件路径集合。
        /// </summary>
        public HashSet<string> GetRunningExecutablePaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    var path = ProcessPathHelper.GetExecutablePath(process);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        paths.Add(path);
                    }
                }
                catch
                {
                    // 忽略
                }
                finally
                {
                    process.Dispose();
                }
            }

            return paths;
        }

        /// <summary>
        /// 判断路径是否在白名单中。
        /// </summary>
        private static bool IsWhitelisted(string path, IList<WhitelistEntry> whitelist)
        {
            foreach (var entry in whitelist)
            {
                if (WhitelistRepository.Matches(entry, path))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 应用商店应用（WindowsApps）无法通过路径直接启动，恢复时需要使用应用模型 ID。
        /// </summary>
        private static bool IsPackagedApp(string path)
        {
            var windowsApps = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            return IsUnderDirectory(path, windowsApps);
        }

        /// <summary>
        /// 判断路径是否位于指定目录内（系统组件、系统级辅助进程不纳入采集）。
        /// </summary>
        private static bool IsUnderDirectory(string path, string directory)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            var prefix = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetDisplayName(string path, string fallback)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(info.FileDescription))
                {
                    return info.FileDescription.Trim();
                }
            }
            catch
            {
                // 忽略
            }

            return string.IsNullOrWhiteSpace(fallback)
                ? Path.GetFileNameWithoutExtension(path)
                : fallback;
        }
    }
}
