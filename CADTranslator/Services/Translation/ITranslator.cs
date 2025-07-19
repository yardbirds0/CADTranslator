// 文件路径: CADTranslator/Services/Translation/ITranslator.cs

using CADTranslator.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CADTranslator.Services
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

        #endregion

        #region --- 3. 核心与扩展功能 ---

        /// <summary>
        /// (核心功能) 异步翻译一段文本。
        /// </summary>
        /// <param name="textToTranslate">待翻译的文本</param>
        /// <param name="fromLanguage">源语言代码</param>
        /// <param name="toLanguage">目标语言代码</param>
        /// <returns>翻译后的文本</returns>
        Task<string> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage, CancellationToken cancellationToken);

        /// <summary>
        /// (扩展功能) 异步获取此服务可用的模型列表。
        /// 如果 IsModelFetchingSupported 为 false，调用此方法应抛出 NotSupportedException。
        /// </summary>
        /// <returns>一个包含模型名称字符串的列表。</returns>
        Task<List<string>> GetModelsAsync();

        /// <summary>
        /// (扩展功能) 异步查询账户余额或用量信息。
        /// 如果 IsBalanceCheckSupported 为 false，调用此方法应抛出 NotSupportedException。
        /// </summary>
        /// <returns>一个Key-Value对的列表，用于在历史记录中动态显示。</returns>
        Task<List<KeyValuePair<string, string>>> CheckBalanceAsync();

        #endregion
        }
    }