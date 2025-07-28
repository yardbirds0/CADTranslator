using CADTranslator.Models.API;
using CADTranslator.Services.Settings;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CADTranslator.ViewModels
    {
    public class BalanceHistoryViewModel : INotifyPropertyChanged
        {
        #region --- 字段与事件 ---

        private readonly ObservableCollection<BalanceRecord> _masterRecordList;
        private readonly AppSettings _settings;
        public event Action<List<BalanceRecord>> DeleteRequested;

        private ApiServiceType _selectedApiService;

        #endregion

        #region --- 属性 ---

        public DataTable HistoryDataTable { get; private set; }
        public RelayCommand DeleteCommand { get; }

        // 【新增】用于绑定ComboBox的数据源
        public List<ApiServiceType> AvailableApiServices { get; private set; }

        // 【新增】用于绑定ComboBox的选中项
        public ApiServiceType SelectedApiService
            {
            get => _selectedApiService;
            set
                {
                if (SetField(ref _selectedApiService, value))
                    {
                    // 当选项变化时，立刻重建表格
                    BuildDataTable();
                    }
                }
            }

        #endregion

        #region --- 构造函数 ---

        public BalanceHistoryViewModel(ObservableCollection<BalanceRecord> historyRecords, AppSettings settings, ApiServiceType defaultService)
            {
            _masterRecordList = historyRecords ?? throw new ArgumentNullException(nameof(historyRecords));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            DeleteCommand = new RelayCommand(ExecuteDelete, CanExecuteDelete);

            // 【新增】从所有历史记录中，提炼出有哪些API服务，作为ComboBox的选项
            AvailableApiServices = _masterRecordList
                .Select(r => r.ServiceType)
                .Distinct()
                .OrderBy(s => s.ToString())
                .ToList();

            // 【新增】设置智能默认选项
            _selectedApiService = defaultService;

            // 初始时，根据默认选项构建表格
            BuildDataTable();

            _masterRecordList.CollectionChanged += (s, e) => BuildDataTable();
            }

        #endregion

        #region --- 命令实现 (不变) ---

        private bool CanExecuteDelete(object parameter)
            {
            var selectedItems = parameter as IList<object>;
            return selectedItems != null && selectedItems.Count > 0;
            }

        private void ExecuteDelete(object parameter)
            {
            var selectedItems = parameter as IList<object>;
            if (!CanExecuteDelete(selectedItems)) return;

            var recordsToDelete = new List<BalanceRecord>();
            foreach (DataRowView rowView in selectedItems.Cast<DataRowView>())
                {
                if (DateTime.TryParse(rowView["查询时间"].ToString(), out var timestamp))
                    {
                    var record = _masterRecordList.FirstOrDefault(r => r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff") == timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    if (record != null)
                        {
                        recordsToDelete.Add(record);
                        }
                    }
                }

            if (recordsToDelete.Any())
                {
                DeleteRequested?.Invoke(recordsToDelete);
                }
            }

        #endregion

        #region --- 私有方法 (BuildDataTable 已升级) ---

        private void BuildDataTable()
            {
            var newTable = new DataTable();

            // 【核心修改】步骤 1: 只筛选出当前选中API的记录
            var filteredRecords = _masterRecordList
                .Where(r => r.ServiceType == SelectedApiService)
                .ToList();

            if (!filteredRecords.Any())
                {
                HistoryDataTable = newTable;
                OnPropertyChanged(nameof(HistoryDataTable));
                return;
                }

            // 【核心修改】步骤 2: 只根据“筛选后”的记录来动态创建列
            newTable.Columns.Add("查询时间", typeof(string));

            var allKeysInFilteredHistory = filteredRecords
                .SelectMany(r => r.Data?.Select(kvp => kvp.Key) ?? Enumerable.Empty<string>())
                .Distinct()
                .ToList();

            foreach (var key in allKeysInFilteredHistory)
                {
                var mappingRule = _settings.FriendlyNameMappings.Values.FirstOrDefault(r => r.Aliases.Contains(key));
                string columnName = mappingRule?.DefaultFriendlyName ?? key;
                if (!newTable.Columns.Contains(columnName))
                    {
                    newTable.Columns.Add(columnName, typeof(string));
                    }
                }

            // 【核心修改】步骤 3: 只填充“筛选后”的记录到表格中
            foreach (var record in filteredRecords.OrderByDescending(r => r.Timestamp))
                {
                var row = newTable.NewRow();
                row["查询时间"] = record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");

                if (record.Data != null)
                    {
                    foreach (var kvp in record.Data)
                        {
                        var rule = _settings.FriendlyNameMappings.Values.FirstOrDefault(r => r.Aliases.Contains(kvp.Key));
                        string colName = rule?.DefaultFriendlyName ?? kvp.Key;
                        if (newTable.Columns.Contains(colName))
                            {
                            row[colName] = kvp.Value;
                            }
                        }
                    }
                newTable.Rows.Add(row);
                }

            HistoryDataTable = newTable;
            OnPropertyChanged(nameof(HistoryDataTable));
            }

        #endregion

        #region --- INotifyPropertyChanged 实现 (不变) ---
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