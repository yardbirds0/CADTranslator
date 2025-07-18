// 文件路径: CADTranslator/Services/Translation/GeminiTranslator.cs

using CADTranslator.Models;
using Mscc.GenerativeAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CADTranslator.Services
    {
    public class GeminiTranslator : ITranslator
        {
        #region --- 字段 ---

        private readonly Lazy<IGenerativeAI> _lazyGoogleAI;
        private readonly string _model;
        private readonly string _apiKey;

        #endregion

        #region --- 构造函数 ---

        public GeminiTranslator(string apiKey, string model)
            {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException(nameof(apiKey), "Gemini API Key 不能为空。");
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model), "Gemini 模型名称不能为空。");

            _apiKey = apiKey;
            _model = model;

            // 【已修正】应用惰性加载
            _lazyGoogleAI = new Lazy<IGenerativeAI>(() => new GoogleAI(apiKey: _apiKey));
            }

        #endregion

        #region --- 1. 身份标识 (ITranslator 实现) ---

        public ApiServiceType ServiceType => ApiServiceType.Gemini;
        public string DisplayName => "谷歌Gemini";
        public string ApiDocumentationUrl => "https://ai.google.dev/docs";

        #endregion

        #region --- 2. 能力声明 (ITranslator 实现) ---

        public bool IsApiKeyRequired => true;
        public bool IsUserIdRequired => false;
        public bool IsApiUrlRequired => false;
        public bool IsModelRequired => true;
        public bool IsPromptSupported => true;
        public bool IsModelFetchingSupported => true;
        public bool IsBalanceCheckSupported => false;

        #endregion

        #region --- 3. 核心与扩展功能 (ITranslator 实现) ---

        public async Task<string> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage)
            {
            try
                {
                // 【已修正】使用 .Value 获取实例
                var generativeModel = _lazyGoogleAI.Value.GenerativeModel(model: _model);
                string prompt = $"You are a professional translator for Civil Engineering drawings. Your task is to translate the user's text from {fromLanguage} to {toLanguage}. Do not add any extra explanations, just return the translated text. If you encounter symbols, keep their original style.\n\nText to translate:\n---\n{textToTranslate}\n---";

                var response = await generativeModel.GenerateContent(prompt);

                return response?.Text?.Trim() ?? "翻译失败：API未返回有效内容。";
                }
            catch (Exception ex)
                {
                return $"调用Gemini API时发生异常: {ex.Message.Replace('\t', ' ')}";
                }
            }

        public async Task<List<string>> GetModelsAsync()
            {
            try
                {
                // 【已修正】使用 .Value 获取实例
                var generativeModel = _lazyGoogleAI.Value.GenerativeModel();
                var models = await generativeModel.ListModels();

                if (models == null || !models.Any())
                    {
                    return new List<string>();
                    }

                return models
                       .Where(m => m.SupportedGenerationMethods.Contains("generateContent"))
                       .Select(m => m.Name.Replace("models/", ""))
                       .ToList();
                }
            catch (Exception ex)
                {
                throw new Exception($"获取Gemini模型列表时发生错误: {ex.Message}", ex);
                }
            }

        public Task<List<KeyValuePair<string, string>>> CheckBalanceAsync()
            {
            throw new NotSupportedException("Gemini API 服务不支持在线查询余额。");
            }

        #endregion
        }
    }