// 文件路径: CADTranslator/Services/Translation/OpenAiTranslator.cs
// 【完整文件替换】

using CADTranslator.Models;
using CADTranslator.Models.API;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading; // ◄◄◄ 【新增】引入 CancellationToken
using System.Threading.Tasks;

namespace CADTranslator.Services.Translation
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
        public bool IsTokenCountSupported => false;
        public bool IsBatchTranslationSupported => false;

        #endregion

        #region --- 3. 核心与扩展功能 (ITranslator 实现) ---

        // ▼▼▼ 【方法重写】重写整个 TranslateAsync 方法以支持 CancellationToken ▼▼▼
        // ▼▼▼ 请用此方法完整替换旧的 TranslateAsync 方法 ▼▼▼
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

                // 【核心修改】创建一个代表网络请求的任务
                var translationTask = _lazyClient.Value.CompleteChatAsync(messages, cancellationToken: cancellationToken);

                // 【核心修改】创建一个20秒的超时任务
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);

                // 【核心修改】让网络任务和超时任务进行“赛跑”
                var completedTask = await Task.WhenAny(translationTask, timeoutTask);

                if (completedTask == timeoutTask)
                    {
                    // 如果是超时任务先完成，就抛出网络错误异常
                    throw new ApiException(ApiErrorType.NetworkError, ServiceType, "请求超时 (超过20秒)。");
                    }

                // 如果是网络任务先完成，就获取它的结果
                ChatCompletion completion = await translationTask;

                if (completion.Content != null && completion.Content.Any())
                    {
                    return completion.Content[0].Text.Trim();
                    }

                throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, $"API未返回任何内容。完成原因: {completion.FinishReason}");
                }
            // 捕获由我们主动熔断（cancellationToken）引发的取消
            catch (OperationCanceledException)
                {
                throw;
                }
            catch (Exception ex)
                {
                // 如果在捕获通用异常时，发现取消信号已经被触发了，那么也按“取消”处理
                if (cancellationToken.IsCancellationRequested)
                    {
                    throw new OperationCanceledException();
                    }

                // 其他所有情况，都视为API接口错误
                throw new ApiException(ApiErrorType.ApiError, ServiceType, ex.Message);
                }
            }

        public Task<List<string>> TranslateBatchAsync(List<string> textsToTranslate, string fromLanguage, string toLanguage, CancellationToken cancellationToken)
            {
            throw new NotSupportedException("当前OpenAI集成不支持批量翻译。");
            }
        public Task<List<string>> GetModelsAsync(CancellationToken cancellationToken)
            {
            throw new NotSupportedException("当前OpenAI集成不支持在线获取模型列表。请在模型管理中手动添加。");
            }

        public Task<List<KeyValuePair<string, string>>> CheckBalanceAsync()
            {
            throw new NotSupportedException("OpenAI API 服务不支持在线查询余额。");
            }

        public Task<int> CountTokensAsync(string textToCount)
            {
            throw new NotSupportedException("当前OpenAI集成不支持计算Token。");
            }
        #endregion
        }
    }