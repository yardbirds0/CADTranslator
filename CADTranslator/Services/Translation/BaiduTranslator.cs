using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq; // 需要此引用来使用 LINQ 的 Select 方法
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace CADTranslator.Services
{
    public class BaiduTranslator : ITranslator
    {
        private readonly string _appId;
        private readonly string _appKey;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) }; // 设置10秒超时

        /// <summary>
        /// 构造函数。如果传入的ID或密钥为空，则使用默认值。
        /// </summary>
        /// <param name="appId">百度翻译App ID</param>
        /// <param name="appKey">百度翻译密钥</param>
        public BaiduTranslator(string appId, string appKey)
        {
            // 1. 【新增】如果传入的ID或密钥为空，则使用您指定的默认值
            _appId = string.IsNullOrWhiteSpace(appId) ? "20250708002400901" : appId;
            _appKey = string.IsNullOrWhiteSpace(appKey) ? "1L_Bso6ORO8torYgecjh" : appKey;
        }

        public async Task<string> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage)
        {
            if (string.IsNullOrWhiteSpace(textToTranslate))
            {
                return "";
            }

            // 2. 【修正】与之前一样，处理换行符对于保证签名正确至关重要
            string queryText = textToTranslate.Replace('\n', ' ');

            var random = new Random();
            string salt = random.Next(32768, 65536).ToString();
            string sign = GenerateSign(queryText, salt);
            string baseUrl = "http://api.fanyi.baidu.com/api/trans/vip/translate";

            // 3. 【核心修正】严格按照官方C#示例，将所有参数构建为URL查询字符串
            var queryParams = new Dictionary<string, string>
            {
                { "q", queryText },
                { "from", fromLanguage },
                { "to", toLanguage },
                { "appid", _appId },
                { "salt", salt },
                { "sign", sign }
            };

            // 使用LINQ和Uri.EscapeDataString来安全地构建查询字符串，确保特殊字符被正确编码
            string queryString = string.Join("&", queryParams.Select(kvp =>
                $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));

            string fullUrl = $"{baseUrl}?{queryString}";

            try
            {
                // 4. 【核心修正】使用GET方法进行请求
                var response = await _httpClient.GetAsync(fullUrl);
                response.EnsureSuccessStatusCode();

                string jsonResponse = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<BaiduTranslationResult>(jsonResponse);

                if (result?.TransResult != null && result.TransResult.Any())
                {
                    // 使用 StringBuilder 合并可能分段返回的翻译结果
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

        // MD5签名生成算法保持不变
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

        // 用于解析JSON的辅助类保持不变
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
    }
}