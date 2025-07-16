using CADTranslator.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System; // 需要这个来使用 DateTime

namespace CADTranslator.UI.ViewModels
    {
    public class BalanceHistoryViewModel
        {
        /// <summary>
        /// 用于绑定到DataGrid的余额历史记录集合
        /// </summary>
        public ObservableCollection<BalanceRecord> History { get; set; }

        /// <summary>
        /// 【新增】无参数的构造函数，供WPF框架和XAML设计器使用。
        /// </summary>
        public BalanceHistoryViewModel()
            {
            // 初始化集合，确保它永远不为null
            History = new ObservableCollection<BalanceRecord>();

            // (可选) 在DEBUG模式下为设计器提供一些示例数据，方便预览
#if DEBUG
            History.Add(new BalanceRecord { Timestamp = DateTime.Now, ServiceType = ApiServiceType.SiliconFlow, UserId = "sample-user-123", AccountStatus = "active", BalanceInfo = "总余额: ¥10.24" });
            History.Add(new BalanceRecord { Timestamp = DateTime.Now.AddDays(-1), ServiceType = ApiServiceType.SiliconFlow, UserId = "sample-user-123", AccountStatus = "active", BalanceInfo = "总余额: ¥12.88" });
#endif
            }

        /// <summary>
        /// 我们原有的构造函数，在程序运行时接收真实数据。
        /// </summary>
        /// <param name="historyRecords"></param>
        public BalanceHistoryViewModel(IEnumerable<BalanceRecord> historyRecords)
            {
            // 从主ViewModel接收历史记录，并填充到自己的集合中
            History = new ObservableCollection<BalanceRecord>(historyRecords);
            }
        }
    }