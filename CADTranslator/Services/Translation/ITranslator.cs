using System.Threading.Tasks; // 异步编程所需

namespace CADTranslator.Services
{
    /// <summary>
    /// 为所有翻译服务定义了统一的规范（或称“契约”）
    /// </summary>
    public interface ITranslator
    {
        /// <summary>
        /// 异步翻译一段文本
        /// </summary>
        /// <param name="textToTranslate">待翻译的文本</param>
        /// <param name="fromLanguage">源语言代码 (如 "auto")</param>
        /// <param name="toLanguage">目标语言代码 (如 "en")</param>
        /// <returns>翻译后的文本</returns>
        Task<string> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage);
    }
}