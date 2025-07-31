// 文件路径: CADTranslator/Services/Translation/ITranslator.cs

using CADTranslator.Models;
using CADTranslator.Models.API;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CADTranslator.Services.Translation
    {
    /// <summary>
    /// 定义了所有翻译服务提供商的统一规范。
    /// 它不仅包含翻译功能，还定义了服务的能力和附加功能。
    /// </summary>
    public interface ITranslator
        {
        #region --- 1. 身份标识 ---

        /// <summary>
        /// 获取此服务提供商的唯一枚举类型。
        /// </summary>
        ApiServiceType ServiceType { get; }

        /// <summary>
        /// 获取用于在UI上显示的友好名称 (例如: "百度翻译")。
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// 获取此服务的官方文档或帮助页面的URL。
        /// 如果没有，则返回空字符串。
        /// </summary>
        string ApiDocumentationUrl { get; }

        #endregion

        #region --- 2. 能力声明 (用于UI绑定和逻辑判断) ---

        /// <summary>
        /// 获取一个值，该值指示此服务是否需要配置API密钥。
        /// </summary>
        bool IsApiKeyRequired { get; }

        /// <summary>
        /// 获取一个值，该值指示此服务是否需要配置用户ID或App ID。
        /// </summary>
        bool IsUserIdRequired { get; }

        /// <summary>
        /// 获取一个值，该值指示此服务是否需要配置自定义的API URL。
        /// </summary>
        bool IsApiUrlRequired { get; }

        /// <summary>
        /// 获取一个值，该值指示此服务是否必须指定一个模型才能工作。
        /// </summary>
        bool IsModelRequired { get; }

        /// <summary>
        /// 获取一个值，该值指示此服务是否支持使用自定义提示词。
        /// (例如，百度翻译等专用API可能不支持)
        /// </summary>
        bool IsPromptSupported { get; }

        /// <summary>
        /// 获取一个值，该值指示此服务是否支持在线获取其可用模型列表。
        /// </summary>
        bool IsModelFetchingSupported { get; }

        /// <summary>
        /// 获取一个值，该值指示此服务是否支持查询账户余额或用量。
        /// </summary>
        bool IsBalanceCheckSupported { get; }

        /// <summary>
        /// 【新增】获取一个值，该值指示此服务是否支持计算Token用量。
        /// </summary>
        bool IsTokenCountSupported { get; }

        /// <summary>
        /// 【新增】获取一个值，该值指示此服务是否支持本地Token计算。
        /// </summary>
        bool IsLocalTokenCountSupported { get; } // <-- 在这里添加新属性

        /// <summary>
        /// 【新增】获取此API服务的计费方式。
        /// </summary>
        BillingUnit UnitType { get; } // <-- 在这里添加新属性

        /// <summary>
        /// 【新增】获取一个值，该值指示此服务是否支持JSON模式的批量翻译。
        /// </summary>
        bool IsBatchTranslationSupported { get; }

        #endregion

        #region --- 3. 核心与扩展功能 ---
        /// <summary>
        /// (核心功能) 异步翻译一段文本。
        /// </summary>
        /// <param name="textToTranslate">待翻译的文本</param>
        /// <param name="fromLanguage">源语言代码</param>
        /// <param name="toLanguage">目标语言代码</param>
        /// <param name="promptTemplate">【新增】用户选择或输入的提示词模板。</param>
        /// <returns>翻译后的文本</returns>
        Task<(string TranslatedText, TranslationUsage Usage)> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage, string promptTemplate, CancellationToken cancellationToken);


        /// <summary>
        /// 【新增】(核心功能) 异步批量翻译多段文本。
        /// </summary>
        /// <param name="textsToTranslate">待翻译的文本列表</param>
        /// <param name="fromLanguage">源语言代码</param>
        /// <param name="toLanguage">目标语言代码</param>
        /// <param name="promptTemplate">【新增】用户选择或输入的提示词模板。</param>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>一个与输入列表顺序对应的翻译结果列表。</returns>
        Task<(List<string> TranslatedTexts, TranslationUsage Usage)> TranslateBatchAsync(List<string> textsToTranslate, string fromLanguage, string toLanguage, string promptTemplate, CancellationToken cancellationToken);

        /// <summary>
        /// (扩展功能) 异步获取此服务可用的模型列表。
        /// 如果 IsModelFetchingSupported 为 false，调用此方法应抛出 NotSupportedException。
        /// </summary>
        /// <param name="cancellationToken">用于取消操作的令牌。</param>
        /// <returns>一个包含模型名称字符串的列表。</returns>
        Task<List<string>> GetModelsAsync(CancellationToken cancellationToken);

        /// <summary>
        /// (扩展功能) 异步查询账户余额或用量信息。
        /// 如果 IsBalanceCheckSupported 为 false，调用此方法应抛出 NotSupportedException。
        /// </summary>
        /// <returns>一个Key-Value对的列表，用于在历史记录中动态显示。</returns>
        Task<List<KeyValuePair<string, string>>> CheckBalanceAsync();

        /// <summary>
        /// 【新增】(扩展功能) 异步计算一段文本的Token数量。
        /// 如果 IsTokenCountSupported 为 false，调用此方法应抛出 NotSupportedException。
        /// </summary>
        /// <param name="textToCount">需要计算的文本。</param>
        /// <returns>一个包含Token数量的整数。</returns>
        Task<int> CountTokensAsync(string textToCount, CancellationToken cancellationToken); // <-- 在这里增加参数

        Task PerformPreflightCheckAsync(CancellationToken cancellationToken);

        #endregion
        }
    }