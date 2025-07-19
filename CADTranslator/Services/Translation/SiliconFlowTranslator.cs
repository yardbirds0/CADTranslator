// 文件路径: CADTranslator/Services/Translation/SiliconFlowTranslator.cs
// 【完整文件替换】

using CADTranslator.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading; // ◄◄◄ 【新增】引入 CancellationToken
using System.Threading.Tasks;

namespace CADTranslator.Services
    {
    public class SiliconFlowTranslator : ITranslator
        {
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

        #region --- 1. 身份标识 (ITranslator 实现) ---

        public ApiServiceType ServiceType => ApiServiceType.SiliconFlow;
        public string DisplayName => "硅基流动";
        public string ApiDocumentationUrl => "https://www.siliconflow.cn/zh-cn/doc";

        #endregion

        #region --- 2. 能力声明 (ITranslator 实现) ---

        public bool IsApiKeyRequired => true;
        public bool IsUserIdRequired => false;
        public bool IsApiUrlRequired => true;
        public bool IsModelRequired => true;
        public bool IsPromptSupported => true;
        public bool IsModelFetchingSupported => true;
        public bool IsBalanceCheckSupported => true;

        #endregion

        #region --- 3. 核心与扩展功能 (ITranslator 实现) ---

        // ▼▼▼ 【方法重写】重写整个 TranslateAsync 方法以支持 CancellationToken ▼▼▼
        public async Task<string> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage, CancellationToken cancellationToken)
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
                string requestUrl = $"{_endpoint}/chat/completions";

                // 【核心修改】将 cancellationToken 传递给 PostAsync
                var response = await _lazyHttpClient.Value.PostAsync(requestUrl, content, cancellationToken);

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

                    return translatedText.Trim();
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

        public async Task<List<string>> GetModelsAsync()
            {
            const string modelListUrl = "https://api.siliconflow.cn/v1/models";
            try
                {
                // 【核心修改】为 GetAsync 也传递一个 CancellationToken.None，保持一致性
                var response = await _lazyHttpClient.Value.GetAsync(modelListUrl, CancellationToken.None);
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
                // 【核心修改】为 GetAsync 也传递一个 CancellationToken.None
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