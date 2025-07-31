
using CADTranslator.Models.API;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace CADTranslator.ViewModels
    {
    public class UsageHistoryViewModel : INotifyPropertyChanged
        {
        #region --- 字段与属性 ---

        private readonly ReadOnlyCollection<TranslationRecord> _masterRecordList;
        private ApiServiceType? _selectedApiService; // 使用可空类型以支持“全部”选项

        public DataTable HistoryDataTable { get; private set; }
        public ICommand CloseCommand { get; }

        // 用于绑定ComboBox的数据源
        public List<object> ApiFilterOptions { get; private set; }

        // 用于绑定ComboBox的选中项
        public object SelectedApiFilter
            {
            get => _selectedApiService ?? (object)"全部";
            set
                {
                if (value is ApiServiceType serviceType)
                    {
                    _selectedApiService = serviceType;
                    }
                else
                    {
                    _selectedApiService = null; // "全部" 选项
                    }
                OnPropertyChanged();
                BuildDataTableAndSummary();
                }
            }

        // --- 底部汇总数据 ---
        private long _totalParagraphs;
        public long TotalParagraphs { get => _totalParagraphs; set => SetField(ref _totalParagraphs, value); }

        private double _totalDuration;
        public double TotalDuration { get => _totalDuration; set => SetField(ref _totalDuration, value); }

        private long _totalCharacters;
        public long TotalCharacters { get => _totalCharacters; set => SetField(ref _totalCharacters, value); }

        #endregion

        #region --- 构造函数 ---

        public UsageHistoryViewModel(ObservableCollection<TranslationRecord> historyRecords)
            {
            _masterRecordList = new ReadOnlyCollection<TranslationRecord>(historyRecords);
            CloseCommand = new RelayCommand(ExecuteClose);

            // 从历史记录中提炼出API服务选项，并添加一个“全部”选项
            ApiFilterOptions = new List<object> { "全部" };
            var availableServices = _masterRecordList
                .Select(r => r.ServiceType)
                .Distinct()
                .OrderBy(s => s.ToString())
                .Cast<object>();
            ApiFilterOptions.AddRange(availableServices);

            _selectedApiService = null; // 默认显示全部

            BuildDataTableAndSummary();
            }

        #endregion

        #region --- 命令实现 ---

        private void ExecuteClose(object parameter)
            {
            if (parameter is Window window)
                {
                window.Close();
                }
            }

        #endregion

        #region --- 核心逻辑 ---

        private void BuildDataTableAndSummary()
            {
            var newTable = new DataTable();

            // 1. 根据选择筛选记录
            var filteredRecords = _selectedApiService.HasValue
                ? _masterRecordList.Where(r => r.ServiceType == _selectedApiService.Value).ToList()
                : _masterRecordList.ToList();

            // 2. 更新底部的汇总数据
            if (filteredRecords.Any())
                {
                TotalParagraphs = filteredRecords.Sum(r => (long)r.ParagraphCount);
                TotalDuration = filteredRecords.Sum(r => r.DurationInSeconds);
                TotalCharacters = filteredRecords.Sum(r => r.TranslatedCharacterCount);
                }
            else
                {
                TotalParagraphs = 0;
                TotalDuration = 0;
                TotalCharacters = 0;
                }

            // 3. 构建DataTable的列 (已更新)
            newTable.Columns.Add("时间", typeof(string));
            newTable.Columns.Add("接口", typeof(string));
            newTable.Columns.Add("模型", typeof(string));
            newTable.Columns.Add("耗时(秒)", typeof(string));
            newTable.Columns.Add("成功", typeof(int));
            newTable.Columns.Add("失败", typeof(int));
            newTable.Columns.Add("取消", typeof(string));
            newTable.Columns.Add("吞吐率(字/秒)", typeof(string)); // 【新增】
            newTable.Columns.Add("源语言", typeof(string));     // 【新增】
            newTable.Columns.Add("目标语言", typeof(string));   // 【新增】
            newTable.Columns.Add("原文(字)", typeof(long));
            newTable.Columns.Add("译文(字)", typeof(long));
            newTable.Columns.Add("总Token", typeof(long));
            newTable.Columns.Add("输入Token", typeof(long));
            newTable.Columns.Add("输出Token", typeof(long));
            newTable.Columns.Add("排版", typeof(string));
            newTable.Columns.Add("并发", typeof(string));
            newTable.Columns.Add("发送模式", typeof(string));
            newTable.Columns.Add("提示词", typeof(string));
            newTable.Columns.Add("失败原因", typeof(string));

            // 4. 填充数据行 (已更新)
            foreach (var record in filteredRecords.OrderByDescending(r => r.Timestamp))
                {
                var row = newTable.NewRow();
                row["时间"] = record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                row["接口"] = record.ServiceType.ToString();
                row["模型"] = record.ModelName ?? "N/A";
                row["耗时(秒)"] = record.DurationInSeconds.ToString("F1");
                row["成功"] = record.ParagraphCount;
                row["失败"] = record.FailureCount;
                row["取消"] = record.WasCancelled ? "是" : "否";

                // 【新增】计算并填充吞吐率
                if (record.DurationInSeconds > 0.1)
                    {
                    row["吞吐率(字/秒)"] = (record.TranslatedCharacterCount / record.DurationInSeconds).ToString("F0");
                    }
                else
                    {
                    row["吞吐率(字/秒)"] = "N/A";
                    }

                row["源语言"] = record.SourceLanguage;   // 【新增】
                row["目标语言"] = record.TargetLanguage; // 【新增】

                row["原文(字)"] = record.SourceCharacterCount;
                row["译文(字)"] = record.TranslatedCharacterCount;
                row["总Token"] = record.Usage?.TotalTokens ?? 0;
                row["输入Token"] = record.Usage?.PromptTokens ?? 0;
                row["输出Token"] = record.Usage?.CompletionTokens ?? 0;
                row["排版"] = record.IsLiveLayoutEnabled ? "开启" : "关闭";
                row["并发"] = record.ConcurrencyLevel;
                row["发送模式"] = record.SendingMode == PromptSendingMode.Once ? "合并" : "逐句";
                row["提示词"] = record.PromptTemplateUsed.ToString();

                if (record.FailureCount > 0 && record.FailureMessages.Any())
                    {
                    row["失败原因"] = string.Join("; ", record.FailureMessages);
                    }
                else
                    {
                    row["失败原因"] = "—";
                    }

                newTable.Rows.Add(row);
                }

            HistoryDataTable = newTable;
            OnPropertyChanged(nameof(HistoryDataTable));
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