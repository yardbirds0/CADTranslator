// 文件路径: CADTranslator/Services/Translation/SiliconFlowTranslator.cs
// 【完整文件替换】

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
    public class SiliconFlowTranslator : ITranslator
        {
        #region --- 内部数据模型 ---

        private class SiliconFlowUsage
            {
            [JsonProperty("prompt_tokens")]
            public long PromptTokens { get; set; }
            [JsonProperty("completion_tokens")]
            public long CompletionTokens { get; set; }
            [JsonProperty("total_tokens")]
            public long TotalTokens { get; set; }
            }

        private class SiliconFlowResponse
            {
            [JsonProperty("choices")]
            public List<Choice> Choices { get; set; }
            [JsonProperty("usage")]
            public SiliconFlowUsage Usage { get; set; }
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
        public SiliconFlowTranslator(string apiEndpoint, string apiKey, string model)
            {
            if (string.IsNullOrWhiteSpace(apiEndpoint))
                throw new ApiException(ApiErrorType.ConfigurationError, ApiServiceType.SiliconFlow, "API URL (终结点) 不能为空。");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ApiException(ApiErrorType.ConfigurationError, ApiServiceType.SiliconFlow, "API 密钥不能为空。");

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

        #region --- 身份与能力声明 ---
        public ApiServiceType ServiceType => ApiServiceType.SiliconFlow;
        public string DisplayName => "硅基流动";
        public string ApiDocumentationUrl => "https://www.siliconflow.cn/pricing";
        public bool IsApiKeyRequired => true;
        public bool IsUserIdRequired => false;
        public bool IsApiUrlRequired => true;
        public bool IsModelRequired => true;
        public bool IsPromptSupported => true;
        public bool IsModelFetchingSupported => true;
        public bool IsBalanceCheckSupported => true;
        public bool IsTokenCountSupported => true;
        public bool IsLocalTokenCountSupported => true;
        public bool IsBatchTranslationSupported => true;
        public BillingUnit UnitType => BillingUnit.Token;
        #endregion

        #region --- 核心与扩展功能 ---

        public async Task<(string TranslatedText, TranslationUsage Usage)> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage, CancellationToken cancellationToken)
            {
            // 单句翻译直接复用批量翻译逻辑，效率更高
            var (translatedTexts, usage) = await TranslateBatchAsync(new List<string> { textToTranslate }, fromLanguage, toLanguage, cancellationToken);
            return (translatedTexts.FirstOrDefault() ?? string.Empty, usage);
            }

        public async Task<(List<string> TranslatedTexts, TranslationUsage Usage)> TranslateBatchAsync(List<string> textsToTranslate, string fromLanguage, string toLanguage, CancellationToken cancellationToken)
            {
            if (string.IsNullOrWhiteSpace(_model))
                throw new ApiException(ApiErrorType.ConfigurationError, ServiceType, "模型名称不能为空。");
            if (textsToTranslate == null || !textsToTranslate.Any())
                return (new List<string>(), null);

            try
                {
                string textsAsJsonArray = JsonConvert.SerializeObject(textsToTranslate);
                string prompt = $"You are a professional translator for Civil Engineering drawings. Your task is to translate a JSON array of strings from {fromLanguage} to {toLanguage}. Your response MUST be a JSON array of strings, with each string being the translation of the corresponding string in the input array. Maintain the same order. Do not add any extra explanations or content outside of the JSON array.\n\nInput JSON array:\n---\n{textsAsJsonArray}\n---";

                var requestData = new
                    {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    },
                    response_format = new { type = "json_object" },
                    stream = false
                    };

                string jsonPayload = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                string requestUrl = $"{_endpoint}/chat/completions";

                var response = await _lazyHttpClient.Value.PostAsync(requestUrl, content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                    {
                    await HandleApiError(response);
                    }

                string jsonResponse = await response.Content.ReadAsStringAsync();
                try
                    {
                    var data = JsonConvert.DeserializeObject<SiliconFlowResponse>(jsonResponse);
                    var responseContent = data?.Choices?.FirstOrDefault()?.Message?.Content;

                    if (responseContent == null)
                        throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, "API响应中缺少有效的'content'字段。");

                    var cleanedContent = responseContent.Trim().Trim('`').Replace("json", "").Trim();
                    var translatedList = JsonConvert.DeserializeObject<List<string>>(cleanedContent);

                    if (translatedList == null || translatedList.Count != textsToTranslate.Count)
                        throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, $"API返回的翻译结果数量 ({translatedList?.Count ?? 0}) 与原文数量 ({textsToTranslate.Count}) 不匹配。");

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
                throw new ApiException(ApiErrorType.NetworkError, ServiceType, "请求超时。请检查网络连接或VPN设置。");
                }
            catch (HttpRequestException ex)
                {
                throw new ApiException(ApiErrorType.NetworkError, ServiceType, $"网络请求失败: {ex.Message}");
                }
            }

        public async Task<List<string>> GetModelsAsync(CancellationToken cancellationToken)
            {
            const string modelListUrl = "https://api.siliconflow.cn/v1/models";
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
            const string userInfoUrl = "https://api.siliconflow.cn/v1/user/info";
            try
                {
                var response = await _lazyHttpClient.Value.GetAsync(userInfoUrl, CancellationToken.None);
                if (!response.IsSuccessStatusCode)
                    {
                    await HandleApiError(response);
                    }
                string jsonResponse = await response.Content.ReadAsStringAsync();
                try
                    {
                    var userData = JObject.Parse(jsonResponse)["data"] as JObject;
                    if (userData == null)
                        throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, "在用户信息API响应中未找到'data'对象。");

                    var balanceInfo = new List<KeyValuePair<string, string>>();
                    foreach (var property in userData.Properties())
                        {
                        string value = property.Name == "totalBalance" ? $"{property.Value} ¥" : property.Value.ToString();
                        balanceInfo.Add(new KeyValuePair<string, string>(property.Name, value));
                        }
                    return balanceInfo;
                    }
                catch (JsonException ex)
                    {
                    throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, $"无法解析用户信息API的响应: {ex.Message}");
                    }
                }
            catch (TaskCanceledException)
                {
                throw new ApiException(ApiErrorType.NetworkError, ServiceType, "查询余额请求超时。");
                }
            catch (HttpRequestException ex)
                {
                throw new ApiException(ApiErrorType.NetworkError, ServiceType, $"查询余额时网络请求失败: {ex.Message}");
                }
            }

        public Task<int> CountTokensAsync(string textToCount, CancellationToken cancellationToken)
            {
            // 对于支持本地计算的接口，这个方法可以不再被ViewModel调用，但保留以符合接口规范
            throw new NotSupportedException("硅基流动服务应使用本地Token计算，不应调用此API方法。");
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
                apiErrorCode = errorObj["code"]?.ToString();
                }
            catch (JsonException)
                {
                errorMessage = errorBody;
                }

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