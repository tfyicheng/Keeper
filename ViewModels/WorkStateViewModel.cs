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
using Keeper.Infrastructure;
using Keeper.Models;
using Keeper.Services;
using Microsoft.Win32;

namespace Keeper.ViewModels
{
    /// <summary>
    /// 工作状态：保存 / 恢复当前打开的程序。
    /// </summary>
    public class WorkStateViewModel : ObservableObject
    {
        private readonly SnapshotService _snapshotService = new SnapshotService();
        private readonly SnapshotRepository _repository = new SnapshotRepository();
        private readonly WhitelistRepository _whitelistRepository = new WhitelistRepository();

        private SnapshotInfo _selectedSnapshot;
        private string _newSnapshotName = string.Empty;
        private string _statusText = "还没有保存过快照。";
        private bool _isBusy;

        public WorkStateViewModel()
        {
            Snapshots = new ObservableCollection<SnapshotInfo>();
            SelectedItems = new ObservableCollection<SnapshotItem>();

            SaveCommand = new RelayCommand(async () => await SaveAsync(), () => !IsBusy);
            RestoreCommand = new RelayCommand(async () => await RestoreAsync(SelectedSnapshot), () => !IsBusy && SelectedSnapshot != null);
            DeleteCommand = new RelayCommand(async () => await DeleteAsync(), () => !IsBusy && SelectedSnapshot != null);
            ClearSnapshotsCommand = new RelayCommand(async () => await ClearSnapshotsAsync(), () => !IsBusy && Snapshots.Count > 0);
            RefreshCommand = new RelayCommand(LoadSnapshots, () => !IsBusy);
            AddFilesCommand = new RelayCommand(async () => await PickFilesAsync(), () => !IsBusy && SelectedSnapshot != null);
            RemoveItemCommand = new RelayCommand(async item => await RemoveItemAsync(item as SnapshotItem));
            OpenFolderCommand = new RelayCommand(parameter => OpenFolder(parameter as SnapshotItem));
            AddToWhitelistCommand = new RelayCommand(async parameter => await AddToWhitelistAsync(parameter as SnapshotItem));

            LoadSnapshots();
        }

        public ObservableCollection<SnapshotInfo> Snapshots { get; }

        public ObservableCollection<SnapshotItem> SelectedItems { get; }

        public ICommand SaveCommand { get; }

        public ICommand RestoreCommand { get; }

        public ICommand DeleteCommand { get; }

        /// <summary>
        /// 清空全部历史快照。
        /// </summary>
        public ICommand ClearSnapshotsCommand { get; }

        public ICommand RefreshCommand { get; }

        public ICommand AddFilesCommand { get; }

        public ICommand RemoveItemCommand { get; }

        public ICommand OpenFolderCommand { get; }

        public ICommand AddToWhitelistCommand { get; }

        /// <summary>
        /// 保存新快照时使用的名称，留空则自动命名。
        /// </summary>
        public string NewSnapshotName
        {
            get { return _newSnapshotName; }
            set { SetProperty(ref _newSnapshotName, value); }
        }

        /// <summary>
        /// 当前快照名称，失去焦点时持久化重命名结果。
        /// </summary>
        public string SnapshotName
        {
            get { return _selectedSnapshot?.Name ?? string.Empty; }
            set
            {
                var snapshot = _selectedSnapshot;
                if (snapshot == null)
                {
                    return;
                }

                var name = (value ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(name) || string.Equals(name, snapshot.Name, StringComparison.Ordinal))
                {
                    // 名称不能为空，界面恢复为原值
                    OnPropertyChanged();
                    return;
                }

                try
                {
                    _repository.UpdateName(snapshot.Id, name);
                    snapshot.Name = name;
                    StatusText = "已重命名为「" + name + "」。";
                }
                catch (Exception ex)
                {
                    StatusText = "重命名失败：" + ex.Message;
                    OnPropertyChanged();
                }
            }
        }

        public SnapshotInfo SelectedSnapshot
        {
            get { return _selectedSnapshot; }
            set
            {
                if (SetProperty(ref _selectedSnapshot, value))
                {
                    LoadSelectedItems();
                    OnPropertyChanged(nameof(SnapshotName));
                    CommandManager.InvalidateRequerySuggested();
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
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string RunningHint
        {
            get { return "保存后，重启电脑再打开 Keeper，即可一键恢复这些程序。"; }
        }

        /// <summary>
        /// 启动时自动恢复最近一次快照。
        /// </summary>
        public async Task RestoreLatestAsync()
        {
            var latest = Snapshots.FirstOrDefault() ?? _repository.GetLatest();
            if (latest == null)
            {
                return;
            }

            await RestoreAsync(latest);
        }

        /// <summary>
        /// 依次恢复多条快照（启动时按设置中选中的快照执行）。
        /// </summary>
        public async Task RestoreSnapshotsAsync(IEnumerable<SnapshotInfo> snapshots)
        {
            foreach (var snapshot in snapshots ?? Enumerable.Empty<SnapshotInfo>())
            {
                await RestoreAsync(snapshot);
            }
        }

        /// <summary>
        /// 向当前快照追加文件。
        /// </summary>
        public async Task AddFilesAsync(IEnumerable<string> paths)
        {
            var snapshot = SelectedSnapshot;
            if (snapshot == null)
            {
                StatusText = "请先选择一条快照，再追加启动项。";
                return;
            }

            var files = (paths ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList();
            if (files.Count == 0)
            {
                return;
            }

            IsBusy = true;
            try
            {
                var result = await Task.Run(() => _snapshotService.AddFiles(snapshot, files));
                foreach (var item in result.Added)
                {
                    SelectedItems.Add(item);
                }

                snapshot.ItemCount = SelectedItems.Count;

                if (result.Added.Count == 0)
                {
                    StatusText = "没有可添加的文件（可能已在列表中）。";
                }
                else
                {
                    StatusText = "已添加 " + result.Added.Count + " 个启动项"
                        + (result.Skipped > 0 ? "，跳过 " + result.Skipped + " 个。" : "。");
                }
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
        /// 从当前快照移除一个启动项。
        /// </summary>
        public async Task RemoveItemAsync(SnapshotItem item)
        {
            var snapshot = SelectedSnapshot;
            if (snapshot == null || item == null)
            {
                return;
            }

            try
            {
                await Task.Run(() => _snapshotService.RemoveItem(item));
                SelectedItems.Remove(item);
                snapshot.ItemCount = SelectedItems.Count;
                StatusText = "已移除「" + (item.DisplayName ?? item.FileName) + "」。";
            }
            catch (Exception ex)
            {
                StatusText = "移除失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 在资源管理器中定位该启动项。
        /// </summary>
        private void OpenFolder(SnapshotItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ExecutablePath))
            {
                return;
            }

            try
            {
                var arguments = File.Exists(item.ExecutablePath)
                    ? "/select,\"" + item.ExecutablePath + "\""
                    : "\"" + (Path.GetDirectoryName(item.ExecutablePath) ?? item.ExecutablePath) + "\"";

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

        /// <summary>
        /// 把条目加入白名单，并从当前快照中移除。
        /// </summary>
        private async Task AddToWhitelistAsync(SnapshotItem item)
        {
            if (item == null)
            {
                return;
            }

            var name = string.IsNullOrWhiteSpace(item.DisplayName) ? item.FileName : item.DisplayName;
            var confirm = MessageBox.Show(
                "将「" + name + "」加入白名单？" + Environment.NewLine + Environment.NewLine +
                "加入后，保存快照时不再记录该程序，并从当前快照中移除。",
                "添加到白名单",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK)
            {
                return;
            }

            try
            {
                await Task.Run(() => _whitelistRepository.Add(item.ExecutablePath, name));
                await RemoveItemAsync(item);
                StatusText = "已将「" + name + "」加入白名单，并从当前快照移除。";
            }
            catch (Exception ex)
            {
                StatusText = "加入白名单失败：" + ex.Message;
            }
        }

        private async Task PickFilesAsync()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择要加入快照的文件",
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

        private void LoadSnapshots()
        {
            var selectedId = _selectedSnapshot?.Id ?? 0;

            Snapshots.Clear();
            foreach (var snapshot in _repository.GetAll())
            {
                Snapshots.Add(snapshot);
            }

            if (Snapshots.Count == 0)
            {
                SelectedSnapshot = null;
                StatusText = "还没有保存过快照，点击右上角按钮开始记录。";
                return;
            }

            SelectedSnapshot = Snapshots.FirstOrDefault(s => s.Id == selectedId) ?? Snapshots[0];
            StatusText = "共 " + Snapshots.Count + " 条快照。";
        }

        private void LoadSelectedItems()
        {
            SelectedItems.Clear();
            if (_selectedSnapshot == null)
            {
                return;
            }

            foreach (var item in _repository.GetItems(_selectedSnapshot.Id))
            {
                SelectedItems.Add(item);
            }

            if (SelectedItems.Count == 0)
            {
                foreach (var item in _selectedSnapshot.Items)
                {
                    SelectedItems.Add(item);
                }
            }
        }

        /// <summary>
        /// 采集并保存当前工作状态（供界面按钮与托盘菜单共用）。
        /// </summary>
        public async Task SaveAsync()
        {
            IsBusy = true;
            StatusText = "正在采集当前运行的程序…";

            try
            {
                var name = NewSnapshotName;
                var snapshot = await Task.Run(() => _snapshotService.SaveCurrent(name));
                NewSnapshotName = string.Empty;
                LoadSnapshots();

                var created = Snapshots.FirstOrDefault(s => s.Id == snapshot.Id);
                if (created != null)
                {
                    SelectedSnapshot = created;
                }

                StatusText = snapshot.ItemCount == 0
                    ? "没有发现正在运行的程序，快照为空。"
                    : "已保存快照「" + snapshot.Name + "」，共记录 " + snapshot.ItemCount + " 个启动项。";
            }
            catch (Exception ex)
            {
                StatusText = "保存失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task RestoreAsync(SnapshotInfo snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            IsBusy = true;
            StatusText = "正在恢复「" + snapshot.Name + "」…";

            try
            {
                var skipRunning = new SettingsRepository().GetBool(SettingsKeys.RestoreSkipRunning, true);
                var result = await Task.Run(() => _snapshotService.Restore(snapshot, skipRunning));

                var message = "已启动 " + result.Started + " 个程序";
                if (result.Skipped > 0)
                {
                    message += "，跳过 " + result.Skipped + " 个已运行程序";
                }

                if (result.Failures.Count > 0)
                {
                    message += "；" + result.Failures.Count + " 个未能启动：" + string.Join("、", result.Failures.Take(3));
                }

                StatusText = message + "。";
            }
            catch (Exception ex)
            {
                StatusText = "恢复失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task DeleteAsync()
        {
            var snapshot = SelectedSnapshot;
            if (snapshot == null)
            {
                return;
            }

            var confirm = MessageBox.Show(
                "确定删除快照「" + snapshot.Name + "」吗？",
                "删除快照",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.OK)
            {
                return;
            }

            IsBusy = true;
            try
            {
                await Task.Run(() => _repository.Delete(snapshot.Id));
                LoadSnapshots();
                StatusText = "已删除快照。";
            }
            catch (Exception ex)
            {
                StatusText = "删除失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 清空全部历史快照（含启动项）。
        /// </summary>
        private async Task ClearSnapshotsAsync()
        {
            if (Snapshots.Count == 0)
            {
                StatusText = "还没有保存过快照。";
                return;
            }

            var confirm = MessageBox.Show(
                "确定清空全部 " + Snapshots.Count + " 条历史快照吗？" + Environment.NewLine + Environment.NewLine +
                "清空后无法恢复，已选中的启动恢复快照也会失效。",
                "清空历史快照",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.OK)
            {
                return;
            }

            IsBusy = true;
            try
            {
                var count = Snapshots.Count;
                await Task.Run(() => _repository.DeleteAll());
                LoadSnapshots();
                StatusText = "已清空全部 " + count + " 条历史快照。";
            }
            catch (Exception ex)
            {
                StatusText = "清空失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
