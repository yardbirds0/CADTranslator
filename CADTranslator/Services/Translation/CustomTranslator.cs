// 文件路径: CADTranslator/Services/Translation/CustomTranslator.cs

using CADTranslator.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CADTranslator.Services
    {
    public class CustomTranslator : ITranslator
        {
        #region --- 字段 ---

        private readonly Lazy<HttpClient> _lazyHttpClient;
        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _model;

        #endregion

        #region --- 构造函数 ---

        public CustomTranslator(string apiEndpoint, string apiKey, string model)
            {
            if (string.IsNullOrWhiteSpace(apiEndpoint))
                throw new ArgumentNullException(nameof(apiEndpoint), "自定义API终结点 (URL) 不能为空。");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException(nameof(apiKey), "自定义API密钥不能为空。");
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model), "自定义API模型名称不能为空。");

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

        public ApiServiceType ServiceType => ApiServiceType.Custom;
        public string DisplayName => "自定义接口";
        public string ApiDocumentationUrl => "";

        #endregion

        #region --- 2. 能力声明 (ITranslator 实现) ---

        public bool IsApiKeyRequired => true;
        public bool IsUserIdRequired => false;
        public bool IsApiUrlRequired => true;
        public bool IsModelRequired => true;
        public bool IsPromptSupported => true;
        public bool IsModelFetchingSupported => false;
        public bool IsBalanceCheckSupported => false;

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
                        new { role = "system", content = $"You are a professional translator for Civil Engineering drawings. Your task is to translate the user's text from {fromLanguage} to {toLanguage}. Do not add any extra explanations, just return the translated text. If you encounter symbols, keep their original style." },
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
                return $"调用自定义API({_endpoint})时发生异常: {ex.Message.Replace('\t', ' ')}";
                }
            }

        public Task<List<string>> GetModelsAsync()
            {
            throw new NotSupportedException("自定义接口服务不支持在线获取模型列表。请在模型管理中手动添加。");
            }

        public Task<List<KeyValuePair<string, string>>> CheckBalanceAsync()
            {
            throw new NotSupportedException("自定义接口服务不支持在线查询余额。");
            }

        #endregion
        }
    }