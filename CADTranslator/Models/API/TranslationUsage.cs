// 文件路径: CADTranslator/Models/API/TranslationUsage.cs
// 【这是一个新文件】

namespace CADTranslator.Models.API
    {
    /// <summary>
    /// 存储单次翻译请求的Token用量信息。
    /// </summary>
    public class TranslationUsage
        {
        public long PromptTokens { get; set; }
        public long CompletionTokens { get; set; }
        public long TotalTokens { get; set; }

        /// <summary>
        /// 将另一个用量实例的数据累加到当前实例。
        /// </summary>
        public void Add(TranslationUsage other)
            {
            if (other != null)
                {
                this.PromptTokens += other.PromptTokens;
                this.CompletionTokens += other.CompletionTokens;
                this.TotalTokens += other.TotalTokens;
                }
            }
        }
    }