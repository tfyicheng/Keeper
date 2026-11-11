using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Keeper.Data;
using Keeper.Helpers;
using Keeper.Infrastructure;
using Keeper.Models;
using Keeper.Services;
using Microsoft.Win32;

namespace Keeper.ViewModels
{
    /// <summary>
    /// 白名单：这里的程序在保存快照时会被忽略。
    /// </summary>
    public class WhitelistViewModel : ObservableObject
    {
        private readonly WhitelistRepository _repository = new WhitelistRepository();
        private readonly AutostartService _autostartService = new AutostartService();
        private string _statusText = string.Empty;

        public WhitelistViewModel()
        {
            Entries = new ObservableCollection<WhitelistEntry>();

            AddFilesCommand = new RelayCommand(async () => await PickFilesAsync());
            AddCheckedStartupItemsCommand = new RelayCommand(async () => await AddCheckedStartupItemsAsync());
            ClearAllCommand = new RelayCommand(ClearAll, () => Entries.Count > 0);
            RemoveCommand = new RelayCommand(async parameter => await RemoveAsync(parameter as WhitelistEntry));
            OpenFolderCommand = new RelayCommand(parameter => OpenFolder(parameter as WhitelistEntry));

            Reload();
        }

        public ObservableCollection<WhitelistEntry> Entries { get; }

        public ICommand AddFilesCommand { get; }

        /// <summary>
        /// 把「启动项」页中已勾选（启用）的项目批量加入白名单。
        /// </summary>
        public ICommand AddCheckedStartupItemsCommand { get; }

        /// <summary>
        /// 一键清空白名单。
        /// </summary>
        public ICommand ClearAllCommand { get; }

        public ICommand RemoveCommand { get; }

        public ICommand OpenFolderCommand { get; }

        public string StatusText
        {
            get { return _statusText; }
            private set { SetProperty(ref _statusText, value); }
        }

        /// <summary>
        /// 重新读取白名单列表。
        /// </summary>
        public void Reload()
        {
            Entries.Clear();
            foreach (var entry in _repository.GetAll())
            {
                Entries.Add(entry);
            }

            StatusText = Entries.Count == 0
                ? "白名单为空。加入白名单的程序，保存快照时不会被记录。"
                : "共 " + Entries.Count + " 个白名单程序，保存快照时将自动忽略。";

            CommandManager.InvalidateRequerySuggested();
        }

        /// <summary>
        /// 追加文件到白名单。
        /// </summary>
        public async Task AddFilesAsync(IEnumerable<string> paths)
        {
            var files = new List<string>();
            var unresolved = new List<string>();
            foreach (var path in paths ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    continue;
                }

                // 快捷方式按指向的目标记录，否则白名单永远匹配不到运行中的程序
                var target = ShortcutHelper.ResolveTarget(path);
                if (string.IsNullOrWhiteSpace(target) || !File.Exists(target))
                {
                    unresolved.Add(Path.GetFileNameWithoutExtension(path));
                    continue;
                }

                var fullPath = Path.GetFullPath(target);
                if (!files.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
                {
                    files.Add(fullPath);
                }
            }

            if (files.Count == 0)
            {
                if (unresolved.Count > 0)
                {
                    StatusText = "无法解析快捷方式的目标，已跳过：" + string.Join("、", unresolved) + "。";
                }

                return;
            }

            try
            {
                var added = await Task.Run(() =>
                {
                    var count = 0;
                    foreach (var file in files)
                    {
                        if (_repository.Add(file, SnapshotService.GetDisplayName(file)))
                        {
                            count++;
                        }
                    }

                    return count;
                });

                Reload();
                StatusText = added == 0
                    ? "这些文件已经在白名单中。"
                    : "已添加 " + added + " 个程序到白名单，保存快照时将忽略它们。";

                if (unresolved.Count > 0)
                {
                    StatusText += " 已跳过无法解析目标的快捷方式：" + string.Join("、", unresolved) + "。";
                }
            }
            catch (Exception ex)
            {
                StatusText = "添加失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 一键清空白名单（二次确认）。
        /// </summary>
        private void ClearAll()
        {
            if (Entries.Count == 0)
            {
                return;
            }

            var confirm = MessageBox.Show(
                "确定要清空白名单中的 " + Entries.Count + " 个程序吗？此操作不可撤销。",
                "清空白名单",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _repository.DeleteAll();
                Entries.Clear();
                StatusText = "已清空白名单，保存快照时将记录所有程序。";
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                StatusText = "清空失败：" + ex.Message;
            }
        }

        private async Task PickFilesAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择要加入白名单的程序",
                Filter = "所有文件|*.*",
                Multiselect = true,
                CheckFileExists = true,
                // 不解析快捷方式，否则指向商店应用等特殊目标的 .lnk 会无法选中
                DereferenceLinks = false
            };

            if (dialog.ShowDialog() == true)
            {
                await AddFilesAsync(dialog.FileNames);
            }
        }

        /// <summary>
        /// 把「启动项」页中已勾选（启用）的项目一键加入白名单。
        /// </summary>
        private async Task AddCheckedStartupItemsAsync()
        {
            StatusText = "正在读取已勾选的启动项…";

            try
            {
                var files = await Task.Run(() => _autostartService.Scan()
                    .Where(entry => entry.IsEnabled)
                    .Select(ResolveExecutablePath)
                    .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList());

                if (files.Count == 0)
                {
                    StatusText = "没有找到已勾选且可以加入白名单的启动项。";
                    return;
                }

                var added = await Task.Run(() =>
                {
                    var count = 0;
                    foreach (var file in files)
                    {
                        if (_repository.Add(file, SnapshotService.GetDisplayName(file)))
                        {
                            count++;
                        }
                    }

                    return count;
                });

                Reload();
                StatusText = added == 0
                    ? "已勾选的 " + files.Count + " 个启动项都已在白名单中。"
                    : "已把 " + added + " 个已勾选的启动项加入白名单，保存快照时将忽略它们。";
            }
            catch (Exception ex)
            {
                StatusText = "追加失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 启动项最终指向的可执行文件路径（快捷方式取目标程序）。
        /// </summary>
        private static string ResolveExecutablePath(AutostartEntry entry)
        {
            var path = entry.TargetPath;
            if (string.IsNullOrWhiteSpace(path) ||
                !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                path = CommandLineHelper.GetExecutablePath(entry.Command);
            }

            return path;
        }

        /// <summary>
        /// 在资源管理器中定位该白名单程序。
        /// </summary>
        private void OpenFolder(WhitelistEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.ExecutablePath))
            {
                return;
            }

            try
            {
                var arguments = File.Exists(entry.ExecutablePath)
                    ? "/select,\"" + entry.ExecutablePath + "\""
                    : "\"" + (Path.GetDirectoryName(entry.ExecutablePath) ?? entry.ExecutablePath) + "\"";

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = arguments,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusText = "打开文件夹失败：" + ex.Message;
            }
        }

        private async Task RemoveAsync(WhitelistEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            try
            {
                await Task.Run(() => _repository.Delete(entry.Id));
                Entries.Remove(entry);
                StatusText = "已从白名单移除「" + (string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.FileName : entry.DisplayName) + "」。";
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                StatusText = "移除失败：" + ex.Message;
            }
        }
    }
}
