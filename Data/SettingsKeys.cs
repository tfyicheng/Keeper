namespace Keeper.Data
{
    /// <summary>
    /// 配置项的键名集中管理。
    /// </summary>
    public static class SettingsKeys
    {
        /// <summary>
        /// 启动 Keeper 时是否自动恢复最近一次快照。
        /// </summary>
        public const string AutoRestoreOnLaunch = "AutoRestoreOnLaunch";

        /// <summary>
        /// 恢复时是否跳过已经运行的程序。
        /// </summary>
        public const string RestoreSkipRunning = "RestoreSkipRunning";

        /// <summary>
        /// 关闭主窗口时的行为：Ask（询问）/ Tray（退到托盘）/ Exit（退出程序）。
        /// </summary>
        public const string CloseAction = "CloseAction";

        /// <summary>
        /// 启动时要恢复的快照 Id 列表（逗号分隔），为空表示恢复最近一次快照。
        /// </summary>
        public const string AutoRestoreSnapshotIds = "AutoRestoreSnapshotIds";
    }
}
