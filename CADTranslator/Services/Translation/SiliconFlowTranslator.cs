using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CADTranslator.Services
    {
    public class SiliconFlowTranslator : ITranslator
        {
        private readonly HttpClient _httpClient;
        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _model;

        /// <summary>
        /// 为兼容类OpenAI标准的自定义API创建一个翻译器实例.
        /// </summary>
        /// <param name="apiEndpoint">API的基础URL, 例如 "https://api.siliconflow.cn/v1"</param>
        /// <param name="apiKey">API密钥</param>
        /// <param name="model">要使用的模型名称</param>
        public SiliconFlowTranslator(string apiEndpoint, string apiKey, string model)
            {
            if (string.IsNullOrWhiteSpace(apiEndpoint))
                throw new ArgumentNullException(nameof(apiEndpoint), "自定义API终结点 (URL) 不能为空。");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException(nameof(apiKey), "自定义API密钥不能为空。");
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model), "自定义API模型名称不能为空。");

            _endpoint = apiEndpoint.TrimEnd('/'); // 确保URL末尾没有斜杠
            _apiKey = apiKey;
            _model = model;

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            }

        public async Task<string> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage)
            {
            try
                {
                // 1. 构建请求体，与您的示例完全一致，但 stream: false
                var requestData = new
                    {
                    model = _model,
                    messages = new[]
                    {
                        // 我们可以提供一个更明确的系统指令
                       new { role = "system", content = $"你是一个专业的结构专业图纸翻译家。你的任务是把用户的文本从 {fromLanguage} 翻译成 {toLanguage}. 不要添加任何额外的解释，只返回翻译好的文本。遇到符号则保留原来的样式。" },

                        new { role = "user", content = textToTranslate }
                    },
                    stream = false // 设置为false以获取完整响应
                    };

                string jsonPayload = JsonConvert.SerializeObject(requestData);
                var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                // 2. 发送POST请求到/chat/completions终结点
                string requestUrl = $"{_endpoint}/chat/completions";
                var response = await _httpClient.PostAsync(requestUrl, content);

                if (!response.IsSuccessStatusCode)
                    {
                    string errorBody = await response.Content.ReadAsStringAsync();
                    return $"请求失败: {response.StatusCode}。URL: {requestUrl}。详情: {errorBody}";
                    }

                // 3. 解析完整的JSON响应
                string jsonResponse = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(jsonResponse);

                // 提取 choices[0].message.content 的内容
                var translatedText = data["choices"]?[0]?["message"]?["content"]?.ToString();

                return translatedText?.Trim() ?? "翻译失败：未能从API响应中解析出有效内容。";
                }
            catch (Exception ex)
                {
                // 提供更详细的异常信息，帮助调试
                return $"调用自定义API({_endpoint})时发生异常: {ex.Message.Replace('\t', ' ')}";
                }
            }
        }
    }