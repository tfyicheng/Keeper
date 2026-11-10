using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Keeper.Data;
using Keeper.Models;

namespace Keeper.Services
{
    /// <summary>
    /// 快照恢复结果。
    /// </summary>
    public class RestoreResult
    {
        public RestoreResult()
        {
            Failures = new List<string>();
        }

        public int Started { get; set; }

        public int Skipped { get; set; }

        public IList<string> Failures { get; set; }
    }

    /// <summary>
    /// 快照条目增删结果。
    /// </summary>
    public class SnapshotUpdateResult
    {
        public SnapshotUpdateResult()
        {
            Added = new List<SnapshotItem>();
        }

        /// <summary>
        /// 成功追加的条目。
        /// </summary>
        public IList<SnapshotItem> Added { get; private set; }

        /// <summary>
        /// 被跳过的数量（重复、文件夹或文件不存在）。
        /// </summary>
        public int Skipped { get; set; }
    }

    /// <summary>
    /// 工作状态快照的保存与恢复。
    /// </summary>
    public class SnapshotService
    {
        private const int MaxSnapshots = 30;

        private readonly SnapshotRepository _repository = new SnapshotRepository();
        private readonly RunningAppService _runningAppService = new RunningAppService();

        /// <summary>
        /// 保存当前工作状态为一条新快照。
        /// </summary>
        public SnapshotInfo SaveCurrent(string name)
        {
            var apps = _runningAppService.Capture();
            var items = apps.Select(app => new SnapshotItem
            {
                ExecutablePath = app.ExecutablePath,
                DisplayName = app.DisplayName,
                Arguments = string.Empty,
                LaunchTarget = app.LaunchTarget ?? string.Empty
            }).ToList();

            var snapshot = new SnapshotInfo
            {
                Name = string.IsNullOrWhiteSpace(name) ? "工作状态 " + DateTime.Now.ToString("MM-dd HH:mm") : name.Trim(),
                CreatedAt = DateTime.Now,
                ItemCount = items.Count,
                Items = items
            };

            snapshot.Id = _repository.Insert(snapshot, items);
            _repository.Trim(MaxSnapshots);
            return snapshot;
        }

        /// <summary>
        /// 向快照追加文件（exe、文档、图片等任意格式），重复或无效路径会被跳过。
        /// </summary>
        public SnapshotUpdateResult AddFiles(SnapshotInfo snapshot, IEnumerable<string> paths)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var result = new SnapshotUpdateResult();
            if (paths == null)
            {
                return result;
            }

            var existing = new HashSet<string>(
                _repository.GetItems(snapshot.Id).Select(item => item.ExecutablePath),
                StringComparer.OrdinalIgnoreCase);

            foreach (var rawPath in paths)
            {
                if (string.IsNullOrWhiteSpace(rawPath))
                {
                    continue;
                }

                string path;
                try
                {
                    path = Path.GetFullPath(rawPath);
                }
                catch
                {
                    result.Skipped++;
                    continue;
                }

                if (Directory.Exists(path) || !File.Exists(path) || !existing.Add(path))
                {
                    result.Skipped++;
                    continue;
                }

                var item = new SnapshotItem
                {
                    SnapshotId = snapshot.Id,
                    ExecutablePath = path,
                    Arguments = string.Empty,
                    DisplayName = GetDisplayName(path)
                };

                result.Added.Add(_repository.InsertItem(item));
            }

            return result;
        }

        /// <summary>
        /// 从快照中移除一个启动项。
        /// </summary>
        public void RemoveItem(SnapshotItem item)
        {
            if (item == null)
            {
                return;
            }

            _repository.DeleteItem(item.Id);
        }

        /// <summary>
        /// 生成条目展示名称：可执行文件取文件说明，其它格式取文件名。
        /// </summary>
        public static string GetDisplayName(string path)
        {
            try
            {
                if (string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var info = FileVersionInfo.GetVersionInfo(path);
                    if (!string.IsNullOrWhiteSpace(info.FileDescription))
                    {
                        return info.FileDescription.Trim();
                    }
                }
            }
            catch
            {
                // 忽略读取失败
            }

            return Path.GetFileNameWithoutExtension(path);
        }

        /// <summary>
        /// 恢复指定快照中的程序。
        /// </summary>
        public RestoreResult Restore(SnapshotInfo snapshot, bool skipAlreadyRunning)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (snapshot.Items == null || snapshot.Items.Count == 0)
            {
                snapshot.Items = _repository.GetItems(snapshot.Id);
            }

            var result = new RestoreResult();
            var running = skipAlreadyRunning
                ? _runningAppService.GetRunningExecutablePaths()
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in snapshot.Items)
            {
                var usesShellTarget = !string.IsNullOrWhiteSpace(item.LaunchTarget);

                if (string.IsNullOrWhiteSpace(item.ExecutablePath) ||
                    (!usesShellTarget && !File.Exists(item.ExecutablePath)))
                {
                    result.Failures.Add((item.DisplayName ?? item.ExecutablePath) + "（文件不存在）");
                    continue;
                }

                if (skipAlreadyRunning && running.Contains(item.ExecutablePath))
                {
                    result.Skipped++;
                    continue;
                }

                try
                {
                    // 打包应用（WindowsApps）不能直接启动 exe，交给资源管理器按 AppUserModelID 打开
                    var startInfo = usesShellTarget
                        ? new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = item.LaunchTarget,
                            UseShellExecute = true
                        }
                        : new ProcessStartInfo
                        {
                            FileName = item.ExecutablePath,
                            Arguments = item.Arguments ?? string.Empty,
                            UseShellExecute = true,
                            WorkingDirectory = Path.GetDirectoryName(item.ExecutablePath) ?? string.Empty
                        };

                    Process.Start(startInfo);
                    result.Started++;
                    running.Add(item.ExecutablePath);
                }
                catch (Exception ex)
                {
                    result.Failures.Add((item.DisplayName ?? item.ExecutablePath) + "（" + ex.Message + "）");
                }
            }

            return result;
        }
    }
}
