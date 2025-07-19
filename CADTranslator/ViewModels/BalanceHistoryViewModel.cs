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
        private readonly AppSettings _settings; // ◄◄◄ 【新增】用于存储设置
        public event Action<List<BalanceRecord>> DeleteRequested;

        #endregion

        #region --- 属性 ---

        public DataTable HistoryDataTable { get; private set; }
        public RelayCommand DeleteCommand { get; }

        #endregion

        #region --- 构造函数 ---

        // ▼▼▼ 【核心修改】构造函数现在接收 AppSettings ▼▼▼
        public BalanceHistoryViewModel(ObservableCollection<BalanceRecord> historyRecords, AppSettings settings)
            {
            _masterRecordList = historyRecords ?? throw new ArgumentNullException(nameof(historyRecords));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            DeleteCommand = new RelayCommand(ExecuteDelete, CanExecuteDelete);
            BuildDataTable();

            _masterRecordList.CollectionChanged += (s, e) => BuildDataTable();
            }

        #endregion

        #region --- 命令实现 ---

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

        #region --- 私有方法 ---

        // ▼▼▼ 【核心修改】重写整个 BuildDataTable 方法 ▼▼▼
        private void BuildDataTable()
            {
            var newTable = new DataTable();
            if (_masterRecordList == null || !_masterRecordList.Any())
                {
                HistoryDataTable = newTable;
                OnPropertyChanged(nameof(HistoryDataTable));
                return;
                }

            // 1. 添加固定的初始列
            newTable.Columns.Add("查询时间", typeof(string));
            newTable.Columns.Add("API服务", typeof(string));

            // 2. 严格按照 settings 中定义的顺序来创建数据列
            foreach (var mappingRulePair in _settings.FriendlyNameMappings)
                {
                var mappingRule = mappingRulePair.Value;
                // 使用规则的友好名称作为列的标题。实际的绑定是在后台通过原始Key进行的。
                // 注意：列名必须是唯一的，这里我们用友好名称。
                if (!newTable.Columns.Contains(mappingRule.DefaultFriendlyName))
                    {
                    newTable.Columns.Add(mappingRule.DefaultFriendlyName, typeof(string));
                    }
                }

            // 3. 添加任何在规则中未定义的“其他”列，以防API返回了新字段
            var allKeysInHistory = _masterRecordList
                .SelectMany(r => r.Data.Select(kvp => kvp.Key))
                .Distinct();
            var allKnownAliases = _settings.FriendlyNameMappings.Values.SelectMany(r => r.Aliases).ToList();

            foreach (var key in allKeysInHistory)
                {
                if (!allKnownAliases.Contains(key) && !newTable.Columns.Contains(key))
                    {
                    newTable.Columns.Add(key, typeof(string)); // 对于未知列，直接用原始Key
                    }
                }

            // 4. 填充数据行
            foreach (var record in _masterRecordList.OrderByDescending(r => r.Timestamp))
                {
                var row = newTable.NewRow();
                row["查询时间"] = record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff");
                row["API服务"] = record.ServiceType.ToString();

                foreach (var kvp in record.Data)
                    {
                    // 找到这个原始Key属于哪个规则
                    var rule = _settings.FriendlyNameMappings.Values.FirstOrDefault(r => r.Aliases.Contains(kvp.Key));
                    if (rule != null)
                        {
                        // 使用规则的友好名称来填充对应的列
                        row[rule.DefaultFriendlyName] = kvp.Value;
                        }
                    else if (newTable.Columns.Contains(kvp.Key))
                        {
                        // 如果是未知的Key，直接用Key填充
                        row[kvp.Key] = kvp.Value;
                        }
                    }
                newTable.Rows.Add(row);
                }

            HistoryDataTable = newTable;
            OnPropertyChanged(nameof(HistoryDataTable));
            }

        #endregion

        #region --- INotifyPropertyChanged 实现 ---
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        #endregion
        }
    }