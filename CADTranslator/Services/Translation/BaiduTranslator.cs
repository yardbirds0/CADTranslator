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
            _appId = appId;
            _appKey = appKey;
            }

        #endregion

        #region --- 1. 身份标识 (ITranslator 实现) ---

        public ApiServiceType ServiceType => ApiServiceType.Baidu;
        public string DisplayName => "百度翻译";
        public string ApiDocumentationUrl => "https://api.fanyi.baidu.com/api/trans/product/desktop";

        #endregion

        #region --- 2. 能力声明 (ITranslator 实现) ---

        public bool IsApiKeyRequired => true;
        public bool IsUserIdRequired => true;
        public bool IsApiUrlRequired => false;
        public bool IsModelRequired => false;
        public bool IsPromptSupported => false;
        public bool IsModelFetchingSupported => false;
        public bool IsBalanceCheckSupported => false;
        public bool IsTokenCountSupported => false;
        public bool IsBatchTranslationSupported => false; // 【新增】明确表示不支持批量翻译

        #endregion

        #region --- 3. 核心与扩展功能 (ITranslator 实现) ---

        public async Task<string> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage, CancellationToken cancellationToken)
            {
            // (此方法代码保持不变)
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

                var response = await _httpClient.GetAsync(fullUrl, cancellationToken);

                response.EnsureSuccessStatusCode();

                string jsonResponse = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<BaiduTranslationResult>(jsonResponse);

                if (!string.IsNullOrEmpty(result?.ErrorCode))
                    {
                    string friendlyMessage = $"错误码: {result.ErrorCode}, {result.ErrorMessage?.Replace('\t', ' ')}";
                    throw new ApiException(ApiErrorType.ApiError, ServiceType, friendlyMessage, response.StatusCode, result.ErrorCode);
                    }

                if (result?.TransResult != null && result.TransResult.Any())
                    {
                    var translatedText = new StringBuilder();
                    foreach (var part in result.TransResult)
                        {
                        translatedText.Append(part.Dst);
                        }
                    return translatedText.ToString();
                    }

                throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, "API未返回有效或可解析的结果。");
                }
            catch (TaskCanceledException)
                {
                if (cancellationToken.IsCancellationRequested)
                    {
                    throw;
                    }
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

        // 【新增】实现新的批量翻译方法
        public Task<List<string>> TranslateBatchAsync(List<string> textsToTranslate, string fromLanguage, string toLanguage, CancellationToken cancellationToken)
            {
            // 因为不支持，所以直接抛出 NotSupportedException 异常
            throw new NotSupportedException("百度翻译服务不支持批量翻译。");
            }


        public Task<List<string>> GetModelsAsync(CancellationToken cancellationToken)
            {
            throw new NotSupportedException("百度翻译服务不支持获取模型列表。");
            }

        public Task<List<KeyValuePair<string, string>>> CheckBalanceAsync()
            {
            throw new NotSupportedException("百度翻译服务不支持查询余额。");
            }

        public Task<int> CountTokensAsync(string textToCount)
            {
            throw new NotSupportedException("百度翻译服务不支持计算Token。");
            }

        #endregion

        #region --- 私有辅助方法 ---

        // (这部分代码保持不变)
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