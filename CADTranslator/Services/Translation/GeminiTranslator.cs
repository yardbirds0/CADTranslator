// 文件路径: CADTranslator/Services/Translation/GeminiTranslator.cs
// 【完整文件替换】

using CADTranslator.Models;
using Mscc.GenerativeAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading; // ◄◄◄ 【新增】引入 CancellationToken
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
                throw new ApiException(ApiErrorType.ConfigurationError, ApiServiceType.Gemini, "API Key 不能为空。");

            _apiKey = apiKey;
            _model = model;

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

        // ▼▼▼ 【方法重写】重写整个 TranslateAsync 方法以支持 CancellationToken ▼▼▼
        public async Task<string> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage, CancellationToken cancellationToken)
            {
            if (string.IsNullOrWhiteSpace(_model))
                throw new ApiException(ApiErrorType.ConfigurationError, ServiceType, "模型名称不能为空。");

            // 在发起API调用前，检查任务是否已被取消
            cancellationToken.ThrowIfCancellationRequested();

            try
                {
                var generativeModel = _lazyGoogleAI.Value.GenerativeModel(model: _model);
                string prompt = $"You are a professional translator for Civil Engineering drawings. Your task is to translate the user's text from {fromLanguage} to {toLanguage}. Do not add any extra explanations, just return the translated text. If you encounter symbols, keep their original style.\n\nText to translate:\n---\n{textToTranslate}\n---";

                var response = await generativeModel.GenerateContent(prompt);

                if (response?.Text == null)
                    {
                    throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, "API返回了空或无效的响应内容。");
                    }

                return response.Text.Trim();
                }
            // ◄◄◄ 【新增】捕获 TaskCanceledException
            catch (TaskCanceledException)
                {
                // 如果能捕获到这个异常（例如，如果未来的库版本支持取消），我们重新抛出它
                throw;
                }
            catch (Exception ex)
                {
                // 检查是否是因为取消令牌被触发而导致的通用异常
                if (cancellationToken.IsCancellationRequested)
                    {
                    // 如果是，则抛出标准的取消异常
                    throw new TaskCanceledException();
                    }

                // 否则，将其视为普通的API错误
                // Gemini库的网络错误和API业务错误通常都封装在Exception的Message中
                // 我们可以通过检查消息内容来粗略判断是否为网络问题
                if (ex.Message.Contains("Request timed out") || ex.Message.Contains("NameResolutionFailure"))
                    {
                    throw new ApiException(ApiErrorType.NetworkError, ServiceType, $"网络请求失败: {ex.Message}");
                    }

                throw new ApiException(ApiErrorType.ApiError, ServiceType, ex.Message);
                }
            }

        public async Task<List<string>> GetModelsAsync()
            {
            try
                {
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
                if (ex.Message.Contains("Request timed out") || ex.Message.Contains("NameResolutionFailure"))
                    {
                    throw new ApiException(ApiErrorType.NetworkError, ServiceType, $"获取模型列表时网络请求失败: {ex.Message}");
                    }
                throw new ApiException(ApiErrorType.ApiError, ServiceType, $"获取模型列表时发生错误: {ex.Message}");
                }
            }

        public Task<List<KeyValuePair<string, string>>> CheckBalanceAsync()
            {
            throw new NotSupportedException("Gemini API 服务不支持在线查询余额。");
            }

        #endregion
        }
    }