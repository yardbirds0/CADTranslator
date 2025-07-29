// 文件路径: CADTranslator/Services/Tokenization/ITokenizationService.cs
// 【这是一个新文件】

namespace CADTranslator.Services.Tokenization
    {
    /// <summary>
    /// 为本地Token计算服务定义一个标准接口。
    /// </summary>
    public interface ITokenizationService
        {
        /// <summary>
        /// 检查此服务是否能够为指定的模型名称计算Token。
        /// </summary>
        /// <param name="modelName">要检查的模型名称。</param>
        /// <returns>如果可以处理，则返回 true；否则返回 false。</returns>
        bool CanTokenize(string modelName);

        /// <summary>
        /// 为给定的模型计算文本的Token数量。
        /// </summary>
        /// <param name="textToCount">要计算的文本。</param>
        /// <param name="modelName">具体的模型名称，用于选择合适的分词器。</param>
        /// <returns>一个包含Token数量和错误信息的元组。成功时，错误信息为null。</returns>
        (int TokenCount, string ErrorMessage) CountTokens(string textToCount, string modelName);
        }
    }