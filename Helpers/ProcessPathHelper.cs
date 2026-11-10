using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Keeper.Helpers
{
    /// <summary>
    /// 读取进程信息。以管理员权限运行的进程无法通过 Process.MainModule 读取路径，
    /// 这里改用 QueryFullProcessImageName 作为兜底，并支持读取打包应用（AppUserModelId）。
    /// </summary>
    public static class ProcessPathHelper
    {
        private const int ProcessQueryLimitedInformation = 0x1000;
        private const int ErrorInsufficientBuffer = 122;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder buffer,
            ref int size);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetApplicationUserModelId(IntPtr process, ref int length, StringBuilder aumid);

        /// <summary>
        /// 获取进程可执行文件完整路径，失败时返回 null。
        /// </summary>
        public static string GetExecutablePath(Process process)
        {
            if (process == null)
            {
                return null;
            }

            try
            {
                var path = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    return path;
                }
            }
            catch
            {
                // 管理员权限进程会拒绝访问，继续走下面的兜底接口
            }

            var handle = OpenProcess(ProcessQueryLimitedInformation, false, process.Id);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var buffer = new StringBuilder(1024);
                var size = buffer.Capacity;
                return QueryFullProcessImageName(handle, 0, buffer, ref size)
                    ? buffer.ToString(0, size)
                    : null;
            }
            catch
            {
                return null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>
        /// 获取进程的应用模型 ID（打包应用才有），失败时返回 null。
        /// </summary>
        public static string GetApplicationUserModelId(Process process)
        {
            if (process == null)
            {
                return null;
            }

            var handle = OpenProcess(ProcessQueryLimitedInformation, false, process.Id);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var length = 256;
                var buffer = new StringBuilder(length);
                var result = GetApplicationUserModelId(handle, ref length, buffer);
                if (result == 0)
                {
                    return buffer.ToString();
                }

                if (result != ErrorInsufficientBuffer)
                {
                    return null;
                }

                buffer = new StringBuilder(length);
                result = GetApplicationUserModelId(handle, ref length, buffer);
                return result == 0 ? buffer.ToString() : null;
            }
            catch
            {
                return null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }
}
