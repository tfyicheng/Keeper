using System;
using Microsoft.Win32;
using Keeper.Helpers;

namespace Keeper.Services
{
    /// <summary>
    /// Keeper 自身的开机启动管理（写入当前用户 Run 键，无需管理员权限）。
    /// </summary>
    public class SelfStartupService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ValueName = "Keeper";

        public bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    var value = key?.GetValue(ValueName) as string;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        return false;
                    }

                    return string.Equals(value.Trim('"'), AppPaths.ExecutablePath, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        public void SetEnabled(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (key == null)
                    {
                        return;
                    }

                    if (enabled)
                    {
                        key.SetValue(ValueName, "\"" + AppPaths.ExecutablePath + "\"");
                        // 清掉系统可能遗留的“已禁用”标记，否则写入也不会生效
                        using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, true))
                        {
                            approved?.DeleteValue(ValueName, false);
                        }
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("设置开机启动失败：" + ex.Message, ex);
            }
        }
    }
}
