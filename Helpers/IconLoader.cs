using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using DrawingIcon = System.Drawing.Icon;

namespace Keeper.Helpers
{
    /// <summary>
    /// 从可执行文件 / 快捷方式中提取图标，供界面展示。
    /// </summary>
    public static class IconLoader
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, BitmapSource> Cache =
            new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 加载指定文件的图标，失败时返回 null。
        /// </summary>
        public static BitmapSource Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            lock (SyncRoot)
            {
                BitmapSource cached;
                if (Cache.TryGetValue(path, out cached))
                {
                    return cached;
                }
            }

            var icon = Extract(path);

            lock (SyncRoot)
            {
                Cache[path] = icon;
            }

            return icon;
        }

        private static BitmapSource Extract(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var target = ShortcutHelper.ResolveTarget(path) ?? path;
                if (!File.Exists(target))
                {
                    target = path;
                }

                using (DrawingIcon icon = DrawingIcon.ExtractAssociatedIcon(target))
                {
                    if (icon == null)
                    {
                        return null;
                    }

                    var source = Imaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
