using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Keeper.Data;
using Keeper.Helpers;
using Keeper.Infrastructure;
using Keeper.Models;
using Keeper.Services;

namespace Keeper.ViewModels
{
    /// <summary>
    /// 关闭主窗口时的行为选项。
    /// </summary>
    public class CloseActionOption
    {
        public const string Ask = "Ask";

        public string Value { get; set; }

        public string Text { get; set; }
    }

    /// <summary>
    /// 设置。
    /// </summary>
    public class SettingsViewModel : ObservableObject
    {
        private readonly SettingsRepository _repository = new SettingsRepository();
        private readonly SelfStartupService _selfStartupService = new SelfStartupService();
        private readonly SnapshotRepository _snapshotRepository = new SnapshotRepository();

        private bool _autoRestoreOnLaunch;
        private bool _launchWithWindows;
        private bool _restoreSkipRunning;
        private string _statusText = string.Empty;
        private SnapshotInfo _snapshotToAdd;

        public SettingsViewModel()
        {
            _autoRestoreOnLaunch = _repository.GetBool(SettingsKeys.AutoRestoreOnLaunch, true);
            _restoreSkipRunning = _repository.GetBool(SettingsKeys.RestoreSkipRunning, true);
            _launchWithWindows = _selfStartupService.IsEnabled();

            SelectedSnapshots = new ObservableCollection<SnapshotInfo>();
            AvailableSnapshots = new ObservableCollection<SnapshotInfo>();

            CloseActionOptions = new List<CloseActionOption>
            {
                new CloseActionOption { Value = CloseActionOption.Ask, Text = "每次询问" },
                new CloseActionOption { Value = "Tray", Text = "最小化到托盘" },
                new CloseActionOption { Value = "Exit", Text = "直接退出程序" }
            };

            OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
            RestartAsAdminCommand = new RelayCommand(RestartAsAdmin, () => !IsAdministrator);
            AddSnapshotCommand = new RelayCommand(AddSnapshot, () => SnapshotToAdd != null);
            RemoveSnapshotCommand = new RelayCommand(parameter => RemoveSnapshot(parameter as SnapshotInfo));

            ReloadSnapshots();
        }

        public IList<CloseActionOption> CloseActionOptions { get; }

        /// <summary>
        /// 启动时需要恢复的快照（可多选，按添加顺序恢复）。
        /// </summary>
        public ObservableCollection<SnapshotInfo> SelectedSnapshots { get; }

        /// <summary>
        /// 尚未被选中的快照，用于下拉添加。
        /// </summary>
        public ObservableCollection<SnapshotInfo> AvailableSnapshots { get; }

        public ICommand AddSnapshotCommand { get; }

        public ICommand RemoveSnapshotCommand { get; }

        /// <summary>
        /// 关闭主窗口时的行为，写入设置后立即生效，无需重启。
        /// </summary>
        public CloseActionOption SelectedCloseAction
        {
            get
            {
                var value = _repository.Get(SettingsKeys.CloseAction, CloseActionOption.Ask);
                return CloseActionOptions.FirstOrDefault(option =>
                           string.Equals(option.Value, value, StringComparison.OrdinalIgnoreCase))
                       ?? CloseActionOptions[0];
            }
            set
            {
                if (value == null)
                {
                    return;
                }

                _repository.Set(SettingsKeys.CloseAction, value.Value);
                OnPropertyChanged();
                StatusText = "关闭主窗口时：" + value.Text + "。";
            }
        }

        public ICommand OpenDataFolderCommand { get; }

        public ICommand RestartAsAdminCommand { get; }

        public bool AutoRestoreOnLaunch
        {
            get { return _autoRestoreOnLaunch; }
            set
            {
                if (!SetProperty(ref _autoRestoreOnLaunch, value))
                {
                    return;
                }

                _repository.SetBool(SettingsKeys.AutoRestoreOnLaunch, value);
                StatusText = value
                    ? "启动 Keeper 时将恢复下方所选的快照。"
                    : "已关闭启动时自动恢复。";
            }
        }

        /// <summary>
        /// 下拉框中当前选中的待添加快照。
        /// </summary>
        public SnapshotInfo SnapshotToAdd
        {
            get { return _snapshotToAdd; }
            set
            {
                if (SetProperty(ref _snapshotToAdd, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        /// <summary>
        /// 重新加载快照选择（进入设置页时调用）。
        /// </summary>
        public void ReloadSnapshots()
        {
            var ids = GetSelectedSnapshotIds();
            var all = _snapshotRepository.GetAll();

            SelectedSnapshots.Clear();
            foreach (var id in ids)
            {
                var match = all.FirstOrDefault(snapshot => snapshot.Id == id);
                if (match != null)
                {
                    SelectedSnapshots.Add(match);
                }
            }

            RebuildAvailable(all);
            PersistSelectedSnapshots();
            SnapshotToAdd = null;
        }

        /// <summary>
        /// 读取启动时要恢复的快照列表；为空时由调用方回退到最近一次快照。
        /// </summary>
        public IList<SnapshotInfo> GetStartupSnapshots()
        {
            var ids = GetSelectedSnapshotIds();
            if (ids.Count == 0)
            {
                return new List<SnapshotInfo>();
            }

            var all = _snapshotRepository.GetAll();
            return ids
                .Select(id => all.FirstOrDefault(snapshot => snapshot.Id == id))
                .Where(snapshot => snapshot != null)
                .ToList();
        }

        private void AddSnapshot()
        {
            var snapshot = SnapshotToAdd;
            if (snapshot == null)
            {
                return;
            }

            if (SelectedSnapshots.All(item => item.Id != snapshot.Id))
            {
                SelectedSnapshots.Add(snapshot);
                PersistSelectedSnapshots();
            }

            SnapshotToAdd = null;
            RebuildAvailable(_snapshotRepository.GetAll());
            StatusText = "启动时将按顺序恢复 " + SelectedSnapshots.Count + " 条快照。";
        }

        private void RemoveSnapshot(SnapshotInfo snapshot)
        {
            if (snapshot == null || !SelectedSnapshots.Remove(snapshot))
            {
                return;
            }

            PersistSelectedSnapshots();
            RebuildAvailable(_snapshotRepository.GetAll());
            StatusText = SelectedSnapshots.Count == 0
                ? "未选择快照，启动时将恢复最近一次快照。"
                : "启动时将按顺序恢复 " + SelectedSnapshots.Count + " 条快照。";
        }

        private void RebuildAvailable(IEnumerable<SnapshotInfo> all)
        {
            var selectedIds = new HashSet<long>(SelectedSnapshots.Select(item => item.Id));

            AvailableSnapshots.Clear();
            foreach (var snapshot in all)
            {
                if (!selectedIds.Contains(snapshot.Id))
                {
                    AvailableSnapshots.Add(snapshot);
                }
            }
        }

        private List<long> GetSelectedSnapshotIds()
        {
            var result = new List<long>();
            var raw = _repository.Get(SettingsKeys.AutoRestoreSnapshotIds, string.Empty);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return result;
            }

            foreach (var part in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                long id;
                if (long.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
                {
                    result.Add(id);
                }
            }

            return result;
        }

        private void PersistSelectedSnapshots()
        {
            var ids = SelectedSnapshots.Select(item => item.Id.ToString(CultureInfo.InvariantCulture));
            _repository.Set(SettingsKeys.AutoRestoreSnapshotIds, string.Join(",", ids));
        }

        public bool RestoreSkipRunning
        {
            get { return _restoreSkipRunning; }
            set
            {
                if (!SetProperty(ref _restoreSkipRunning, value))
                {
                    return;
                }

                _repository.SetBool(SettingsKeys.RestoreSkipRunning, value);
                StatusText = value ? "恢复时会跳过已在运行的程序。" : "恢复时会尝试再次启动所有程序。";
            }
        }

        public bool LaunchWithWindows
        {
            get { return _launchWithWindows; }
            set
            {
                if (_launchWithWindows == value)
                {
                    return;
                }

                try
                {
                    _selfStartupService.SetEnabled(value);
                    _launchWithWindows = value;
                    OnPropertyChanged(nameof(LaunchWithWindows));
                    StatusText = value ? "已设置为开机自动启动。" : "已取消开机自动启动。";
                }
                catch (Exception ex)
                {
                    StatusText = ex.Message;
                    OnPropertyChanged(nameof(LaunchWithWindows));
                }
            }
        }

        public string DatabasePath
        {
            get { return AppPaths.DatabasePath; }
        }

        public string DataDirectory
        {
            get { return AppPaths.BaseDirectory; }
        }

        public bool IsAdministrator
        {
            get { return PrivilegeHelper.IsAdministrator(); }
        }

        public string PrivilegeText
        {
            get { return IsAdministrator ? "管理员" : "普通用户"; }
        }

        public string VersionText
        {
            get
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return version == null ? "1.0.0" : version.ToString(3);
            }
        }

        public string StatusText
        {
            get { return _statusText; }
            private set { SetProperty(ref _statusText, value); }
        }

        private void OpenDataFolder()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "\"" + AppPaths.BaseDirectory + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusText = "打开目录失败：" + ex.Message;
            }
        }

        private void RestartAsAdmin()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = AppPaths.ExecutablePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process.Start(startInfo);
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                StatusText = "以管理员身份重启失败：" + ex.Message;
            }
        }
    }
}
