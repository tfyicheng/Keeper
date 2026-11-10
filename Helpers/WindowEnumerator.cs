using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Keeper.Helpers
{
    /// <summary>
    /// 枚举顶层窗口，用于识别只有托盘图标、没有可见主窗口的程序（如微信、Claunch、远控PC）。
    /// </summary>
    public static class WindowEnumerator
    {
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

        /// <summary>
        /// 系统 / 输入法使用的消息窗口类名，这类窗口不代表用户程序。
        /// </summary>
        private static readonly HashSet<string> IgnoredClassNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "IME",
                "MSCTFIME UI",
                "Default IME",
                "GDI+ Hook Window Class",
                "SysShadow",
                "ForegroundStaging",
                "ApplicationFrameWindow",
                "Windows.UI.Core.CoreWindow"
            };

        /// <summary>
        /// 托盘程序创建的隐藏宿主窗口类名。这类窗口通常没有标题，
        /// 但只要有它，就说明该进程是带托盘图标的用户程序（远控PC、CLaunch、Everything、TranslucentTB 等）。
        /// </summary>
        private static readonly HashSet<string> TrayHostClassNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Electron_NotifyIconHostWindow",
                "Chrome_WidgetWin_1",
                "EVERYTHING_TASKBAR_NOTIFICATION",
                "TRAYWINDOW",
                "TXGuiFoundation",
                "tooltips_class32"
            };

        /// <summary>
        /// WinForms / Qt 托盘程序的隐藏窗口类名使用前缀匹配。
        /// </summary>
        private static readonly string[] TrayHostClassPrefixes =
        {
            "WindowsForms10.Window",
            "Qt5QWindow",
            "Qt6QWindow"
        };

        /// <summary>
        /// 返回拥有用户程序窗口的进程 ID 集合（包含最小化到托盘、窗口被隐藏的程序）。
        /// </summary>
        public static HashSet<int> GetProcessIdsWithWindows()
        {
            return CollectProcessIds(IsApplicationWindow);
        }

        /// <summary>
        /// 返回拥有托盘宿主窗口的进程 ID 集合（打包应用用它判断是否为托盘程序）。
        /// </summary>
        public static HashSet<int> GetTrayHostProcessIds()
        {
            return CollectProcessIds(IsTrayHostWindow);
        }

        private static HashSet<int> CollectProcessIds(Func<IntPtr, bool> predicate)
        {
            var result = new HashSet<int>();

            try
            {
                EnumWindows((hWnd, lParam) =>
                {
                    uint processId;
                    GetWindowThreadProcessId(hWnd, out processId);

                    if (processId != 0 && predicate(hWnd))
                    {
                        result.Add((int)processId);
                    }

                    return true;
                }, IntPtr.Zero);
            }
            catch
            {
                // 枚举失败时按“没有窗口”处理
            }

            return result;
        }

        private static bool IsTrayHostWindow(IntPtr hWnd)
        {
            return !IsWindowVisible(hWnd) && IsTrayHostClass(GetClassNameOf(hWnd));
        }

        private static bool IsApplicationWindow(IntPtr hWnd)
        {
            var className = GetClassNameOf(hWnd);
            if (IgnoredClassNames.Contains(className))
            {
                return false;
            }

            if (IsWindowVisible(hWnd))
            {
                return true;
            }

            // 托盘宿主窗口：隐藏且没有标题，但属于用户程序。
            if (IsTrayHostClass(className))
            {
                return true;
            }

            // 其它隐藏窗口：有标题说明是程序自己的主窗口（最小化到托盘），
            // 系统辅助窗口通常没有标题。工具窗口同样可能是托盘程序的主窗口，这里不再排除。
            return GetWindowTextLength(hWnd) > 0;
        }

        private static bool IsTrayHostClass(string className)
        {
            if (TrayHostClassNames.Contains(className))
            {
                return true;
            }

            foreach (var prefix in TrayHostClassPrefixes)
            {
                if (className.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetClassNameOf(IntPtr hWnd)
        {
            var buffer = new StringBuilder(256);
            var length = GetClassName(hWnd, buffer, buffer.Capacity);
            return length <= 0 ? string.Empty : buffer.ToString();
        }
    }
}
