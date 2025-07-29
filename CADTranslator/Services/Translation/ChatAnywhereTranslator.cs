// 文件路径: CADTranslator/Services/Translation/ChatAnywhereTranslator.cs
// 【这是一个新文件】

using CADTranslator.Models;
using CADTranslator.Models.API;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CADTranslator.Services.Translation
    {
    public class ChatAnywhereTranslator : ITranslator
        {
        #region --- 内部数据模型 ---

        // 用于反序列化 usage 字段
        private class ChatAnywhereUsage
            {
            [JsonProperty("prompt_tokens")]
            public long PromptTokens { get; set; }
            [JsonProperty("completion_tokens")]
            public long CompletionTokens { get; set; }
            [JsonProperty("total_tokens")]
            public long TotalTokens { get; set; }
            }

        // 用于反序列化完整的API响应
        private class ChatAnywhereResponse
            {
            [JsonProperty("choices")]
            public List<Choice> Choices { get; set; }
            [JsonProperty("usage")]
            public ChatAnywhereUsage Usage { get; set; }
            }
        private class Choice
            {
            [JsonProperty("message")]
            public Message Message { get; set; }
            }
        private class Message
            {
            [JsonProperty("content")]
            public string Content { get; set; }
            }

        #endregion

        #region --- 字段 ---

        private readonly Lazy<HttpClient> _lazyHttpClient;
        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _model;

        #endregion

        #region --- 构造函数 ---

        public ChatAnywhereTranslator(string apiEndpoint, string apiKey, string model)
            {
            if (string.IsNullOrWhiteSpace(apiEndpoint))
                throw new ApiException(ApiErrorType.ConfigurationError, ApiServiceType.ChatAnywhere, "API URL (终结点) 不能为空。");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ApiException(ApiErrorType.ConfigurationError, ApiServiceType.ChatAnywhere, "API 密钥不能为空。");

            _endpoint = apiEndpoint.TrimEnd('/');
            _apiKey = apiKey;
            _model = model;

            _lazyHttpClient = new Lazy<HttpClient>(() =>
            {
                var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
                return client;
            });
            }

        #endregion

        #region --- 1. 身份标识 (ITranslator 实现) ---

        public ApiServiceType ServiceType => ApiServiceType.ChatAnywhere;
        public string DisplayName => "ChatAnywhere";
        public string ApiDocumentationUrl => "https://api.chatanywhere.tech";
        #endregion

        #region --- 2. 能力声明 (ITranslator 实现) ---

        public bool IsApiKeyRequired => true;
        public bool IsUserIdRequired => false;
        public bool IsApiUrlRequired => true;
        public bool IsModelRequired => true;
        public bool IsPromptSupported => true;
        public bool IsModelFetchingSupported => true;
        public bool IsBalanceCheckSupported => true;
        public bool IsTokenCountSupported => true;
        public bool IsLocalTokenCountSupported => true;
        public bool IsBatchTranslationSupported => false;
        public BillingUnit UnitType => BillingUnit.Token;
        #endregion

        #region --- 3. 核心与扩展功能 (ITranslator 实现) ---

        public async Task<(string TranslatedText, TranslationUsage Usage)> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage, CancellationToken cancellationToken)
            {
            if (string.IsNullOrWhiteSpace(_model))
                throw new ApiException(ApiErrorType.ConfigurationError, ServiceType, "模型名称不能为空。");

            try
                {
                var requestData = new
                    {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "system", content = $"你是一个专业的结构专业图纸翻译家。你的任务是把用户的文本从 {fromLanguage} 翻译成 {toLanguage}. 不要添加任何额外的解释，只返回翻译好的文本。遇到符号则保留原来的样式。" },
                        new { role = "user", content = textToTranslate }
                    },
                    stream = false
                    };

                string jsonPayload = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                string requestUrl = $"{_endpoint}/v1/chat/completions";

                var response = await _lazyHttpClient.Value.PostAsync(requestUrl, content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                    {
                    await HandleApiError(response);
                    }

                string jsonResponse = await response.Content.ReadAsStringAsync();
                try
                    {
                    // 反序列化为我们定义的完整响应模型
                    var data = JsonConvert.DeserializeObject<ChatAnywhereResponse>(jsonResponse);
                    var translatedText = data?.Choices?.FirstOrDefault()?.Message?.Content;

                    if (translatedText == null)
                        throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, "API响应中缺少有效的'content'字段。");

                    // 创建并填充我们标准化的 TranslationUsage 对象
                    TranslationUsage usage = null;
                    if (data.Usage != null)
                        {
                        usage = new TranslationUsage
                            {
                            PromptTokens = data.Usage.PromptTokens,
                            CompletionTokens = data.Usage.CompletionTokens,
                            TotalTokens = data.Usage.TotalTokens
                            };
                        }

                    return (translatedText.Trim(), usage);
                    }
                catch (JsonException ex)
                    {
                    throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, $"无法解析API返回的成功响应: {ex.Message}");
                    }
                }
            catch (TaskCanceledException)
                {
                if (cancellationToken.IsCancellationRequested) throw;
                throw new ApiException(ApiErrorType.NetworkError, ServiceType, "请求超时。请检查网络连接或VPN设置。");
                }
            catch (HttpRequestException ex)
                {
                throw new ApiException(ApiErrorType.NetworkError, ServiceType, $"网络请求失败: {ex.Message}");
                }
            }

        public Task<(List<string> TranslatedTexts, TranslationUsage Usage)> TranslateBatchAsync(List<string> textsToTranslate, string fromLanguage, string toLanguage, CancellationToken cancellationToken)
            {
            throw new NotSupportedException("ChatAnywhere 服务不支持批量翻译。");
            }
        public async Task<List<string>> GetModelsAsync(CancellationToken cancellationToken)
            {
            string modelListUrl = $"{_endpoint}/v1/models";
            try
                {
                var response = await _lazyHttpClient.Value.GetAsync(modelListUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                    {
                    await HandleApiError(response);
                    }
                string jsonResponse = await response.Content.ReadAsStringAsync();
                try
                    {
                    var responseObject = JObject.Parse(jsonResponse);
                    var modelsArray = responseObject["data"] as JArray;
                    if (modelsArray == null)
                        throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, "在模型列表API响应中未找到'data'数组。");
                    return modelsArray.Select(m => m["id"]?.ToString() ?? string.Empty).ToList();
                    }
                catch (JsonException ex)
                    {
                    throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, $"无法解析模型列表API的响应: {ex.Message}");
                    }
                }
            catch (TaskCanceledException)
                {
                if (cancellationToken.IsCancellationRequested) throw;
                throw new ApiException(ApiErrorType.NetworkError, ServiceType, "获取模型列表请求超时。");
                }
            catch (HttpRequestException ex)
                {
                throw new ApiException(ApiErrorType.NetworkError, ServiceType, $"获取模型列表时网络请求失败: {ex.Message}");
                }
            }

        public async Task<List<KeyValuePair<string, string>>> CheckBalanceAsync()
            {
            string usageUrl = $"{_endpoint}/v1/query/usage_details";
            try
                {
                // 【核心修正】为余额查询创建一个专用的、不带"Bearer"的HttpClient
                using (var usageClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
                    {
                    usageClient.DefaultRequestHeaders.Add("Authorization", _apiKey); // 直接使用API Key

                    var requestData = new
                        {
                        model = "%",
                        hours = 8760
                        };

                    string jsonPayload = JsonConvert.SerializeObject(requestData);
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    // 使用专用的usageClient发起请求
                    var response = await usageClient.PostAsync(usageUrl, content, CancellationToken.None);

                    if (!response.IsSuccessStatusCode)
                        {
                        await HandleApiError(response);
                        }

                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    try
                        {
                        var usageData = JArray.Parse(jsonResponse);
                        if (usageData == null)
                            throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, "在用量查询API响应中未找到有效数据。");

                        long totalPromptTokens = usageData.Sum(item => item["promptTokens"]?.Value<long>() ?? 0);
                        long totalCompletionTokens = usageData.Sum(item => item["completionTokens"]?.Value<long>() ?? 0);
                        long totalTokens = usageData.Sum(item => item["totalTokens"]?.Value<long>() ?? 0);
                        long totalRequests = usageData.Sum(item => item["count"]?.Value<long>() ?? 0);
                        double totalCost = usageData.Sum(item => item["cost"]?.Value<double>() ?? 0.0);

                        var balanceInfo = new List<KeyValuePair<string, string>>
                        {
                            new KeyValuePair<string, string>("查询范围", "过去1年"),
                            new KeyValuePair<string, string>("消耗费用(cost)", $"{totalCost:F4}"),
                            new KeyValuePair<string, string>("总请求数(count)", totalRequests.ToString()),
                            new KeyValuePair<string, string>("总Tokens(totalTokens)", totalTokens.ToString()),
                            new KeyValuePair<string, string>("输入Tokens(promptTokens)", totalPromptTokens.ToString()),
                            new KeyValuePair<string, string>("输出Tokens(completionTokens)", totalCompletionTokens.ToString())
                        };
                        return balanceInfo;
                        }
                    catch (JsonException ex)
                        {
                        throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, $"无法解析用量查询API的响应: {ex.Message}");
                        }
                    }
                }
            catch (TaskCanceledException)
                {
                throw new ApiException(ApiErrorType.NetworkError, ServiceType, "查询用量请求超时。");
                }
            catch (HttpRequestException ex)
                {
                throw new ApiException(ApiErrorType.NetworkError, ServiceType, $"查询用量时网络请求失败: {ex.Message}");
                }
            }

        public Task<int> CountTokensAsync(string textToCount, CancellationToken cancellationToken)
            {
            throw new NotSupportedException("ChatAnywhere 服务不支持计算Token。");
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
            catch (JsonException) { }

            switch (response.StatusCode)
                {
                case HttpStatusCode.Unauthorized:
                    errorMessage = "身份验证失败，请检查您的API密钥是否正确。";
                    break;
                case HttpStatusCode.NotFound:
                    errorMessage = "请求的API地址未找到，请检查API URL配置是否正确。";
                    break;
                case (HttpStatusCode)429:
                    errorMessage = "请求过于频繁，已触发速率限制，请稍后重试。";
                    break;
                }

            throw new ApiException(ApiErrorType.ApiError, ServiceType, errorMessage, response.StatusCode, apiErrorCode);
            }

        #endregion
        }
    }