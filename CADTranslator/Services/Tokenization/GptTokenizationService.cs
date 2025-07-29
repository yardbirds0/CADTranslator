// 文件路径: CADTranslator/Services/Tokenization/GptTokenizationService.cs
// 【这是一个新文件】

using Microsoft.ML.Tokenizers;
using System;
using System.Collections.Concurrent;

namespace CADTranslator.Services.Tokenization
    {
    /// <summary>
    /// 使用 Microsoft.ML.Tokenizers 和 Tiktoken 算法，为GPT系列模型提供本地Token计算服务。
    /// 此服务实现了 ITokenizationService 接口，并且是线程安全的。
    /// </summary>
    public class GptTokenizationService : ITokenizationService
        {
        private static readonly ConcurrentDictionary<string, Tokenizer> _tokenizerCache = new ConcurrentDictionary<string, Tokenizer>();
        private const string FallbackModel = "gpt-4o";

        public bool CanTokenize(string modelName)
            {
            if (string.IsNullOrWhiteSpace(modelName)) return false;
            return modelName.IndexOf("gpt", StringComparison.OrdinalIgnoreCase) >= 0;
            }

        public (int TokenCount, string ErrorMessage) CountTokens(string textToCount, string modelName)
            {
            if (string.IsNullOrEmpty(textToCount)) return (0, null);

            try
                {
                var tokenizer = GetTokenizer(modelName);
                return (tokenizer.CountTokens(textToCount), null);
                }
            catch (Exception ex)
                {
                return (-1, ex.Message);
                }
            }

        private Tokenizer GetTokenizer(string modelName)
            {
            // 如果模型名为空或无效，则直接使用备用模型进行估算
            string effectiveModelName = CanTokenize(modelName) ? modelName : FallbackModel;

            if (_tokenizerCache.TryGetValue(effectiveModelName, out var tokenizer))
                {
                return tokenizer;
                }

            try
                {
                tokenizer = TiktokenTokenizer.CreateForModel(effectiveModelName);
                }
            catch (Exception ex)
                {
                // 如果创建分词器失败（例如，数据DLL丢失），则抛出明确的异常
                throw new InvalidOperationException($"无法初始化Tiktoken分词器({effectiveModelName})。请确保相关NuGet包已正确安装。错误: {ex.Message}");
                }

            _tokenizerCache.TryAdd(effectiveModelName, tokenizer);
            return tokenizer;
            }
        }
    }