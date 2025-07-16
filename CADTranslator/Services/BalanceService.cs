using CADTranslator.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace CADTranslator.Services
    {
    /// <summary>
    /// 提供从各种API获取余额信息功能的服务
    /// </summary>
    public class BalanceService
        {
        // 创建一个静态、可重用的HttpClient实例以提高性能
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string SiliconFlowUserInfoUrl = "https://api.siliconflow.cn/v1/user/info";

        /// <summary>
        /// 从硅基流动(SiliconFlow)的API异步获取用户账户信息。
        /// </summary>
        /// <param name="apiKey">用户的API密钥</param>
        /// <returns>一个包含账户信息的 BalanceRecord 对象，如果失败则返回 null</returns>
        public async Task<BalanceRecord> GetSiliconFlowBalanceAsync(string apiKey)
            {
            if (string.IsNullOrWhiteSpace(apiKey))
                {
                throw new ArgumentNullException(nameof(apiKey), "API Key不能为空。");
                }

            // 创建一个新的请求消息，以便为单次请求设置特定的认证头
            using (var requestMessage = new HttpRequestMessage(HttpMethod.Get, SiliconFlowUserInfoUrl))
                {
                requestMessage.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                try
                    {
                    var response = await _httpClient.SendAsync(requestMessage);

                    if (!response.IsSuccessStatusCode)
                        {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        // 抛出一个更具体的异常，方便上层捕获和显示
                        throw new HttpRequestException($"请求余额失败，状态码：{response.StatusCode}。详情: {errorContent}");
                        }

                    var content = await response.Content.ReadAsStringAsync();
                    var responseObject = JObject.Parse(content);
                    var userData = responseObject["data"];

                    if (userData == null)
                        {
                        throw new InvalidOperationException("API响应中未找到'data'字段或格式不正确。");
                        }

                    // 将获取到的信息填充到我们定义好的数据模型中
                    var record = new BalanceRecord
                        {
                        Timestamp = DateTime.Now,
                        ServiceType = ApiServiceType.SiliconFlow,
                        UserId = userData["id"]?.ToString() ?? "N/A",
                        AccountStatus = userData["status"]?.ToString() ?? "N/A",
                        // 格式化余额信息以便显示
                        BalanceInfo = $"余额: {userData["totalBalance"]}"
                        };

                    return record;
                    }
                catch (Exception ex)
                    {
                    // 将捕获到的任何异常（网络、解析等）重新包装并抛出，
                    // 这样调用方(ViewModel)就可以统一处理并显示给用户。
                    throw new Exception($"获取余额时发生错误: {ex.Message}", ex);
                    }
                }
            }

        // --- 预留空间 ---
        // 未来要支持百度翻译余额查询，我们可以在这里添加一个新方法：
        // public async Task<BalanceRecord> GetBaiduBalanceAsync(string appId, string appKey)
        // {
        //     // ... 实现百度的查询逻辑 ...
        // }
        }
    }