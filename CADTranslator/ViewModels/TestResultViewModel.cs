// 文件路径: CADTranslator/ViewModels/TestResultViewModel.cs

using Autodesk.AutoCAD.DatabaseServices;
using CADTranslator.Models.CAD;
using CADTranslator.Services.CAD;
using CADTranslator.Services.Settings;
using CADTranslator.Views;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
// 【新增】引入 StringBuilder
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace CADTranslator.ViewModels
    {
    public class TestResultViewModel : INotifyPropertyChanged
        {
        #region --- 字段与服务 ---

        private readonly Window _view;
        private readonly List<LayoutTask> _originalTargets;
        // 【新增】持有原始障碍物列表
        private readonly List<Entity> _rawObstacles;
        private readonly SettingsService _settingsService = new SettingsService();
        private AppSettings _settings;

        #endregion

        #region --- 绑定属性 ---

        public ObservableCollection<LayoutTask> ReportItems { get; set; }

        public ObservableCollection<int> RoundOptions { get; set; }
        public ObservableCollection<double> SearchRangeOptions { get; set; }

        private string _summaryText;
        public string SummaryText
            {
            get => _summaryText;
            set => SetField(ref _summaryText, value);
            }

        public int NumberOfRounds
            {
            get => _settings.TestNumberOfRounds;
            set
                {
                if (_settings.TestNumberOfRounds != value)
                    {
                    _settings.TestNumberOfRounds = value;
                    OnPropertyChanged();
                    SaveSettings();
                    }
                }
            }

        public double CurrentSearchRangeFactor
            {
            get => _settings.TestSearchRangeFactor;
            set
                {
                if (_settings.TestSearchRangeFactor != value)
                    {
                    _settings.TestSearchRangeFactor = value;
                    OnPropertyChanged();
                    SaveSettings();
                    foreach (var task in _originalTargets)
                        {
                        task.SearchRangeFactor = _settings.TestSearchRangeFactor;
                        }
                    }
                }
            }

        #endregion

        #region --- 命令 ---

        public ICommand RecalculateLayoutCommand { get; }
        public ICommand ApplyLayoutCommand { get; }

        #endregion

        #region --- 构造函数 ---

        // 【修改】构造函数，接收所有必要的数据
        public TestResultViewModel(Window view, List<LayoutTask> targets, List<Entity> rawObstacles, (int rounds, double bestScore, double worstScore) summary)
            {
            _view = view;
            _originalTargets = targets;
            _rawObstacles = rawObstacles; // 保存障碍物

            _settings = _settingsService.LoadSettings();

            // ... (RoundOptions 和 SearchRangeOptions 的初始化逻辑不变) ...
            SearchRangeOptions = new ObservableCollection<double> { 5.0, 8.0, 10.0, 15.0, 20.0 };
            if (!SearchRangeOptions.Contains(CurrentSearchRangeFactor))
                {
                SearchRangeOptions.Add(CurrentSearchRangeFactor);
                }
            foreach (var task in _originalTargets)
                {
                task.SearchRangeFactor = CurrentSearchRangeFactor;
                }
            RoundOptions = new ObservableCollection<int> { 10, 50, 100, 200, 500, 1000 };
            if (NumberOfRounds < 10)
                {
                NumberOfRounds = 10;
                }

            ReportItems = new ObservableCollection<LayoutTask>(targets);
            UpdateSummary(summary); // 初始化摘要文本

            RecalculateLayoutCommand = new RelayCommand(ExecuteRecalculateLayout);
            ApplyLayoutCommand = new RelayCommand(ExecuteApplyLayout);
            }

        #endregion

        #region --- 命令实现 ---

        private async void ExecuteRecalculateLayout(object parameter)
            {
            SummaryText = $"正在使用 {NumberOfRounds} 轮次进行新一轮推演，请稍候...";

            var newSummary = await Task.Run(() =>
            {
                foreach (var task in _originalTargets)
                    {
                    task.BestPosition = null;
                    task.AlgorithmPosition = null;
                    task.CurrentUserPosition = null;
                    task.IsManuallyMoved = false;
                    task.FailureReason = null;
                    task.CollisionDetails.Clear();
                    }

                var calculator = new LayoutCalculator();
                return calculator.CalculateLayouts(_originalTargets, _rawObstacles, NumberOfRounds);
            });

            UpdateSummary(newSummary);
            var refreshedItems = new ObservableCollection<LayoutTask>(_originalTargets);
            ReportItems = refreshedItems;
            OnPropertyChanged(nameof(ReportItems)); // 明确通知UI整个集合都变了

            (_view as TestResultWindow)?.ForceRedraw();
            }

        private void ExecuteApplyLayout(object parameter)
            {
            var tasksToApply = _originalTargets;
            CadBridgeService.LayoutTasksToApply = tasksToApply;
            CadBridgeService.SendCommandToAutoCAD("TEST_APPLY\n");
            _view.Close();
            }

        #endregion

        #region --- 辅助方法 ---

        private void SaveSettings()
            {
            _settingsService.SaveSettings(_settings);
            }

        // 【新增】从View迁移过来的 UpdateSummary 方法
        private void UpdateSummary((int rounds, double bestScore, double worstScore) summary)
            {
            var summaryTextBuilder = new StringBuilder();
            summaryTextBuilder.AppendLine($"总推演轮次: {summary.rounds} 轮");
            summaryTextBuilder.AppendLine($"最佳布局评分: {summary.bestScore:F2}");
            summaryTextBuilder.AppendLine($"最差布局评分: {summary.worstScore:F2}");
            SummaryText = summaryTextBuilder.ToString();
            }

        #endregion

        #region --- INotifyPropertyChanged 实现 ---

        public event PropertyChangedEventHandler PropertyChanged;
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
            {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
            }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

        #endregion
        }
    }