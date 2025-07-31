// 文件路径: CADTranslator/Services/Translation/OpenAiTranslator.cs
// 【请用此代码完整替换】

using CADTranslator.Models;
using CADTranslator.Models.API;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CADTranslator.Services.Translation
    {
    public class OpenAiTranslator : ITranslator
        {
        #region --- 字段 ---

        // 【恢复】使用您原来可以正常工作的 Lazy<ChatClient>
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

            // 【恢复】使用您原来可以正常工作的初始化方式
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
        public bool IsModelFetchingSupported => false; // OpenAI 库的这个版本不直接支持模型列表
        public bool IsBalanceCheckSupported => false;
        public bool IsTokenCountSupported => false;   // OpenAI 库的这个版本不直接支持Token计数
        public bool IsBatchTranslationSupported => false;
        public bool IsLocalTokenCountSupported => true;
        public BillingUnit UnitType => BillingUnit.Token;
        #endregion

        #region --- 3. 核心与扩展功能 (ITranslator 实现) ---

        public async Task<(string TranslatedText, TranslationUsage Usage)> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage, string promptTemplate, CancellationToken cancellationToken)
            {
            if (string.IsNullOrWhiteSpace(_model))
                throw new ApiException(ApiErrorType.ConfigurationError, ServiceType, "模型名称不能为空。");

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                try
                    {
                    // 设置我们自己的、15秒的“连接超时闹钟”
                    cts.CancelAfter(TimeSpan.FromSeconds(15));

                    var finalPrompt = promptTemplate
                        .Replace("{fromLanguage}", fromLanguage)
                        .Replace("{toLanguage}", toLanguage);

                    var messages = new List<ChatMessage>
                    {
                        new SystemChatMessage(finalPrompt),
                        new UserChatMessage(textToTranslate)
                    };

                    // 【恢复】使用您原来可以正常工作的API调用方式
                    var completionResponse = await _lazyClient.Value.CompleteChatAsync(messages, cancellationToken: cts.Token);

                    // ▼▼▼ 【核心修正】从这里开始 ▼▼▼
                    // 1. 直接从“信封”中取出“信件”（真正的ChatCompletion对象）
                    ChatCompletion completion = completionResponse; // 这个库版本支持隐式转换

                    // 2. 对“信件”进行判断
                    if (completion.Content != null && completion.Content.Any())
                        {
                        return (completion.Content[0].Text.Trim(), null);
                        }

                    throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, $"API未返回任何内容。完成原因: {completion.FinishReason}");
                    // ▲▲▲ 修改结束 ▲▲▲
                    }
                catch (TaskCanceledException)
                    {
                    if (cancellationToken.IsCancellationRequested) throw;
                    throw new ApiException(ApiErrorType.NetworkError, ServiceType, "连接超时 (超过15秒)，请检查网络或代理设置。");
                    }
                catch (Exception ex)
                    {
                    if (cancellationToken.IsCancellationRequested)
                        {
                        throw new OperationCanceledException();
                        }
                    throw new ApiException(ApiErrorType.ApiError, ServiceType, ex.Message);
                    }
                }
            }

        public Task<(List<string> TranslatedTexts, TranslationUsage Usage)> TranslateBatchAsync(List<string> textsToTranslate, string fromLanguage, string toLanguage, string promptTemplate, CancellationToken cancellationToken)
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

        public Task<int> CountTokensAsync(string textToCount, CancellationToken cancellationToken)
            {
            throw new NotSupportedException("当前OpenAI集成不支持计算Token。");
            }

        public Task PerformPreflightCheckAsync(CancellationToken cancellationToken)
            {
            // 执行一个超轻量级的“空”翻译请求来探测网络。
            // 我们不关心结果，只关心它是否会因为网络问题而抛出异常。
            // 使用一个极短的超时来确保快速失败。
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                cts.CancelAfter(TimeSpan.FromSeconds(15));
                // 注意：这里我们传入了空字符串作为翻译内容，并且promptTemplate也为空
                return TranslateAsync(string.Empty, "auto", "en", string.Empty, cts.Token);
                }
            }
        #endregion
        }
    }