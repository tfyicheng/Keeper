using System;
using System.IO;
using System.Reflection;

namespace Keeper.Helpers
{
    /// <summary>
    /// 统一管理程序中用到的路径，数据文件固定放在程序所在目录。
    /// </summary>
    public static class AppPaths
    {
        public const string AppDisplayName = "Keeper";
        public const string DatabaseFileName = "Keeper.db";

        public static string BaseDirectory
        {
            get { return AppDomain.CurrentDomain.BaseDirectory; }
        }

        /// <summary>
        /// SQLite 数据文件路径（与程序同目录）。
        /// </summary>
        public static string DatabasePath
        {
            get { return Path.Combine(BaseDirectory, DatabaseFileName); }
        }

        public static string ExecutablePath
        {
            get
            {
                var location = Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrEmpty(location))
                {
                    location = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                }

                return location;
            }
        }
    }
}
