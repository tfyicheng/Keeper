using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Keeper.Helpers;
using Keeper.Infrastructure;
using Keeper.Models;
using Keeper.Services;

namespace Keeper.ViewModels
{
    /// <summary>
    /// 启动项管理。
    /// </summary>
    public class AutostartViewModel : ObservableObject
    {
        private readonly AutostartService _service = new AutostartService();
        private readonly ICollectionView _view;

        private string _searchText = string.Empty;
        private string _statusText = "正在扫描启动项…";
        private bool _isBusy;

        public AutostartViewModel()
        {
            Entries = new ObservableCollection<AutostartEntry>();
            _view = CollectionViewSource.GetDefaultView(Entries);
            _view.Filter = FilterEntry;

            RefreshCommand = new RelayCommand(async () => await LoadAsync(), () => !IsBusy);
            ToggleCommand = new RelayCommand(async parameter => await ToggleAsync(parameter as AutostartEntry));
            DeleteCommand = new RelayCommand(async parameter => await DeleteAsync(parameter as AutostartEntry));
            AddFilesCommand = new RelayCommand(async () => await PickFilesAsync(), () => !IsBusy);
            OpenFolderCommand = new RelayCommand(parameter => OpenFolder(parameter as AutostartEntry));

            _ = LoadAsync();
        }

        public ObservableCollection<AutostartEntry> Entries { get; }

        public ICollectionView EntriesView
        {
            get { return _view; }
        }

        public ICommand RefreshCommand { get; }

        public ICommand ToggleCommand { get; }

        public ICommand DeleteCommand { get; }

        public ICommand AddFilesCommand { get; }

        public ICommand OpenFolderCommand { get; }

        public string SearchText
        {
            get { return _searchText; }
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    _view.Refresh();
                }
            }
        }

        public string StatusText
        {
            get { return _statusText; }
            private set { SetProperty(ref _statusText, value); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set { SetProperty(ref _isBusy, value); }
        }

        public int TotalCount
        {
            get { return Entries.Count; }
        }

        public int EnabledCount
        {
            get { return Entries.Count(entry => entry.IsEnabled); }
        }

        public string SummaryText
        {
            get
            {
                if (Entries.Count == 0)
                {
                    return "未扫描到启动项";
                }

                return "共 " + TotalCount + " 项 · 已启用 " + EnabledCount + " 项";
            }
        }

        private bool FilterEntry(object item)
        {
            var entry = item as AutostartEntry;
            if (entry == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_searchText))
            {
                return true;
            }

            var keyword = _searchText.Trim();
            return Contains(entry.Name, keyword)
                || Contains(entry.Command, keyword)
                || Contains(entry.TargetPath, keyword)
                || Contains(entry.SourceText, keyword);
        }

        private static bool Contains(string source, string keyword)
        {
            return !string.IsNullOrEmpty(source) && source.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private async Task LoadAsync()
        {
            IsBusy = true;
            StatusText = "正在扫描启动项…";

            try
            {
                var items = await Task.Run(() => _service.Scan());

                Entries.Clear();
                foreach (var entry in items)
                {
                    Entries.Add(entry);
                }

                _view.Refresh();
                RaiseSummaryChanged();

                StatusText = Entries.Count == 0
                    ? "没有找到开机启动项。"
                    : "扫描完成，共 " + Entries.Count + " 项。";
            }
            catch (Exception ex)
            {
                StatusText = "扫描失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task PickFilesAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择要开机启动的文件",
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
        /// 把任意格式的文件追加为开机启动项（在当前用户启动文件夹创建快捷方式）。
        /// </summary>
        public async Task AddFilesAsync(IEnumerable<string> paths)
        {
            var files = (paths ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList();
            if (files.Count == 0)
            {
                return;
            }

            IsBusy = true;
            StatusText = "正在添加启动项…";

            try
            {
                var created = await Task.Run(() => _service.CreateStartupShortcuts(files));
                await LoadAsync();

                StatusText = created.Count == 0
                    ? "没有新增启动项（已存在或不是文件）。"
                    : "已添加 " + created.Count + " 个启动项。";
            }
            catch (AutostartOperationException ex)
            {
                StatusText = ex.Message;
                MessageBox.Show(ex.Message, "添加启动项", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                StatusText = "添加失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 在资源管理器中定位该启动项对应的文件。
        /// </summary>
        private void OpenFolder(AutostartEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            var path = entry.FilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = entry.TargetPath;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                path = CommandLineHelper.GetExecutablePath(entry.Command);
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                StatusText = "找不到「" + entry.Name + "」对应的文件路径。";
                return;
            }

            try
            {
                var arguments = File.Exists(path)
                    ? "/select,\"" + path + "\""
                    : "\"" + (Path.GetDirectoryName(path) ?? path) + "\"";

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

        private async Task ToggleAsync(AutostartEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            var target = !entry.IsEnabled;
            try
            {
                await Task.Run(() => _service.SetEnabled(entry, target));
                entry.IsEnabled = target;
                RaiseSummaryChanged();
                StatusText = (target ? "已启用「" : "已禁用「") + entry.Name + "」。";
            }
            catch (AutostartOperationException ex)
            {
                StatusText = ex.Message;
                MessageBox.Show(ex.Message, "启动项", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                StatusText = "操作失败：" + ex.Message;
            }
        }

        private async Task DeleteAsync(AutostartEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            var confirm = MessageBox.Show(
                "确定彻底删除启动项「" + entry.Name + "」吗？删除后无法在本工具中恢复。",
                "删除启动项",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK)
            {
                return;
            }

            try
            {
                await Task.Run(() => _service.Delete(entry));
                Entries.Remove(entry);
                RaiseSummaryChanged();
                StatusText = "已删除「" + entry.Name + "」。";
            }
            catch (AutostartOperationException ex)
            {
                StatusText = ex.Message;
                MessageBox.Show(ex.Message, "启动项", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                StatusText = "删除失败：" + ex.Message;
            }
        }

        private void RaiseSummaryChanged()
        {
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(EnabledCount));
            OnPropertyChanged(nameof(SummaryText));
        }
    }
}
