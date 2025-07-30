// 文件路径: CADTranslator/Services/Translation/GeminiTranslator.cs
// 【完整文件替换】

using CADTranslator.Models;
using CADTranslator.Models.API;
using Mscc.GenerativeAI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq; // ◄◄◄ 【新增】引入库
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions; // ◄◄◄ 【新增】引入库
using System.Threading;
using System.Threading.Tasks;

namespace CADTranslator.Services.Translation
    {
    public class GeminiTranslator : ITranslator
        {
        #region --- 内部数据模型 ---

        // 用于反序列化 UsageMetadata 字段
        private class GeminiUsageMetadata
            {
            [JsonProperty("promptTokenCount")]
            public long PromptTokenCount { get; set; }
            [JsonProperty("candidatesTokenCount")]
            public long CandidatesTokenCount { get; set; }
            [JsonProperty("totalTokenCount")]
            public long TotalTokenCount { get; set; }
            }

        #endregion

        #region --- 字段 ---
        private readonly Lazy<IGenerativeAI> _lazyGoogleAI;
        private readonly string _model;
        private readonly string _apiKey;
        #endregion

        #region --- Gemini 错误码字典 ---

        /// <summary>
        /// 存储 Google gRPC 官方错误码及其对应的用户友好中文提示。
        /// </summary>
        private static readonly Dictionary<string, string> GeminiErrorMessages = new Dictionary<string, string>
        {
            { "OK", "成功" },
            { "CANCELLED", "操作已被调用者取消。" },
            { "UNKNOWN", "发生未知服务端错误。" },
            { "INVALID_ARGUMENT", "请求中包含了无效的参数。" },
            { "DEADLINE_EXCEEDED", "操作在截止日期前未能完成，请求超时。" },
            { "NOT_FOUND", "请求的资源未找到 (例如，指定的模型不存在)。" },
            { "ALREADY_EXISTS", "您尝试创建的实体已存在。" },
            { "PERMISSION_DENIED", "权限不足，请检查您的API密钥是否有效或具有相应权限。" },
            { "UNAUTHENTICATED", "请求缺乏有效的身份验证凭据。" },
            { "RESOURCE_EXHAUSTED", "资源配额已耗尽，请检查您的账户用量或升级计划。" },
            { "FAILED_PRECONDITION", "系统状态不满足执行操作的前提 (例如：用户所在区域不支持此API)。" },
            { "ABORTED", "操作已中止，通常是由于并发冲突。" },
            { "OUT_OF_RANGE", "操作超出了有效范围 (例如：读取文件末尾之后的内容)。" },
            { "UNIMPLEMENTED", "服务器不支持或未实现该操作。" },
            { "INTERNAL", "发生内部服务器严重错误，请稍后重试。" },
            { "UNAVAILABLE", "服务当前不可用，通常是临时状况，请稍后重试。" },
            { "DATA_LOSS", "发生了不可恢复的数据丢失或损坏。" }
        };

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

        #region --- 身份与能力声明 ---
        public ApiServiceType ServiceType => ApiServiceType.Gemini;
        public string DisplayName => "谷歌Gemini";
        public string ApiDocumentationUrl => "https://ai.google.dev/docs";
        public bool IsApiKeyRequired => true;
        public bool IsUserIdRequired => false;
        public bool IsApiUrlRequired => false;
        public bool IsModelRequired => true;
        public bool IsPromptSupported => true;
        public bool IsModelFetchingSupported => true;
        public bool IsBalanceCheckSupported => false;
        public bool IsTokenCountSupported => true;
        public bool IsLocalTokenCountSupported => false;
        public bool IsBatchTranslationSupported => true;
        public BillingUnit UnitType => BillingUnit.Token;
        #endregion

        #region --- 核心与扩展功能 ---

        public async Task<(string TranslatedText, TranslationUsage Usage)> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage, string promptTemplate, CancellationToken cancellationToken)
            {
            if (string.IsNullOrWhiteSpace(_model))
                throw new ApiException(ApiErrorType.ConfigurationError, ServiceType, "模型名称不能为空。");

            cancellationToken.ThrowIfCancellationRequested();

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                try
                    {
                    cts.CancelAfter(TimeSpan.FromSeconds(15));
                    var generativeModel = _lazyGoogleAI.Value.GenerativeModel(model: _model);
                    // ▼▼▼ 【核心修改】使用传入的 promptTemplate 来构建最终的 prompt ▼▼▼
                    string prompt = promptTemplate
                        .Replace("{fromLanguage}", fromLanguage)
                        .Replace("{toLanguage}", toLanguage)
                        + $"\n\nText to translate:\n---\n{textToTranslate}\n---";
                    var translationTask = generativeModel.GenerateContent(prompt, cancellationToken: cancellationToken);
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
                    var completedTask = await Task.WhenAny(translationTask, timeoutTask);

                    if (completedTask == timeoutTask)
                        {
                        throw new ApiException(ApiErrorType.NetworkError, ServiceType, "请求超时 (超过20秒)。");
                        }

                    var response = await generativeModel.GenerateContent(prompt, cancellationToken: cts.Token);

                    if (response?.Text == null)
                        {
                        throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, "API返回了空或无效的响应内容。");
                        }

                    // 解析UsageMetadata并创建标准化的TranslationUsage对象
                    TranslationUsage usage = null;
                    if (response.UsageMetadata != null)
                        {
                        usage = new TranslationUsage
                            {
                            PromptTokens = response.UsageMetadata.PromptTokenCount,
                            CompletionTokens = response.UsageMetadata.CandidatesTokenCount,
                            TotalTokens = response.UsageMetadata.TotalTokenCount
                            };
                        }

                    return (response.Text.Trim(), usage);
                    }
                catch (TaskCanceledException)
                    {
                    if (cancellationToken.IsCancellationRequested) throw;
                    throw new ApiException(ApiErrorType.NetworkError, ServiceType, "连接超时 (超过15秒)，请检查网络或代理设置。");
                    }
                catch (Exception ex)
                    {
                    throw ParseAndCreateApiException(ex, cancellationToken);
                    }
                }
            }

        public async Task<(List<string> TranslatedTexts, TranslationUsage Usage)> TranslateBatchAsync(List<string> textsToTranslate, string fromLanguage, string toLanguage, string promptTemplate, CancellationToken cancellationToken)
            {
            // 【修改】添加手动连接超时逻辑，与 TranslateAsync 完全相同
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                try
                    {
                    cts.CancelAfter(TimeSpan.FromSeconds(15));
                    if (string.IsNullOrWhiteSpace(_model))
                        throw new ApiException(ApiErrorType.ConfigurationError, ServiceType, "模型名称不能为空。");
                    if (textsToTranslate == null || !textsToTranslate.Any())
                        return (new List<string>(), null);

                    cancellationToken.ThrowIfCancellationRequested();

                    try
                        {
                        string textsAsJsonArray = JsonConvert.SerializeObject(textsToTranslate);
                        string prompt = promptTemplate
                            .Replace("{fromLanguage}", fromLanguage)
                            .Replace("{toLanguage}", toLanguage)
                            .Replace(" just return the translated text.", " Your response MUST be a valid JSON array of strings, with each string being the translation of the corresponding string in the input array. Maintain the same order. Do not add any extra explanations or content outside of the JSON array.")
                             + $"\n\nInput JSON array:\n---\n{textsAsJsonArray}\n---";
                        var generativeModel = _lazyGoogleAI.Value.GenerativeModel(model: _model);
                        var generationConfig = new GenerationConfig() { ResponseMimeType = "application/json", };
                        var translationTask = generativeModel.GenerateContent(prompt, generationConfig, cancellationToken: cancellationToken);
                        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
                        var completedTask = await Task.WhenAny(translationTask, timeoutTask);
                        if (completedTask == timeoutTask)
                            {
                            throw new ApiException(ApiErrorType.NetworkError, ServiceType, "批量翻译请求超时 (超过20秒)。");
                            }
                        var response = await generativeModel.GenerateContent(prompt, generationConfig, cancellationToken: cts.Token);
                        var responseContent = response?.Text;
                        if (string.IsNullOrWhiteSpace(responseContent))
                            throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, "API响应中缺少有效的'content'字段。");
                        try
                            {
                            var translatedList = JsonConvert.DeserializeObject<List<string>>(responseContent);
                            if (translatedList == null || translatedList.Count != textsToTranslate.Count)
                                throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, $"API返回的翻译结果数量 ({translatedList?.Count ?? 0}) 与原文数量 ({textsToTranslate.Count}) 不匹配。");

                            TranslationUsage usage = null;
                            if (response.UsageMetadata != null)
                                {
                                usage = new TranslationUsage
                                    {
                                    PromptTokens = response.UsageMetadata.PromptTokenCount,
                                    CompletionTokens = response.UsageMetadata.CandidatesTokenCount,
                                    TotalTokens = response.UsageMetadata.TotalTokenCount
                                    };
                                }

                            return (translatedList, usage);
                            }
                        catch (JsonException ex)
                            {
                            throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, $"无法将API返回的内容解析为JSON数组: {ex.Message}");
                            }
                        }
                    catch (TaskCanceledException)
                        {
                        if (cancellationToken.IsCancellationRequested) throw;
                        throw new ApiException(ApiErrorType.NetworkError, ServiceType, "连接超时 (超过15秒)，请检查网络或代理设置。");
                        }
                    catch (Exception ex)
                        {
                        throw ParseAndCreateApiException(ex, cancellationToken);
                        }
                    }
                catch (TaskCanceledException)
                    {
                    if (cancellationToken.IsCancellationRequested) throw;
                    throw new ApiException(ApiErrorType.NetworkError, ServiceType, "连接超时 (超过15秒)，请检查网络或代理设置。");
                    }
                catch (Exception ex)
                    {
                    throw ParseAndCreateApiException(ex, cancellationToken);
                    }
                }
            }

        public async Task<List<string>> GetModelsAsync(CancellationToken cancellationToken)
            {
            // 【修改】添加手动连接超时逻辑，与 TranslateAsync 完全相同
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                try
                    {
                    cts.CancelAfter(TimeSpan.FromSeconds(15));
                    var generativeModel = _lazyGoogleAI.Value.GenerativeModel();
                    var models = await generativeModel.ListModels(cancellationToken: cts.Token);
                    if (models == null || !models.Any()) return new List<string>();
                    return models
                           .Where(m => m.SupportedGenerationMethods.Contains("generateContent"))
                           .Select(m => m.Name.Replace("models/", ""))
                           .ToList();
                    }
                catch (TaskCanceledException) { throw; }
                catch (Exception ex)
                    {
                    throw ParseAndCreateApiException(ex, cancellationToken);
                    }
                }
            }

        public Task<List<KeyValuePair<string, string>>> CheckBalanceAsync()
            {
            throw new NotSupportedException("Gemini API 服务不支持在线查询余额。");
            }

        public async Task<int> CountTokensAsync(string textToCount, CancellationToken cancellationToken)
            {
            // 【修改】添加手动连接超时逻辑，与 TranslateAsync 完全相同
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                try
                    {
                    cts.CancelAfter(TimeSpan.FromSeconds(15));
                    var generativeModel = _lazyGoogleAI.Value.GenerativeModel(model: _model);
                    var response = await generativeModel.CountTokens(textToCount, cancellationToken: cts.Token);
                    return response.TotalTokens;
                    }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                    {
                    throw ParseAndCreateApiException(ex, cancellationToken);
                    }
                }
            }
        #endregion

        #region --- 私有辅助方法 ---
        private ApiException ParseAndCreateApiException(Exception ex, CancellationToken cancellationToken)
            {
            if (cancellationToken.IsCancellationRequested)
                {
                return new ApiException(ApiErrorType.Unknown, ServiceType, "操作已取消。");
                }

            var match = Regex.Match(ex.Message, @"{\s*""error"":\s*{[^}]+}\s*}", RegexOptions.Singleline);
            if (match.Success)
                {
                try
                    {
                    var errorJson = JObject.Parse(match.Value);
                    var status = errorJson["error"]?["status"]?.ToString();
                    var message = errorJson["error"]?["message"]?.ToString();

                    if (!string.IsNullOrEmpty(status) && GeminiErrorMessages.TryGetValue(status, out var friendlyMessage))
                        {
                        return new ApiException(ApiErrorType.ApiError, ServiceType, $"[{status}] {friendlyMessage} (详情: {message})", null, status);
                        }
                    else if (!string.IsNullOrEmpty(message))
                        {
                        return new ApiException(ApiErrorType.ApiError, ServiceType, message, null, status);
                        }
                    }
                catch (JsonException) { }
                }

            if (ex.Message.Contains("Request timed out") || ex.Message.Contains("NameResolutionFailure"))
                {
                return new ApiException(ApiErrorType.NetworkError, ServiceType, $"网络请求失败: {ex.Message}");
                }

            return new ApiException(ApiErrorType.ApiError, ServiceType, ex.Message);
            }
        #endregion
        }
    }