using System;

namespace CADTranslator.Models
    {
    /// <summary>
    /// 代表单条API余额查询的历史记录
    /// </summary>
    public class BalanceRecord
        {
        /// <summary>
        /// 查询发生的时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 被查询的API服务类型 (例如: SiliconFlow)
        /// </summary>
        public ApiServiceType ServiceType { get; set; }

        /// <summary>
        /// 从API返回的用户ID
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// 从API返回的账户状态
        /// </summary>
        public string AccountStatus { get; set; }

        /// <summary>
        /// 格式化后的余额信息，用于显示
        /// </summary>
        public string BalanceInfo { get; set; }
        }
    }