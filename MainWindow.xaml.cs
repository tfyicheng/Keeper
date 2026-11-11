using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Keeper.Data;
using Keeper.Views;

namespace Keeper
{
    /// <summary>
    /// 主窗口：左侧导航 + 右侧内容，单页承载三个功能模块。
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string CloseActionAsk = "Ask";
        private const string CloseActionTray = "Tray";
        private const string CloseActionExit = "Exit";

        private readonly SettingsRepository _settings = new SettingsRepository();
        private bool _allowClose;

        public MainWindow()
        {
            InitializeComponent();
            KeyDown += MainWindow_KeyDown;
        }

        /// <summary>
        /// 按 Esc 等同于点击关闭按钮。
        /// </summary>
        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            // 下拉框、弹窗等控件自行处理 Esc 时不重复响应
            if (e.Handled || e.Key != Key.Escape)
            {
                return;
            }

            // 有模态窗口打开时交给模态窗口处理
            if (OwnedWindows.Cast<Window>().Any(window => window.IsVisible))
            {
                return;
            }

            e.Handled = true;
            Close();
        }

        /// <summary>
        /// 隐藏到系统托盘。
        /// </summary>
        public void HideToTray()
        {
            ShowInTaskbar = false;
            Hide();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_allowClose)
            {
                base.OnClosing(e);
                return;
            }

            var action = _settings.Get(SettingsKeys.CloseAction, CloseActionAsk);

            if (string.Equals(action, CloseActionAsk, StringComparison.OrdinalIgnoreCase))
            {
                var dialog = new ClosePromptWindow { Owner = this };
                dialog.ShowDialog();

                if (dialog.IsCancelled)
                {
                    e.Cancel = true;
                    return;
                }

                action = dialog.MinimizeToTray ? CloseActionTray : CloseActionExit;
                if (dialog.RememberChoice)
                {
                    _settings.Set(SettingsKeys.CloseAction, action);
                }
            }

            if (string.Equals(action, CloseActionExit, StringComparison.OrdinalIgnoreCase))
            {
                // 放行关闭，ShutdownMode 为 OnLastWindowClose，窗口关闭后应用即退出
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            HideToTray();
            (Application.Current as App)?.NotifyMinimizedToTray();
        }

        /// <summary>
        /// 托盘「退出程序」：跳过关闭确认直接退出。
        /// </summary>
        public void ExitWithoutPrompt()
        {
            _allowClose = true;
            Close();
        }
    }
}
