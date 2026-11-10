using System.Collections.ObjectModel;
using Keeper.Helpers;
using Keeper.Infrastructure;

namespace Keeper.ViewModels
{
    /// <summary>
    /// 主窗口视图模型：承载四个功能模块。
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        private NavItem _selectedNavItem;

        public MainViewModel()
        {
            WorkState = new WorkStateViewModel();
            Autostart = new AutostartViewModel();
            Whitelist = new WhitelistViewModel();
            Settings = new SettingsViewModel();

            NavItems = new ObservableCollection<NavItem>
            {
                new NavItem
                {
                    Title = "工作状态",
                    Description = "保存与恢复打开的程序",
                    Glyph = "\uE74E",
                    Page = WorkState
                },
                new NavItem
                {
                    Title = "启动项",
                    Description = "管理开机自启动项目",
                    Glyph = "\uE7E8",
                    Page = Autostart
                },
                new NavItem
                {
                    Title = "白名单",
                    Description = "保存快照时忽略的程序",
                    Glyph = "\uE73E",
                    Page = Whitelist
                },
                new NavItem
                {
                    Title = "设置",
                    Description = "自启偏好与数据位置",
                    Glyph = "\uE713",
                    Page = Settings
                }
            };

            SelectedNavItem = NavItems[0];
        }

        public ObservableCollection<NavItem> NavItems { get; }

        public WorkStateViewModel WorkState { get; }

        public AutostartViewModel Autostart { get; }

        public WhitelistViewModel Whitelist { get; }

        public SettingsViewModel Settings { get; }

        public NavItem SelectedNavItem
        {
            get { return _selectedNavItem; }
            set
            {
                if (SetProperty(ref _selectedNavItem, value))
                {
                    OnPropertyChanged(nameof(CurrentPage));
                    OnPropertyChanged(nameof(CurrentTitle));

                    // 切换页面时刷新依赖数据库的数据，保证显示最新内容
                    (value?.Page as WhitelistViewModel)?.Reload();
                    (value?.Page as SettingsViewModel)?.ReloadSnapshots();
                }
            }
        }

        public object CurrentPage
        {
            get { return _selectedNavItem?.Page; }
        }

        public string CurrentTitle
        {
            get { return _selectedNavItem?.Title ?? AppPaths.AppDisplayName; }
        }

        public string DatabasePath
        {
            get { return AppPaths.DatabasePath; }
        }
    }
}
