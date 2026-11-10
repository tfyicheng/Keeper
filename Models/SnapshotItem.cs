using System.Windows.Media.Imaging;
using Keeper.Helpers;

namespace Keeper.Models
{
    /// <summary>
    /// 快照中记录的一个程序。
    /// </summary>
    public class SnapshotItem
    {
        private BitmapSource _icon;

        public long Id { get; set; }

        public long SnapshotId { get; set; }

        public string ExecutablePath { get; set; }

        public string Arguments { get; set; }

        /// <summary>
        /// 打包应用（WindowsApps）的启动目标，形如 shell:AppsFolder\AUMID；普通程序为空。
        /// </summary>
        public string LaunchTarget { get; set; }

        public string DisplayName { get; set; }

        public BitmapSource Icon
        {
            get
            {
                if (_icon == null)
                {
                    _icon = IconLoader.Load(ExecutablePath);
                }

                return _icon;
            }
        }

        public string FileName
        {
            get { return System.IO.Path.GetFileName(ExecutablePath); }
        }
    }
}
