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
        #region --- 字段与错误码字典 ---
        private readonly string _appId;
        private readonly string _appKey;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        private static readonly Dictionary<string, string> BaiduErrorMessages = new Dictionary<string, string>
        {
            { "52001", "请求超时，请检查文本内容是否过长或语言设置是否正确。" },
            { "52002", "翻译系统错误，请稍后重试。" },
            { "52003", "用户未授权，请检查您的AppID是否正确或服务是否已开通。" },
            { "54000", "必填参数为空，请检查程序配置。" },
            { "54001", "签名错误，请检查您的AppID和密钥是否正确。" },
            { "54003", "访问频率受限，请降低调用频率或升级账户。" },
            { "54004", "账户余额不足，请前往百度翻译管理控制台充值。" },
            { "54005", "长文本请求频繁，请降低长文本的发送频率。" },
            { "58000", "客户端IP非法，请检查后台IP白名单设置，如为动态IP建议留空。" },
            { "58001", "译文语言方向不支持，请检查目标语言是否在语种列表里。" },
            { "58002", "服务当前已关闭，请前往百度翻译管理控制台开启服务。" },
            { "58003", "您的IP已被封禁，请勿在一天内使用多个AppID请求，次日将自动解封。" },
            { "90107", "认证未通过或未生效，请前往百度翻译官网查看认证进度。" },
            { "20003", "请求内容存在安全风险，请检查文本内容。" }
        };
        #endregion

        #region --- 构造函数 ---
        public BaiduTranslator(string appId, string appKey)
            {
            _appId = appId;
            _appKey = appKey;
            }
        #endregion

        #region --- 身份与能力声明 ---
        public ApiServiceType ServiceType => ApiServiceType.Baidu;
        public string DisplayName => "百度翻译";
        public string ApiDocumentationUrl => "https://fanyi-api.baidu.com/api/trans/product/desktop";
        public bool IsApiKeyRequired => true;
        public bool IsUserIdRequired => true;
        public bool IsApiUrlRequired => false;
        public bool IsModelRequired => false;
        public bool IsPromptSupported => false;
        public bool IsModelFetchingSupported => false;
        public bool IsBalanceCheckSupported => false;
        public bool IsTokenCountSupported => true;
        public bool IsLocalTokenCountSupported => false;
        public bool IsBatchTranslationSupported => true;
        public BillingUnit UnitType => BillingUnit.Character;
        #endregion

        #region --- 核心与扩展功能 ---

        public async Task<(string TranslatedText, TranslationUsage Usage)> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage, CancellationToken cancellationToken)
            {
            var (translatedTexts, usage) = await TranslateBatchAsync(new List<string> { textToTranslate }, fromLanguage, toLanguage, cancellationToken);
            return (translatedTexts.FirstOrDefault() ?? string.Empty, usage);
            }

        public async Task<(List<string> TranslatedTexts, TranslationUsage Usage)> TranslateBatchAsync(List<string> textsToTranslate, string fromLanguage, string toLanguage, CancellationToken cancellationToken)
            {
            if (string.IsNullOrWhiteSpace(_appId) || string.IsNullOrWhiteSpace(_appKey))
                throw new ApiException(ApiErrorType.ConfigurationError, ServiceType, "App ID 或 App Key 不能为空。");
            if (textsToTranslate == null || !textsToTranslate.Any())
                return (new List<string>(), null);

            try
                {
                string queryText = string.Join("\n", textsToTranslate);
                var random = new Random();
                string salt = random.Next(32768, 65536).ToString();
                string sign = GenerateSign(queryText, salt);
                string baseUrl = "http://api.fanyi.baidu.com/api/trans/vip/translate";

                var queryParams = new Dictionary<string, string>
                {
                    { "q", queryText }, { "from", fromLanguage }, { "to", toLanguage },
                    { "appid", _appId }, { "salt", salt }, { "sign", sign },
                    { "need_intervene", "1" }
                };

                string queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
                string fullUrl = $"{baseUrl}?{queryString}";

                var response = await _httpClient.GetAsync(fullUrl, cancellationToken);
                response.EnsureSuccessStatusCode();

                string jsonResponse = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<BaiduTranslationResult>(jsonResponse);

                if (!string.IsNullOrEmpty(result?.ErrorCode) && result.ErrorCode != "52000")
                    {
                    string friendlyMessage;
                    if (BaiduErrorMessages.TryGetValue(result.ErrorCode, out var message))
                        {
                        friendlyMessage = $"错误码: {result.ErrorCode} - {message}";
                        }
                    else
                        {
                        friendlyMessage = $"错误码: {result.ErrorCode}, {result.ErrorMessage?.Replace('\t', ' ')} (未知错误)";
                        }
                    throw new ApiException(ApiErrorType.ApiError, ServiceType, friendlyMessage, response.StatusCode, result.ErrorCode);
                    }

                if (result?.TransResult != null && result.TransResult.Any())
                    {
                    if (result.TransResult.Count != textsToTranslate.Count)
                        {
                        throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, $"API返回的翻译结果数量 ({result.TransResult.Count}) 与原文数量 ({textsToTranslate.Count}) 不匹配。");
                        }

                    var translatedList = result.TransResult.Select(p => p.Dst).ToList();
                    string allTranslatedText = string.Join("", translatedList);

                    var usage = new TranslationUsage
                        {
                        PromptTokens = queryText.Length,
                        CompletionTokens = allTranslatedText.Length,
                        TotalTokens = queryText.Length + allTranslatedText.Length
                        };

                    return (translatedList, usage);
                    }

                throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, "API未返回有效或可解析的结果。");
                }
            catch (TaskCanceledException)
                {
                if (cancellationToken.IsCancellationRequested) throw;
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

        public Task<List<string>> GetModelsAsync(CancellationToken cancellationToken)
            {
            throw new NotSupportedException("百度翻译服务不支持获取模型列表。");
            }

        public Task<List<KeyValuePair<string, string>>> CheckBalanceAsync()
            {
            throw new NotSupportedException("百度翻译服务不支持查询余额。");
            }

        public Task<int> CountTokensAsync(string textToCount, CancellationToken cancellationToken)
            {
            return Task.FromResult(string.IsNullOrEmpty(textToCount) ? 0 : textToCount.Length);
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