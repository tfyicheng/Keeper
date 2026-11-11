using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace Keeper.Helpers
{
    /// <summary>
    /// 判断程序是否要求以管理员身份运行（读取 exe 内嵌清单中的 requestedExecutionLevel）。
    /// </summary>
    public static class ElevationHelper
    {
        private const uint LoadLibraryAsDataFile = 0x00000002;
        private const int ManifestResourceType = 24;

        private static readonly IntPtr ManifestResourceId = new IntPtr(1);

        private static readonly ConcurrentDictionary<string, bool> Cache =
            new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr module);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint SizeofResource(IntPtr module, IntPtr resource);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LockResource(IntPtr resourceData);

        /// <summary>
        /// 程序清单中是否声明了 requireAdministrator；快捷方式按指向的目标判断。
        /// </summary>
        public static bool RequiresAdministrator(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            return Cache.GetOrAdd(executablePath, path => ReadRequiresAdministrator(ResolveExecutablePath(path)));
        }

        /// <summary>
        /// 快捷方式解析为指向的目标，其它文件原样返回。
        /// </summary>
        private static string ResolveExecutablePath(string path)
        {
            if (!string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            return ShortcutHelper.ResolveTarget(path) ?? path;
        }

        private static bool ReadRequiresAdministrator(string executablePath)
        {
            if (!string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(executablePath))
            {
                return false;
            }

            try
            {
                var manifest = ReadManifest(executablePath);
                if (string.IsNullOrWhiteSpace(manifest))
                {
                    return false;
                }

                // 清单可能带 UTF-8 BOM，直接交给 XDocument 会解析失败
                var level = XDocument.Parse(manifest.TrimStart('\uFEFF'))
                    .Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "requestedExecutionLevel")
                    ?.Attribute("level")
                    ?.Value;

                return string.Equals(level, "requireAdministrator", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string ReadManifest(string executablePath)
        {
            var module = LoadLibraryEx(executablePath, IntPtr.Zero, LoadLibraryAsDataFile);
            if (module == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var resource = FindResource(module, ManifestResourceId, new IntPtr(ManifestResourceType));
                if (resource == IntPtr.Zero)
                {
                    return null;
                }

                var size = (int)SizeofResource(module, resource);
                if (size <= 0)
                {
                    return null;
                }

                var handle = LoadResource(module, resource);
                if (handle == IntPtr.Zero)
                {
                    return null;
                }

                var data = LockResource(handle);
                if (data == IntPtr.Zero)
                {
                    return null;
                }

                var bytes = new byte[size];
                Marshal.Copy(data, bytes, 0, size);

                // 清单通常是 UTF-8，个别是带 BOM 的 UTF-16
                if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                {
                    return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
                }

                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                FreeLibrary(module);
            }
        }
    }
}
