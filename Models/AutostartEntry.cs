using System.Windows.Media.Imaging;
using Keeper.Helpers;
using Keeper.Infrastructure;

namespace Keeper.Models
{
    public enum AutostartSource
    {
        UserRun = 0,
        MachineRun = 1,
        MachineRun32 = 2,
        UserStartupFolder = 3,
        CommonStartupFolder = 4
    }

    /// <summary>
    /// 开机启动项。
    /// </summary>
    public class AutostartEntry : ObservableObject
    {
        private BitmapSource _icon;
        private bool _isEnabled;

        public string Name { get; set; }

        public string Command { get; set; }

        public AutostartSource Source { get; set; }

        public bool IsEnabled
        {
            get { return _isEnabled; }
            set { SetProperty(ref _isEnabled, value); }
        }

        /// <summary>
        /// 修改该项是否需要管理员权限。
        /// </summary>
        public bool RequiresAdmin { get; set; }

        /// <summary>
        /// 注册表项所在的键路径（启动文件夹项为空）。
        /// </summary>
        public string RegistryKeyPath { get; set; }

        /// <summary>
        /// 启动文件夹中的文件完整路径（注册表项为空）。
        /// </summary>
        public string FilePath { get; set; }

        public string TargetPath { get; set; }

        public string SourceText
        {
            get
            {
                switch (Source)
                {
                    case AutostartSource.MachineRun:
                        return "所有用户 · 注册表";
                    case AutostartSource.MachineRun32:
                        return "所有用户 · 32 位";
                    case AutostartSource.UserStartupFolder:
                        return "当前用户 · 启动文件夹";
                    case AutostartSource.CommonStartupFolder:
                        return "所有用户 · 启动文件夹";
                    default:
                        return "当前用户 · 注册表";
                }
            }
        }

        public string TargetPathText
        {
            get
            {
                return string.IsNullOrWhiteSpace(TargetPath) ? Command : TargetPath;
            }
        }

        public BitmapSource Icon
        {
            get
            {
                if (_icon == null)
                {
                    _icon = IconLoader.Load(ResolveIconSource());
                }

                return _icon;
            }
        }

        private string ResolveIconSource()
        {
            if (!string.IsNullOrWhiteSpace(FilePath))
            {
                return FilePath;
            }

            if (!string.IsNullOrWhiteSpace(TargetPath))
            {
                return TargetPath;
            }

            return CommandLineHelper.GetExecutablePath(Command);
        }
    }
}
