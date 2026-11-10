using System;
using System.Security.Principal;

namespace Keeper.Helpers
{
    /// <summary>
    /// Windows 权限相关工具。
    /// </summary>
    public static class PrivilegeHelper
    {
        /// <summary>
        /// 当前进程是否以管理员身份运行。
        /// </summary>
        public static bool IsAdministrator()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
