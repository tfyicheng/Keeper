using System;
using System.IO;

namespace Keeper.Helpers
{
    /// <summary>
    /// 启动项命令行解析工具。
    /// </summary>
    public static class CommandLineHelper
    {
        /// <summary>
        /// 从命令行中提取可执行文件路径。
        /// </summary>
        public static string GetExecutablePath(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return null;
            }

            var text = commandLine.Trim();

            // 带引号的形式："C:\Program Files\App\app.exe" -arg
            if (text.StartsWith("\"", StringComparison.Ordinal))
            {
                var end = text.IndexOf('"', 1);
                if (end > 1)
                {
                    var quoted = text.Substring(1, end - 1);
                    return Environment.ExpandEnvironmentVariables(quoted);
                }

                return null;
            }

            // 无引号但本身就是一个完整存在的路径
            var expanded = Environment.ExpandEnvironmentVariables(text);
            if (File.Exists(expanded))
            {
                return expanded;
            }

            // 无引号：取第一个空格前的内容
            var spaceIndex = text.IndexOf(' ');
            var candidate = spaceIndex > 0 ? text.Substring(0, spaceIndex) : text;
            return Environment.ExpandEnvironmentVariables(candidate);
        }

        /// <summary>
        /// 从命令行中提取参数部分。
        /// </summary>
        public static string GetArguments(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return string.Empty;
            }

            var text = commandLine.Trim();
            if (text.StartsWith("\"", StringComparison.Ordinal))
            {
                var end = text.IndexOf('"', 1);
                return end > 0 && end + 1 < text.Length ? text.Substring(end + 1).Trim() : string.Empty;
            }

            var expanded = Environment.ExpandEnvironmentVariables(text);
            if (File.Exists(expanded))
            {
                return string.Empty;
            }

            var spaceIndex = text.IndexOf(' ');
            return spaceIndex > 0 ? text.Substring(spaceIndex + 1).Trim() : string.Empty;
        }
    }
}
