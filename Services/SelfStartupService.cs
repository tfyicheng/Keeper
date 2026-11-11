using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;
using Keeper.Helpers;
using Microsoft.Win32;

namespace Keeper.Services
{
    /// <summary>
    /// Keeper 自身的开机启动管理：注册 Windows 计划任务，在用户登录时以管理员权限启动，
    /// 避免走注册表 Run 键时登录被 UAC 拦截。
    /// </summary>
    public class SelfStartupService
    {
        private const string TaskName = "Keeper";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string LegacyValueName = "Keeper";
        private const string TaskNamespace = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        /// <summary>
        /// 计划任务已存在且处于启用状态。
        /// </summary>
        public bool IsEnabled()
        {
            string path;
            bool enabled;
            return TryGetTask(out path, out enabled) && enabled;
        }

        /// <summary>
        /// 计划任务中记录的程序路径；任务不存在或无法读取时返回 null。
        /// </summary>
        public string GetTaskExecutablePath()
        {
            string path;
            bool enabled;
            return TryGetTask(out path, out enabled) ? path : null;
        }

        /// <summary>
        /// 任务中记录的程序路径与当前运行的程序路径是否不一致。
        /// </summary>
        public bool IsPathMismatched(string taskPath)
        {
            if (string.IsNullOrWhiteSpace(taskPath))
            {
                return false;
            }

            var current = AppPaths.ExecutablePath;
            return !string.IsNullOrWhiteSpace(current) &&
                   !string.Equals(taskPath.Trim().Trim('"'), current, StringComparison.OrdinalIgnoreCase);
        }

        public void SetEnabled(bool enabled)
        {
            RemoveLegacyRunEntry();

            if (enabled)
            {
                CreateTask();
            }
            else
            {
                DeleteTask();
            }
        }

        /// <summary>
        /// 按当前程序路径重新注册任务（路径变化后修复）。
        /// </summary>
        public void RepairTask()
        {
            CreateTask();
        }

        /// <summary>
        /// 通过任务计划 COM 接口读取任务信息，避免解析 schtasks 输出时的编码问题。
        /// </summary>
        private static bool TryGetTask(out string executablePath, out bool enabled)
        {
            executablePath = null;
            enabled = false;

            try
            {
                var serviceType = Type.GetTypeFromProgID("Schedule.Service");
                if (serviceType == null)
                {
                    return false;
                }

                dynamic service = Activator.CreateInstance(serviceType);
                service.Connect();

                dynamic root = service.GetFolder("\\");
                dynamic task = root.GetTask(TaskName);

                executablePath = ExtractCommand((string)task.Xml);
                enabled = Convert.ToBoolean(task.Enabled);
                return !string.IsNullOrWhiteSpace(executablePath);
            }
            catch
            {
                // 任务不存在或当前用户无权读取
                return false;
            }
        }

        private static string ExtractCommand(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return null;
            }

            try
            {
                var document = XDocument.Parse(xml);
                var command = document.Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "Command");
                return command?.Value.Trim().Trim('"');
            }
            catch
            {
                return null;
            }
        }

        private void CreateTask()
        {
            var executablePath = AppPaths.ExecutablePath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException("无法获取当前程序路径，开机启动设置失败。");
            }

            var tempFile = Path.Combine(Path.GetTempPath(), "Keeper-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                File.WriteAllText(tempFile, BuildTaskXml(executablePath), Encoding.Unicode);
                RunSchtasks("/Create /TN \"" + TaskName + "\" /XML \"" + tempFile + "\" /F");
            }
            finally
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch
                {
                    // 临时文件清理失败不影响结果
                }
            }
        }

        private static void DeleteTask()
        {
            // 任务不存在时 schtasks 会返回错误，这里忽略
            RunSchtasks("/Delete /TN \"" + TaskName + "\" /F", false);
        }

        /// <summary>
        /// 生成登录触发、管理员权限运行的计划任务定义。
        /// </summary>
        private static string BuildTaskXml(string executablePath)
        {
            var userId = WindowsIdentity.GetCurrent().Name;
            XNamespace ns = TaskNamespace;

            var document = new XDocument(
                new XDeclaration("1.0", "UTF-16", null),
                new XElement(ns + "Task",
                    new XAttribute("version", "1.2"),
                    new XElement(ns + "RegistrationInfo",
                        new XElement(ns + "Description", "Keeper 工作状态守护：用户登录时自动启动")),
                    new XElement(ns + "Triggers",
                        new XElement(ns + "LogonTrigger",
                            new XElement(ns + "Enabled", "true"),
                            new XElement(ns + "UserId", userId))),
                    new XElement(ns + "Principals",
                        new XElement(ns + "Principal",
                            new XAttribute("id", "Author"),
                            new XElement(ns + "UserId", userId),
                            new XElement(ns + "LogonType", "InteractiveToken"),
                            new XElement(ns + "RunLevel", "HighestAvailable"))),
                    new XElement(ns + "Settings",
                        new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                        new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                        new XElement(ns + "StopIfGoingOnBatteries", "false"),
                        new XElement(ns + "AllowHardTerminate", "true"),
                        new XElement(ns + "StartWhenAvailable", "false"),
                        new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                        new XElement(ns + "AllowStartOnDemand", "true"),
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "Hidden", "false"),
                        new XElement(ns + "RunOnlyIfIdle", "false"),
                        new XElement(ns + "WakeToRun", "false"),
                        new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                        new XElement(ns + "Priority", "7")),
                    new XElement(ns + "Actions",
                        new XAttribute("Context", "Author"),
                        new XElement(ns + "Exec",
                            new XElement(ns + "Command", "\"" + executablePath + "\""),
                            new XElement(ns + "WorkingDirectory",
                                Path.GetDirectoryName(executablePath) ?? string.Empty)))));

            return document.Declaration + Environment.NewLine + document.ToString();
        }

        private static void RunSchtasks(string arguments, bool throwOnError = true)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Default,
                StandardErrorEncoding = Encoding.Default
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    throw new InvalidOperationException("无法启动 Windows 任务计划程序。");
                }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0 && throwOnError)
                {
                    var message = string.IsNullOrWhiteSpace(error) ? output : error;
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        message = "错误码 " + process.ExitCode + "。请确认程序以管理员身份运行。";
                    }

                    throw new InvalidOperationException("设置开机启动失败：" + message.Trim());
                }
            }
        }

        /// <summary>
        /// 清理旧版本写入注册表 Run 键的启动项，避免重复启动。
        /// </summary>
        private static void RemoveLegacyRunEntry()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    key?.DeleteValue(LegacyValueName, false);
                }
            }
            catch
            {
                // 旧启动项清理失败不影响计划任务
            }
        }
    }
}
