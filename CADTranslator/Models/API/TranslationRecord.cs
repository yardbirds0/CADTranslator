// 文件路径: CADTranslator/Models/API/TranslationRecord.cs
// 【这是一个新文件】

using System;
using System.Collections.Generic;

namespace CADTranslator.Models.API
    {
    /// <summary>
    /// 存储单次完整翻译任务的所有使用记录和上下文信息。
    /// </summary>
    public class TranslationRecord
        {
        // --- 核心信息 ---
        public DateTime Timestamp { get; set; }
        public ApiServiceType ServiceType { get; set; }
        public string ModelName { get; set; }

        // --- 性能与工作量 ---
        public double DurationInSeconds { get; set; }
        public int ParagraphCount { get; set; }
        public long SourceCharacterCount { get; set; }
        public long TranslatedCharacterCount { get; set; }
        public string SourceLanguage { get; set; }
        public string TargetLanguage { get; set; }

        // --- 消耗指标 ---
        public TranslationUsage Usage { get; set; }

        // --- 上下文配置 ---
        public bool IsLiveLayoutEnabled { get; set; }
        public string ConcurrencyLevel { get; set; }
        public PromptSendingMode SendingMode { get; set; }
        public PromptTemplateType PromptTemplateUsed { get; set; }

        // --- 状态与错误 ---
        public bool WasCancelled { get; set; }
        public int FailureCount { get; set; }
        public List<string> FailureMessages { get; set; } = new List<string>();
        }
    }