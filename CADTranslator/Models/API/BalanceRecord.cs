// 文件路径: CADTranslator/Models/BalanceRecord.cs

using System;
using System.Collections.Generic;

namespace CADTranslator.Models.API
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
        /// 【已修改】存储从API返回的原始、完整的Key-Value对列表。
        /// 这是实现动态表格的基础。
        /// </summary>
        public List<KeyValuePair<string, string>> Data { get; set; } = new List<KeyValuePair<string, string>>();

        // --- 下面的属性是为了方便在主窗口上显示，而不是用于历史记录表格 ---

        /// <summary>
        /// 【已废弃】不再使用这个属性来存储格式化的余额信息。
        /// 我们保留它以确保旧的设置文件反序列化时不会出错，但不再填充它。
        /// </summary>
        [Obsolete("此属性已废弃，请使用Data属性。")]
        public string BalanceInfo { get; set; }

        /// <summary>
        /// 【已废弃】
        /// </summary>
        [Obsolete("此属性已废弃，请使用Data属性。")]
        public string UserId { get; set; }

        /// <summary>
        /// 【已废弃】
        /// </summary>
        [Obsolete("此属性已废弃，请使用Data属性。")]
        public string AccountStatus { get; set; }
        }
    }