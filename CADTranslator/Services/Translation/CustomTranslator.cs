using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CADTranslator.Services
    {
    public class CustomTranslator : ITranslator
        {
        private readonly HttpClient _httpClient;
        private readonly string _endpoint;
        private readonly string _apiKey;

        public CustomTranslator(string apiEndpoint, string apiKey)
            {
            if (string.IsNullOrWhiteSpace(apiEndpoint))
                throw new ArgumentNullException(nameof(apiEndpoint), "Custom API Endpoint cannot be null or empty.");
            // API Key可以是可选的，取决于具体服务
            _apiKey = apiKey;
            _endpoint = apiEndpoint;

            _httpClient = new HttpClient();
            // 只有在提供了API Key的情况下才添加认证头
            if (!string.IsNullOrWhiteSpace(_apiKey))
                {
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
                }
            }

        public async Task<string> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage)
            {
            try
                {
                // 1. 构建请求体 (模仿您提供的代码)
                var requestData = new
                    {
                    // 模型可以从UI传入，这里我们暂时硬编码一个
                    model = "deepseek-ai/DeepSeek-V2.5",
                    messages = new[]
                    {
                        new { role = "system", content = $"Translate the following text from {fromLanguage} to {toLanguage}. Return only the translated text." },
                        new { role = "user", content = textToTranslate }
                    },
                    stream = false // 我们需要一次性返回，所以设置为false
                    };

                string jsonPayload = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // 2. 发送POST请求
                var response = await _httpClient.PostAsync(_endpoint, content);

                if (!response.IsSuccessStatusCode)
                    {
                    string errorBody = await response.Content.ReadAsStringAsync();
                    return $"请求失败，状态码：{response.StatusCode}。详情: {errorBody}";
                    }

                // 3. 解析返回的JSON结果
                string jsonResponse = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(jsonResponse);

                // 提取choices数组中的第一个元素的message.content
                var translatedText = data["choices"]?[0]?["message"]?["content"]?.ToString();

                return translatedText?.Trim() ?? "翻译失败：未能从API响应中解析出内容。";
                }
            catch (Exception ex)
                {
                return $"调用自定义API时发生异常: {ex.Message}";
                }
            }
        }
    }