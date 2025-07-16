using CADTranslator.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System;
using System.ComponentModel; // 1. 引入命名空间
using System.Runtime.CompilerServices; // 2. 引入命名空间

namespace CADTranslator.UI.ViewModels
    {
    // 3. 让这个类实现 INotifyPropertyChanged 接口
    public class BalanceHistoryViewModel : INotifyPropertyChanged
        {
        /// <summary>
        /// 用于绑定到DataGrid的余额历史记录集合
        /// </summary>
        public ObservableCollection<BalanceRecord> History { get; set; }

        public BalanceHistoryViewModel()
            {
            History = new ObservableCollection<BalanceRecord>();

#if DEBUG
            History.Add(new BalanceRecord { Timestamp = DateTime.Now, ServiceType = ApiServiceType.SiliconFlow, UserId = "sample-user-123", AccountStatus = "active", BalanceInfo = "余额: ¥10.24" });
            History.Add(new BalanceRecord { Timestamp = DateTime.Now.AddDays(-1), ServiceType = ApiServiceType.SiliconFlow, UserId = "sample-user-123", AccountStatus = "active", BalanceInfo = "余额: ¥12.88" });
#endif
            }

        public BalanceHistoryViewModel(IEnumerable<BalanceRecord> historyRecords)
            {
            History = new ObservableCollection<BalanceRecord>(historyRecords);
            }

        // 4. 添加 INotifyPropertyChanged 的完整实现
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
            {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
            }
        }
    }