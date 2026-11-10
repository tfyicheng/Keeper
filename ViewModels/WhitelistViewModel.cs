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
        }

        /// <summary>
        /// 追加文件到白名单（支持拖拽任意格式）。
        /// </summary>
        public async Task AddFilesAsync(IEnumerable<string> paths)
        {
            var files = (paths ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Select(path => Path.GetFullPath(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (files.Count == 0)
            {
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
            }
            catch (Exception ex)
            {
                StatusText = "添加失败：" + ex.Message;
            }
        }

        private async Task PickFilesAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择要加入白名单的程序",
                Filter = "所有文件|*.*",
                Multiselect = true,
                CheckFileExists = true
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
            }
            catch (Exception ex)
            {
                StatusText = "移除失败：" + ex.Message;
            }
        }
    }
}
