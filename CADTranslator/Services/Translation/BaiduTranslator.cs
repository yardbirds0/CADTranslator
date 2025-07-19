// 文件路径: CADTranslator/Services/Translation/BaiduTranslator.cs
// 【完整文件替换】

using CADTranslator.Models;
using CADTranslator.Models.API;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CADTranslator.Services.Translation
    {
    public class BaiduTranslator : ITranslator
        {
        #region --- 字段 ---

        private readonly string _appId;
        private readonly string _appKey;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        #endregion

        #region --- 构造函数 ---

        public BaiduTranslator(string appId, string appKey)
            {
            // 在这里我们不检查AppID和AppKey是否为空，因为您有提供默认值。
            // 真正的配置检查将在调用时进行，如果用户提供了空值则会失败。
            _appId = appId;
            _appKey = appKey;
            }

        #endregion

        #region --- 1. 身份标识 (ITranslator 实现) ---

        public ApiServiceType ServiceType => ApiServiceType.Baidu;
        public string DisplayName => "百度翻译";
        public string ApiDocumentationUrl => "https://fanyi-api.baidu.com/doc/21";

        #endregion

        #region --- 2. 能力声明 (ITranslator 实现) ---

        public bool IsApiKeyRequired => true;
        public bool IsUserIdRequired => true;
        public bool IsApiUrlRequired => false;
        public bool IsModelRequired => false;
        public bool IsPromptSupported => false;
        public bool IsModelFetchingSupported => false;
        public bool IsBalanceCheckSupported => false;

        #endregion

        #region --- 3. 核心与扩展功能 (ITranslator 实现) ---

        public async Task<string> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage, CancellationToken cancellationToken) // ◄◄◄ 【新增】cancellationToken 参数
            {
            // 1. 配置检查
            if (string.IsNullOrWhiteSpace(_appId) || string.IsNullOrWhiteSpace(_appKey))
                {
                throw new ApiException(ApiErrorType.ConfigurationError, ServiceType, "App ID 或 App Key 不能为空。");
                }
            if (string.IsNullOrWhiteSpace(textToTranslate))
                {
                return "";
                }

            try
                {
                string queryText = textToTranslate.Replace('\n', ' ');
                var random = new Random();
                string salt = random.Next(32768, 65536).ToString();
                string sign = GenerateSign(queryText, salt);
                string baseUrl = "http://api.fanyi.baidu.com/api/trans/vip/translate";

                var queryParams = new Dictionary<string, string>
                {
                    { "q", queryText },
                    { "from", fromLanguage },
                    { "to", toLanguage },
                    { "appid", _appId },
                    { "salt", salt },
                    { "sign", sign }
                };

                string queryString = string.Join("&", queryParams.Select(kvp =>
                    $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));

                string fullUrl = $"{baseUrl}?{queryString}";

                // 2. 发起API请求并处理响应
                // ▼▼▼ 【核心修改】将 cancellationToken 传递给 GetAsync 方法 ▼▼▼
                var response = await _httpClient.GetAsync(fullUrl, cancellationToken);

                // 当任务被取消时，上面的调用会抛出 TaskCanceledException，并被下面的catch块捕获
                response.EnsureSuccessStatusCode();

                string jsonResponse = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<BaiduTranslationResult>(jsonResponse);

                // 3. 处理API返回的业务错误
                if (!string.IsNullOrEmpty(result?.ErrorCode))
                    {
                    string friendlyMessage = $"错误码: {result.ErrorCode}, {result.ErrorMessage?.Replace('\t', ' ')}";
                    throw new ApiException(ApiErrorType.ApiError, ServiceType, friendlyMessage, response.StatusCode, result.ErrorCode);
                    }

                // 4. 处理成功的响应
                if (result?.TransResult != null && result.TransResult.Any())
                    {
                    var translatedText = new StringBuilder();
                    foreach (var part in result.TransResult)
                        {
                        translatedText.Append(part.Dst);
                        }
                    return translatedText.ToString();
                    }

                // 5. 处理未知的成功响应格式
                throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, "API未返回有效或可解析的结果。");

                }
            // 6. 捕获网络层面的异常
            catch (TaskCanceledException) // ◄◄◄ 这个catch块现在也会捕获由cancellationToken引发的取消
                {
                // 检查是不是由我们的令牌主动取消的
                if (cancellationToken.IsCancellationRequested)
                    {
                    throw; // 如果是，直接重新抛出，让ViewModel知道是主动取消
                    }
                // 否则，认为是普通的网络超时
                throw new ApiException(ApiErrorType.NetworkError, ServiceType, "请求超时。请检查您的网络连接或VPN设置。");
                }
            catch (HttpRequestException ex)
                {
                throw new ApiException(ApiErrorType.NetworkError, ServiceType, $"网络请求失败: {ex.Message}");
                }
            catch (Exception ex)
                {
                throw new ApiException(ApiErrorType.Unknown, ServiceType, $"调用百度翻译时发生未知错误: {ex.Message.Replace('\t', ' ')}");
                }
            }

        public Task<List<string>> GetModelsAsync()
            {
            // 因为不支持，所以抛出异常
            throw new NotSupportedException("百度翻译服务不支持获取模型列表。");
            }

        public Task<List<KeyValuePair<string, string>>> CheckBalanceAsync()
            {
            // 因为不支持，所以抛出异常
            throw new NotSupportedException("百度翻译服务不支持查询余额。");
            }

        #endregion

        #region --- 私有辅助方法 ---

        private string GenerateSign(string query, string salt)
            {
            string str = _appId + query + salt + _appKey;
            using (MD5 md5 = MD5.Create())
                {
                byte[] inputBytes = Encoding.UTF8.GetBytes(str);
                byte[] hashBytes = md5.ComputeHash(inputBytes);

                var sb = new StringBuilder();
                for (int i = 0; i < hashBytes.Length; i++)
                    {
                    sb.Append(hashBytes[i].ToString("x2"));
                    }
                return sb.ToString();
                }
            }

        private class BaiduTranslationResult
            {
            [JsonProperty("from")]
            public string From { get; set; }
            [JsonProperty("to")]
            public string To { get; set; }
            [JsonProperty("trans_result")]
            public List<TranslationPair> TransResult { get; set; }
            [JsonProperty("error_code")]
            public string ErrorCode { get; set; }
            [JsonProperty("error_msg")]
            public string ErrorMessage { get; set; }
            }

        private class TranslationPair
            {
            [JsonProperty("src")]
            public string Src { get; set; }
            [JsonProperty("dst")]
            public string Dst { get; set; }
            }

        #endregion
        }
    }