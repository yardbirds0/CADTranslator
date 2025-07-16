using Mscc.GenerativeAI;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace CADTranslator.Services
    {
    /// <summary>
    /// 提供从各种API获取模型列表功能的服务
    /// </summary>
    public class ModelFetchingService
        {
        // 创建一个静态的、可重用的HttpClient实例，以提高性能
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// 从硅基流动(SiliconFlow)的API异步获取模型列表。
        /// </summary>
        /// <param name="apiKey">用户的API密钥</param>
        /// <returns>一个包含模型名称的列表</returns>
        public async Task<List<string>> GetSiliconFlowModelsAsync(string apiKey)
            {
            // 如果apiKey为空，则直接抛出异常，防止无效请求
            if (string.IsNullOrWhiteSpace(apiKey))
                {
                throw new ArgumentNullException(nameof(apiKey), "API Key不能为空。");
                }

            // 硅基流动获取模型列表的固定URL
            const string modelListUrl = "https://api.siliconflow.cn/v1/models";

            // 创建一个新的请求消息，这样我们可以为单次请求设置特定的认证头
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, modelListUrl))
                {
                // 添加认证头
                requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                try
                    {
                    var response = await _httpClient.SendAsync(requestMessage);

                    // 如果请求失败，抛出一个包含详细信息的异常
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

                    var modelList = new List<string>();
                    foreach (var model in modelsArray)
                        {
                        // 将每个模型对象的 "id" 属性值添加到列表中
                        modelList.Add(model["id"]?.ToString() ?? string.Empty);
                        }

                    return modelList;
                    }
                catch (Exception ex)
                    {
                    // 将捕获到的任何异常（网络、解析等）重新抛出，以便上层调用者（ViewModel）可以处理它
                    throw new Exception($"获取模型时发生错误: {ex.Message}", ex);
                    }
                }
            }


        /// <summary>
        /// 从Google AI (Gemini)的API异步获取模型列表。
        /// </summary>
        /// <param name="apiKey">用户的API密钥</param>
        /// <returns>一个包含模型名称的列表</returns>
        public async Task<List<string>> GetGeminiModelsAsync(string apiKey)
            {
            if (string.IsNullOrWhiteSpace(apiKey))
                {
                throw new ArgumentNullException(nameof(apiKey), "API Key不能为空。");
                }

            try
                {
                // 1. 正常初始化GoogleAI实例
                var googleAI = new Mscc.GenerativeAI.GoogleAI(apiKey: apiKey);

                // 2. 【核心修正】先用任意一个模型（这里使用默认模型）创建一个GenerativeModel实例
                var model = googleAI.GenerativeModel();

                // 3. 然后用这个model实例来调用ListModels()方法
                var models = await model.ListModels();

                if (models == null || !models.Any())
                    {
                    return new List<string>();
                    }

                // 4. 提取模型名称并返回 (这里的 .Name 应该改为 .Id)
                return models.Select(m => m.Name.Replace("models/", "")).ToList();
                }
            catch (Exception ex)
                {
                throw new Exception($"获取Gemini模型时发生错误: {ex.Message}", ex);
                }
            }
        }
    }