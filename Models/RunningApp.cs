namespace Keeper.Models
{
    /// <summary>
    /// 当前正在运行、且带可见窗口的程序。
    /// </summary>
    public class RunningApp
    {
        public string ProcessName { get; set; }

        public string DisplayName { get; set; }

        public string ExecutablePath { get; set; }

        /// <summary>
        /// 打包应用（WindowsApps）的启动目标，形如 shell:AppsFolder\AUMID；普通程序为空。
        /// </summary>
        public string LaunchTarget { get; set; }

        public string MainWindowTitle { get; set; }
    }
}
