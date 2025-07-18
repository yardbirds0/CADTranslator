// 文件路径: CADTranslator/Services/Translation/SiliconFlowTranslator.cs

using CADTranslator.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
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
                throw new ArgumentNullException(nameof(apiEndpoint), "SiliconFlow API 终结点 (URL) 不能为空。");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException(nameof(apiKey), "SiliconFlow API 密钥不能为空。");
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model), "SiliconFlow 模型名称不能为空。");

            _endpoint = apiEndpoint.TrimEnd('/');
            _apiKey = apiKey;
            _model = model;

            // 【已修正】应用惰性加载
            _lazyHttpClient = new Lazy<HttpClient>(() =>
            {
                var client = new HttpClient();
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

        public async Task<string> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage)
            {
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

                // 【已修正】使用 .Value 获取实例
                var response = await _lazyHttpClient.Value.PostAsync(requestUrl, content);

                if (!response.IsSuccessStatusCode)
                    {
                    string errorBody = await response.Content.ReadAsStringAsync();
                    return $"请求失败: {response.StatusCode}。URL: {requestUrl}。详情: {errorBody}";
                    }

                string jsonResponse = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(jsonResponse);
                var translatedText = data["choices"]?[0]?["message"]?["content"]?.ToString();

                return translatedText?.Trim() ?? "翻译失败：未能从API响应中解析出有效内容。";
                }
            catch (Exception ex)
                {
                return $"调用SiliconFlow API({_endpoint})时发生异常: {ex.Message.Replace('\t', ' ')}";
                }
            }

        public async Task<List<string>> GetModelsAsync()
            {
            const string modelListUrl = "https://api.siliconflow.cn/v1/models";
            try
                {
                // 【已修正】使用 .Value 获取实例
                var response = await _lazyHttpClient.Value.GetAsync(modelListUrl);
                if (!response.IsSuccessStatusCode)
                    {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"请求模型列表失败，状态码：{response.StatusCode}。详情: {errorContent}");
                    }

                var content = await response.Content.ReadAsStringAsync();
                var responseObject = JObject.Parse(content);
                var modelsArray = responseObject["data"] as JArray;

                if (modelsArray == null)
                    {
                    throw new InvalidOperationException("在API响应中未找到'data'数组或格式不正确。");
                    }

                return modelsArray.Select(m => m["id"]?.ToString() ?? string.Empty).ToList();
                }
            catch (Exception ex)
                {
                throw new Exception($"获取SiliconFlow模型时发生错误: {ex.Message}", ex);
                }
            }

        public async Task<List<KeyValuePair<string, string>>> CheckBalanceAsync()
            {
            const string userInfoUrl = "https://api.siliconflow.cn/v1/user/info";
            try
                {
                var response = await _lazyHttpClient.Value.GetAsync(userInfoUrl);

                if (!response.IsSuccessStatusCode)
                    {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"请求余额失败，状态码：{response.StatusCode}。详情: {errorContent}");
                    }

                var content = await response.Content.ReadAsStringAsync();
                var userData = JObject.Parse(content)["data"] as JObject; // 确保是JObject

                if (userData == null)
                    {
                    throw new InvalidOperationException("API响应中未找到'data'对象或格式不正确。");
                    }

                // 【核心修正】
                // 动态遍历JObject的所有属性，并将其添加到列表中
                var balanceInfo = new List<KeyValuePair<string, string>>();
                foreach (var property in userData.Properties())
                    {
                    // 对totalBalance做特殊处理，加上货币符号
                    string value = property.Name == "totalBalance"
                        ? $"{property.Value} ¥"
                        : property.Value.ToString();

                    balanceInfo.Add(new KeyValuePair<string, string>(property.Name, value));
                    }

                return balanceInfo;
                }
            catch (Exception ex)
                {
                throw new Exception($"获取SiliconFlow余额时发生错误: {ex.Message}", ex);
                }
            }

        #endregion
        }
    }