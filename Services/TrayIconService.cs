using System;
using System.Drawing;
using System.Windows.Forms;
using Keeper.Helpers;

namespace Keeper.Services
{
    /// <summary>
    /// 系统托盘图标与右键菜单。
    /// </summary>
    public class TrayIconService : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly Icon _icon;
        private readonly bool _ownsIcon;

        public TrayIconService(Action openMainWindow, Action saveState, Action saveAndRestart, Action exitApplication)
        {
            _icon = ExtractIcon(out _ownsIcon);

            var menu = new ContextMenuStrip
            {
                Font = new Font("Microsoft YaHei UI", 9F),
                ShowImageMargin = false
            };
            menu.Items.Add(CreateMenuItem("打开主界面", openMainWindow));
            menu.Items.Add(CreateMenuItem("保存当前状态", saveState));
            menu.Items.Add(CreateMenuItem("保存当前状态并重启", saveAndRestart));
            menu.Items.Add(CreateMenuItem("退出程序", exitApplication));

            _notifyIcon = new NotifyIcon
            {
                Icon = _icon,
                Text = "Keeper 工作状态守护",
                ContextMenuStrip = menu,
                Visible = true
            };
            _notifyIcon.DoubleClick += (sender, args) => openMainWindow();
        }

        /// <summary>
        /// 在托盘弹出气泡提示。
        /// </summary>
        public void ShowBalloon(string title, string text)
        {
            try
            {
                _notifyIcon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
            }
            catch
            {
                // 气泡提示失败不影响主流程
            }
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();

            if (_ownsIcon)
            {
                _icon?.Dispose();
            }
        }

        private static ToolStripMenuItem CreateMenuItem(string text, Action action)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (sender, args) => action();
            return item;
        }

        private static Icon ExtractIcon(out bool ownsIcon)
        {
            try
            {
                var icon = Icon.ExtractAssociatedIcon(AppPaths.ExecutablePath);
                if (icon != null)
                {
                    ownsIcon = true;
                    return icon;
                }
            }
            catch
            {
                // 忽略，使用系统默认图标
            }

            ownsIcon = false;
            return SystemIcons.Application;
        }
    }
}
