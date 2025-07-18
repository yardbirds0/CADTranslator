// 文件路径: CADTranslator/Services/Translation/BaiduTranslator.cs

using CADTranslator.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CADTranslator.Services
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
            _appId = string.IsNullOrWhiteSpace(appId) ? "20250708002400901" : appId;
            _appKey = string.IsNullOrWhiteSpace(appKey) ? "1L_Bso6ORO8torYgecjh" : appKey;
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
        public bool IsPromptSupported => false; // 百度翻译是专用接口，不支持自定义提示词
        public bool IsModelFetchingSupported => false; // IsModelRequired为false，此项也为false
        public bool IsBalanceCheckSupported => false;

        #endregion

        #region --- 3. 核心与扩展功能 (ITranslator 实现) ---

        public async Task<string> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage)
            {
            if (string.IsNullOrWhiteSpace(textToTranslate))
                {
                return "";
                }

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

            try
                {
                var response = await _httpClient.GetAsync(fullUrl);
                response.EnsureSuccessStatusCode();

                string jsonResponse = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<BaiduTranslationResult>(jsonResponse);

                if (result?.TransResult != null && result.TransResult.Any())
                    {
                    var translatedText = new StringBuilder();
                    foreach (var part in result.TransResult)
                        {
                        translatedText.Append(part.Dst);
                        }
                    return translatedText.ToString();
                    }
                else if (!string.IsNullOrEmpty(result?.ErrorCode))
                    {
                    return $"百度API返回错误: Code={result.ErrorCode}, Message={result.ErrorMessage?.Replace('\t', ' ')}";
                    }
                else
                    {
                    return "翻译失败：API未返回有效或可解析的结果。";
                    }
                }
            catch (Exception ex)
                {
                return $"调用百度翻译API时出错: {ex.Message.Replace('\t', ' ')}";
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