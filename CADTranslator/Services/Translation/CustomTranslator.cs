// 文件路径: CADTranslator/Services/Translation/CustomTranslator.cs
// 【完整文件替换】

using CADTranslator.Models;
using CADTranslator.Models.API;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading; // ◄◄◄ 【新增】引入 CancellationToken
using System.Threading.Tasks;

namespace CADTranslator.Services.Translation
    {
    public class CustomTranslator : ITranslator
        {
        #region --- 字段 ---

        private readonly Lazy<HttpClient> _lazyHttpClient;
        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _model;

        #endregion

        #region --- 构造函数 ---
        public CustomTranslator(string apiEndpoint, string apiKey, string model)
            {
            if (string.IsNullOrWhiteSpace(apiEndpoint))
                throw new ApiException(ApiErrorType.ConfigurationError, ApiServiceType.Custom, "自定义API终结点 (URL) 不能为空。");

            _endpoint = apiEndpoint.TrimEnd('/');
            _apiKey = apiKey;
            _model = model;

            // 【修改】更新 HttpClient 的初始化逻辑
            _lazyHttpClient = new Lazy<HttpClient>(() =>
            {
                var client = new HttpClient { Timeout = TimeSpan.FromSeconds(180) };
                if (!string.IsNullOrWhiteSpace(_apiKey))
                    {
                    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
                    }
                return client;
            });
            }
        #endregion

        #region --- 1. 身份标识 (ITranslator 实现) ---

        public ApiServiceType ServiceType => ApiServiceType.Custom;
        public string DisplayName => "自定义接口";
        public string ApiDocumentationUrl => "";

        #endregion

        #region --- 2. 能力声明 (ITranslator 实现) ---

        public bool IsApiKeyRequired => true;
        public bool IsUserIdRequired => false;
        public bool IsApiUrlRequired => true;
        public bool IsModelRequired => true;
        public bool IsPromptSupported => true;
        public bool IsModelFetchingSupported => false;
        public bool IsBalanceCheckSupported => false;
        public bool IsTokenCountSupported => false;
        public bool IsBatchTranslationSupported => false;
        public bool IsLocalTokenCountSupported => true;
        public BillingUnit UnitType => BillingUnit.Token;
        #endregion

        #region --- 3. 核心与扩展功能 (ITranslator 实现) ---

        public async Task<(string TranslatedText, TranslationUsage Usage)> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage, string promptTemplate, CancellationToken cancellationToken)
            {
            if (string.IsNullOrWhiteSpace(_model))
                throw new ApiException(ApiErrorType.ConfigurationError, ServiceType, "模型名称不能为空。");

            try
                {
                var finalPrompt = promptTemplate
                    .Replace("{fromLanguage}", fromLanguage)
                    .Replace("{toLanguage}", toLanguage);

                var requestData = new
                    {
                    model = _model,
                    messages = new[]
                    {
                new { role = "system", content = finalPrompt },
                new { role = "user", content = textToTranslate }
            },
                    stream = false
                    };

                string jsonPayload = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                string requestUrl = $"{_endpoint}/chat/completions";

                using (var request = new HttpRequestMessage(HttpMethod.Post, requestUrl) { Content = content })
                    {
                    HttpResponseMessage response;
                    using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                        {
                        connectCts.CancelAfter(TimeSpan.FromSeconds(15));
                        try
                            {
                            response = await _lazyHttpClient.Value.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, connectCts.Token);
                            }
                        catch (OperationCanceledException)
                            {
                            if (cancellationToken.IsCancellationRequested) throw;
                            throw new ApiException(ApiErrorType.NetworkError, ServiceType, "连接超时 (超过15秒)，请检查网络或代理设置。");
                            }
                        }

                    if (!response.IsSuccessStatusCode)
                        {
                        await HandleApiError(response);
                        }

                    string jsonResponse = await response.Content.ReadAsStringAsync();

                    try
                        {
                        var data = JObject.Parse(jsonResponse);
                        var translatedText = data["choices"]?[0]?["message"]?["content"]?.ToString();

                        if (translatedText == null)
                            throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, "API响应中缺少有效的'content'字段。");

                        return (translatedText.Trim(), null);
                        }
                    catch (JsonException ex)
                        {
                        throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, $"无法解析API返回的成功响应: {ex.Message}");
                        }
                    }
                }
            catch (TaskCanceledException)
                {
                if (cancellationToken.IsCancellationRequested) throw;
                throw new ApiException(ApiErrorType.NetworkError, ServiceType, "请求被取消或总超时 (超过180秒)。");
                }
            catch (HttpRequestException ex)
                {
                throw new ApiException(ApiErrorType.NetworkError, ServiceType, $"网络请求失败: {ex.Message}");
                }
            catch (Exception ex)
                {
                throw new ApiException(ApiErrorType.Unknown, ServiceType, $"调用自定义API({_endpoint})时发生未知异常: {ex.Message.Replace('\t', ' ')}");
                }
            }

        public Task<(List<string> TranslatedTexts, TranslationUsage Usage)> TranslateBatchAsync(List<string> textsToTranslate, string fromLanguage, string toLanguage, string promptTemplate, CancellationToken cancellationToken)
            {
            throw new NotSupportedException("自定义接口服务不支持批量翻译。");
            }

        public Task<List<string>> GetModelsAsync(CancellationToken cancellationToken)
            {
            throw new NotSupportedException("自定义接口服务不支持在线获取模型列表。请在模型管理中手动添加。");
            }

        public Task<List<KeyValuePair<string, string>>> CheckBalanceAsync()
            {
            throw new NotSupportedException("自定义接口服务不支持在线查询余额。");
            }

        public Task<int> CountTokensAsync(string textToCount, CancellationToken cancellationToken)
            {
            throw new NotSupportedException("自定义接口服务不支持计算Token。");
            }

        public Task PerformPreflightCheckAsync(CancellationToken cancellationToken)
            {
            // 对于自定义接口，同样使用发送“空”请求的策略来探测网络。
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                cts.CancelAfter(TimeSpan.FromSeconds(15));
                return TranslateAsync(string.Empty, "auto", "en", string.Empty, cts.Token);
                }
            }

        #endregion

        #region --- 私有辅助方法 ---

        private async Task HandleApiError(HttpResponseMessage response)
            {
            string errorBody = await response.Content.ReadAsStringAsync();
            string errorMessage = errorBody;
            string apiErrorCode = null;

            try
                {
                var errorObj = JObject.Parse(errorBody);
                errorMessage = errorObj["message"]?.ToString() ?? errorBody;
                apiErrorCode = errorObj["code"]?.ToString() ?? errorObj["error"]?["code"]?.ToString();
                }
            catch (JsonException)
                {
                errorMessage = errorBody;
                }

            throw new ApiException(ApiErrorType.ApiError, ServiceType, errorMessage, response.StatusCode, apiErrorCode);
            }

        #endregion
        }
    }