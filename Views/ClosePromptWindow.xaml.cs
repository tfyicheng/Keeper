using System.Windows;
using System.Windows.Input;

namespace Keeper.Views
{
    /// <summary>
    /// 第一次关闭窗口时的询问：退出程序还是退到托盘。
    /// </summary>
    public partial class ClosePromptWindow : Window
    {
        public ClosePromptWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 用户是否选择退到托盘。
        /// </summary>
        public bool MinimizeToTray { get; private set; }

        /// <summary>
        /// 是否记住本次选择。
        /// </summary>
        public bool RememberChoice
        {
            get { return RememberBox.IsChecked == true; }
        }

        /// <summary>
        /// 是否取消关闭（不退出也不隐藏）。
        /// </summary>
        public bool IsCancelled { get; private set; } = true;

        private void Tray_Click(object sender, RoutedEventArgs e)
        {
            MinimizeToTray = true;
            IsCancelled = false;
            Close();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            MinimizeToTray = false;
            IsCancelled = false;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            IsCancelled = true;
            Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                IsCancelled = true;
                Close();
            }
        }
    }
}
