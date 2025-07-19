// 文件路径: CADTranslator/Services/Translation/OpenAiTranslator.cs
// 【完整文件替换】

using CADTranslator.Models;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading; // ◄◄◄ 【新增】引入 CancellationToken
using System.Threading.Tasks;

namespace CADTranslator.Services
    {
    public class OpenAiTranslator : ITranslator
        {
        #region --- 字段 ---

        private readonly Lazy<ChatClient> _lazyClient;
        private readonly string _model;
        private readonly string _apiKey;

        #endregion

        #region --- 构造函数 ---

        public OpenAiTranslator(string apiKey, string model)
            {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ApiException(ApiErrorType.ConfigurationError, ApiServiceType.OpenAI, "API Key 不能为空。");

            _apiKey = apiKey;
            _model = model;

            _lazyClient = new Lazy<ChatClient>(() => new ChatClient(_model, _apiKey));
            }

        #endregion

        #region --- 1. 身份标识 (ITranslator 实现) ---

        public ApiServiceType ServiceType => ApiServiceType.OpenAI;
        public string DisplayName => "ChatGPT";
        public string ApiDocumentationUrl => "https://platform.openai.com/docs/introduction";

        #endregion

        #region --- 2. 能力声明 (ITranslator 实现) ---

        public bool IsApiKeyRequired => true;
        public bool IsUserIdRequired => false;
        public bool IsApiUrlRequired => false;
        public bool IsModelRequired => true;
        public bool IsPromptSupported => true;
        public bool IsModelFetchingSupported => false;
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
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage($"You are a professional translator for Civil Engineering drawings. Your task is to translate the user's text from {fromLanguage} to {toLanguage}. Do not add any extra explanations, just return the translated text. If you encounter symbols, keep their original style."),
                    new UserChatMessage(textToTranslate)
                };

                // ▼▼▼ 【核心修改】将 cancellationToken 传递给 CompleteChatAsync 方法 ▼▼▼
                ChatCompletion completion = await _lazyClient.Value.CompleteChatAsync(messages, cancellationToken: cancellationToken);

                if (completion.Content != null && completion.Content.Any())
                    {
                    return completion.Content[0].Text.Trim();
                    }

                throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, $"API未返回任何内容。完成原因: {completion.FinishReason}");
                }
            // ◄◄◄ 【新增】专门捕获由 CancellationToken 引发的 OperationCanceledException
            catch (OperationCanceledException)
                {
                // 当任务被取消时，库会抛出此异常，我们直接重新抛出
                throw;
                }
            catch (Exception ex)
                {
                // 检查是否是因为取消令牌被触发而导致的通用异常
                if (cancellationToken.IsCancellationRequested)
                    {
                    // 如果是，则抛出标准的取消异常
                    throw new OperationCanceledException();
                    }

                // 通过检查异常消息来粗略判断是否为网络问题
                if (ex.Message.Contains("timed out") || ex.Message.Contains("Error while copying content to a stream"))
                    {
                    throw new ApiException(ApiErrorType.NetworkError, ServiceType, $"网络请求失败: {ex.Message}");
                    }

                // 否则，将其视为普通的API错误
                throw new ApiException(ApiErrorType.ApiError, ServiceType, ex.Message);
                }
            }

        public Task<List<string>> GetModelsAsync()
            {
            throw new NotSupportedException("当前OpenAI集成不支持在线获取模型列表。请在模型管理中手动添加。");
            }

        public Task<List<KeyValuePair<string, string>>> CheckBalanceAsync()
            {
            throw new NotSupportedException("OpenAI API 服务不支持在线查询余额。");
            }

        #endregion
        }
    }