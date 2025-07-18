// 文件路径: CADTranslator/UI/ViewModels/BalanceHistoryViewModel.cs

using CADTranslator.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Runtime.CompilerServices;
using Wpf.Ui.Input;

namespace CADTranslator.UI.ViewModels
    {
    public class BalanceHistoryViewModel : INotifyPropertyChanged
        {
        #region --- 字段与事件 ---

        private readonly ObservableCollection<BalanceRecord> _masterRecordList;

        /// <summary>
        /// 【新增】当用户请求删除记录时触发此事件。
        /// 参数是需要被删除的BalanceRecord的列表。
        /// </summary>
        public event Action<List<BalanceRecord>> DeleteRequested;

        #endregion

        #region --- 属性 ---

        public DataTable HistoryDataTable { get; private set; }

        /// <summary>
        /// 【新增】删除选中记录的命令。
        /// </summary>
        public RelayCommand DeleteCommand { get; }

        #endregion

        #region --- 构造函数 ---

        public BalanceHistoryViewModel(ObservableCollection<BalanceRecord> historyRecords)
            {
            _masterRecordList = historyRecords;
            // 【新增】初始化删除命令
            DeleteCommand = new RelayCommand(ExecuteDelete, CanExecuteDelete);
            BuildDataTable();

            // 【新增】当原始集合变化时，自动重建数据表以刷新UI
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
                    // 注意：这里我将时间戳的解析改为了 TryParse，代码更健壮
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

        private void BuildDataTable()
            {
            var newTable = new DataTable();
            if (_masterRecordList == null || !_masterRecordList.Any())
                {
                HistoryDataTable = newTable;
                OnPropertyChanged(nameof(HistoryDataTable)); // 通知UI更新
                return;
                }

            newTable.Columns.Add("查询时间", typeof(string));
            newTable.Columns.Add("API服务", typeof(string));

            var allKeys = _masterRecordList
                .Where(r => r.Data != null)
                .SelectMany(r => r.Data.Select(kvp => kvp.Key))
                .Distinct()
                .ToList();

            foreach (var key in allKeys)
                {
                if (!newTable.Columns.Contains(key))
                    {
                    newTable.Columns.Add(key, typeof(string));
                    }
                }

            foreach (var record in _masterRecordList.OrderByDescending(r => r.Timestamp))
                {
                var row = newTable.NewRow();
                row["查询时间"] = record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"); // 使用毫秒以确保唯一性
                row["API服务"] = record.ServiceType.ToString();

                if (record.Data != null)
                    {
                    foreach (var kvp in record.Data)
                        {
                        if (newTable.Columns.Contains(kvp.Key))
                            {
                            row[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                newTable.Rows.Add(row);
                }

            HistoryDataTable = newTable;
            OnPropertyChanged(nameof(HistoryDataTable)); // 通知UI更新
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