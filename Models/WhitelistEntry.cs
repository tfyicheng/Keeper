using System.IO;
using System.Windows.Media.Imaging;
using Keeper.Helpers;

namespace Keeper.Models
{
    /// <summary>
    /// 白名单中的一个程序：保存快照时会被忽略。
    /// </summary>
    public class WhitelistEntry
    {
        private BitmapSource _icon;

        public long Id { get; set; }

        public string ExecutablePath { get; set; }

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
            get { return Path.GetFileName(ExecutablePath); }
        }
    }
}
