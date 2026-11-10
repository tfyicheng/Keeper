using System;
using System.IO;

namespace Keeper.Helpers
{
    /// <summary>
    /// 快捷方式（.lnk）目标解析。
    /// </summary>
    public static class ShortcutHelper
    {
        /// <summary>
        /// 在指定位置创建快捷方式；失败时抛出异常。
        /// </summary>
        public static void CreateShortcut(string shortcutPath, string targetPath, string description)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                throw new InvalidOperationException("系统缺少 WScript.Shell 组件，无法创建快捷方式。");
            }

            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? string.Empty;
            if (string.Equals(Path.GetExtension(targetPath), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                shortcut.IconLocation = targetPath;
            }
            shortcut.Description = description ?? string.Empty;
            shortcut.Save();
        }

        /// <summary>
        /// 解析快捷方式指向的目标路径，非快捷方式直接返回原路径，失败返回 null。
        /// </summary>
        public static string ResolveTarget(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (!path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                {
                    return null;
                }

                dynamic shell = Activator.CreateInstance(shellType);
                if (shell == null)
                {
                    return null;
                }

                dynamic shortcut = shell.CreateShortcut(path);
                string target = shortcut.TargetPath;
                return string.IsNullOrWhiteSpace(target) ? null : Environment.ExpandEnvironmentVariables(target);
            }
            catch
            {
                return null;
            }
        }
    }
}
