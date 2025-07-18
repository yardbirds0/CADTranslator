// 文件路径: CADTranslator/Models/ApiUsageStats.cs

namespace CADTranslator.Models
    {
    /// <summary>
    /// 存储单个API服务的用量统计数据
    /// </summary>
    public class ApiUsageStats
        {
        /// <summary>
        /// 累计成功翻译的段落总数
        /// </summary>
        public long TotalParagraphsTranslated { get; set; } = 0;

        /// <summary>
        /// 累计成功翻译的字符总数 (源语言)
        /// </summary>
        public long TotalCharactersTranslated { get; set; } = 0;

        /// <summary>
        /// 累计翻译总耗时 (秒)
        /// </summary>
        public double TotalTimeInSeconds { get; set; } = 0.0;

        /// <summary>
        /// 平均翻译每个段落所需时间 (秒)
        /// </summary>
        public double AverageTimePerParagraph => TotalParagraphsTranslated > 0 ? TotalTimeInSeconds / TotalParagraphsTranslated : 0;
        }
    }