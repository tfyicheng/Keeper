using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Keeper.Helpers
{
    /// <summary>
    /// 等待桌面环境就绪，避免开机登录时恢复的程序启动过早（任务栏尚未创建）而启动失败。
    /// </summary>
    public static class ShellReadyHelper
    {
        private const int WaitTimeoutMilliseconds = 120000;
        private const int SlowSettleDelayMilliseconds = 4500;
        private const int FastSettleDelayMilliseconds = 1000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        /// <summary>
        /// 等待任务栏出现并留出桌面初始化时间。
        /// </summary>
        public static async Task WaitForDesktopAsync()
        {
            var stopwatch = Stopwatch.StartNew();
            while (FindWindow("Shell_TrayWnd", null) == IntPtr.Zero &&
                   stopwatch.ElapsedMilliseconds < WaitTimeoutMilliseconds)
            {
                await Task.Delay(500);
            }

            // 刚从登录进入桌面时多等一会儿，手动重启 Keeper 时任务栏已存在，稍等即可
            var delay = stopwatch.ElapsedMilliseconds < 3000
                ? FastSettleDelayMilliseconds
                : SlowSettleDelayMilliseconds;
            await Task.Delay(delay);
        }
    }
}
