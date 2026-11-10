using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Keeper.Data;
using Keeper.Helpers;
using Keeper.Services;
using Keeper.ViewModels;

namespace Keeper
{
    /// <summary>
    /// 应用入口：初始化数据库、创建主窗口与托盘图标，并按设置自动恢复工作状态。
    /// </summary>
    public partial class App : Application
    {
        private const string InstanceMutexName = @"Local\Keeper.SingleInstance";
        private const string ActivateEventName = @"Local\Keeper.SingleInstance.Activate";

        private MainViewModel _viewModel;
        private MainWindow _mainWindow;
        private TrayIconService _trayIcon;
        private bool _trayTipShown;
        private Mutex _instanceMutex;
        private bool _ownsMutex;
        private EventWaitHandle _activateEvent;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 单实例：已有进程在运行时，通知它把主界面叫到前台，然后退出
            _instanceMutex = new Mutex(true, InstanceMutexName, out _ownsMutex);
            if (!_ownsMutex)
            {
                ActivateRunningInstance();
                Shutdown();
                return;
            }

            base.OnStartup(e);
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            StartActivationListener();

            try
            {
                Database.Initialize();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "无法在程序目录创建数据文件：" + AppPaths.DatabasePath + Environment.NewLine + ex.Message +
                    Environment.NewLine + Environment.NewLine + "请将 Keeper.exe 放到有写入权限的目录后重试。",
                    AppPaths.AppDisplayName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            _viewModel = new MainViewModel();
            _mainWindow = new MainWindow { DataContext = _viewModel };
            MainWindow = _mainWindow;
            _mainWindow.Show();

            _trayIcon = new TrayIconService(ShowMainWindow, SaveFromTray, SaveAndRestart, ExitApplication);

            if (_viewModel.Settings.AutoRestoreOnLaunch)
            {
                Dispatcher.BeginInvoke(
                    new Action(async () => await RestoreOnStartupAsync(_viewModel)),
                    DispatcherPriority.Background);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayIcon?.Dispose();
            _activateEvent?.Dispose();

            if (_ownsMutex)
            {
                try
                {
                    _instanceMutex.ReleaseMutex();
                }
                catch
                {
                    // 进程即将退出，释放失败无需处理
                }
            }

            _instanceMutex?.Dispose();
            base.OnExit(e);
        }

        /// <summary>
        /// 监听其它实例的激活请求，收到后把主界面显示出来。
        /// </summary>
        private void StartActivationListener()
        {
            try
            {
                _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            }
            catch
            {
                return;
            }

            var thread = new Thread(() =>
            {
                while (true)
                {
                    _activateEvent.WaitOne();
                    Dispatcher.BeginInvoke(new Action(ShowMainWindow));
                }
            })
            {
                IsBackground = true,
                Name = "Keeper.ActivationListener"
            };
            thread.Start();
        }

        /// <summary>
        /// 通知已在运行的 Keeper 显示主界面。
        /// </summary>
        private void ActivateRunningInstance()
        {
            try
            {
                using (var handle = EventWaitHandle.OpenExisting(ActivateEventName))
                {
                    handle.Set();
                    return;
                }
            }
            catch
            {
                // 已有实例还没准备好事件对象时退化为提示
            }

            MessageBox.Show(
                "Keeper 已经在运行，请通过托盘图标打开主界面。",
                AppPaths.AppDisplayName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        /// <summary>
        /// 从托盘恢复主界面。
        /// </summary>
        public void ShowMainWindow()
        {
            if (_mainWindow == null)
            {
                return;
            }

            _mainWindow.ShowInTaskbar = true;
            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }

            _mainWindow.Activate();
            _mainWindow.Topmost = true;
            _mainWindow.Topmost = false;
            _mainWindow.Focus();
        }

        /// <summary>
        /// 主窗口第一次隐藏到托盘时给出提示。
        /// </summary>
        public void NotifyMinimizedToTray()
        {
            if (_trayTipShown)
            {
                return;
            }

            _trayTipShown = true;
            _trayIcon?.ShowBalloon("Keeper 仍在运行", "双击托盘图标可重新打开主界面，右键可保存工作状态或退出程序。");
        }

        private async Task RestoreOnStartupAsync(MainViewModel viewModel)
        {
            try
            {
                // 优先恢复设置中选中的快照；未选择时回退到最近一次快照
                var targets = viewModel.Settings.GetStartupSnapshots();
                if (targets.Count > 0)
                {
                    await viewModel.WorkState.RestoreSnapshotsAsync(targets);
                }
                else
                {
                    await viewModel.WorkState.RestoreLatestAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "自动恢复工作状态失败：" + ex.Message,
                    AppPaths.AppDisplayName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private async void SaveFromTray()
        {
            await _viewModel.WorkState.SaveAsync();
            _trayIcon?.ShowBalloon("已保存当前工作状态", _viewModel.WorkState.StatusText);
        }

        private async void SaveAndRestart()
        {
            await _viewModel.WorkState.SaveAsync();

            var message = _viewModel.WorkState.StatusText + Environment.NewLine + Environment.NewLine +
                          "是否立即重启电脑？重启会关闭当前所有程序，请先保存其它软件中未保存的内容。";
            var confirm = MessageBox.Show(message, "保存并重启", MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK)
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "shutdown.exe",
                    Arguments = "/r /t 5 /c \"Keeper：正在重启电脑\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("重启失败：" + ex.Message, AppPaths.AppDisplayName,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExitApplication()
        {
            if (_mainWindow != null)
            {
                _mainWindow.ExitWithoutPrompt();
            }
            else
            {
                Shutdown();
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                "发生未处理的异常：" + e.Exception.Message,
                AppPaths.AppDisplayName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
