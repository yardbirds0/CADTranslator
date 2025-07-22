// 文件路径: CADTranslator/Services/Translation/GeminiTranslator.cs
// 【请使用此版本替换】

using CADTranslator.Models;
using CADTranslator.Models.API;
using Mscc.GenerativeAI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CADTranslator.Services.Translation
    {
    public class GeminiTranslator : ITranslator
        {
        #region --- 字段 ---
        private readonly Lazy<IGenerativeAI> _lazyGoogleAI;
        private readonly string _model;
        private readonly string _apiKey;
        #endregion

        #region --- 构造函数 ---
        public GeminiTranslator(string apiKey, string model)
            {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ApiException(ApiErrorType.ConfigurationError, ApiServiceType.Gemini, "API Key 不能为空。");

            _apiKey = apiKey;
            _model = model;
            _lazyGoogleAI = new Lazy<IGenerativeAI>(() => new GoogleAI(apiKey: _apiKey));
            }
        #endregion

        #region --- 身份与能力 (ITranslator 实现) ---
        public ApiServiceType ServiceType => ApiServiceType.Gemini;
        public string DisplayName => "谷歌Gemini";
        public string ApiDocumentationUrl => "https://ai.google.dev/docs";
        public bool IsApiKeyRequired => true;
        public bool IsUserIdRequired => false;
        public bool IsApiUrlRequired => false;
        public bool IsModelRequired => true;
        public bool IsPromptSupported => true;
        public bool IsModelFetchingSupported => true;
        public bool IsBalanceCheckSupported => false;
        public bool IsTokenCountSupported => true;
        #endregion

        #region --- 核心与扩展功能 (ITranslator 实现) ---

        // ▼▼▼ 请用此方法完整替换旧的 TranslateAsync 方法 ▼▼▼
        public async Task<string> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage, CancellationToken cancellationToken)
            {
            if (string.IsNullOrWhiteSpace(_model))
                throw new ApiException(ApiErrorType.ConfigurationError, ServiceType, "模型名称不能为空。");

            // 在发起API调用前，检查任务是否已被取消
            cancellationToken.ThrowIfCancellationRequested();

            try
                {
                var generativeModel = _lazyGoogleAI.Value.GenerativeModel(model: _model);
                string prompt = $"You are a professional translator for Civil Engineering drawings. Your task is to translate the user's text from {fromLanguage} to {toLanguage}. Do not add any extra explanations, just return the translated text. If you encounter symbols, keep their original style.\n\nText to translate:\n---\n{textToTranslate}\n---";

                // 【核心修改】创建一个代表网络请求的任务
                var translationTask = generativeModel.GenerateContent(prompt);

                // 【核心修改】创建一个20秒的超时任务
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);

                // 【核心修改】让网络任务和超时任务进行“赛跑”
                var completedTask = await Task.WhenAny(translationTask, timeoutTask);

                if (completedTask == timeoutTask)
                    {
                    // 如果是超时任务先完成，就抛出网络错误异常
                    throw new ApiException(ApiErrorType.NetworkError, ServiceType, "请求超时 (超过20秒)。");
                    }

                // 如果是网络任务先完成，就获取它的结果
                var response = await translationTask;

                if (response?.Text == null)
                    {
                    throw new ApiException(ApiErrorType.InvalidResponse, ServiceType, "API返回了空或无效的响应内容。");
                    }
                return response.Text.Trim();
                }
            // 捕获由我们主动熔断（cancellationToken）引发的取消
            catch (OperationCanceledException)
                {
                throw;
                }
            // 捕获所有其他异常并进行分类
            catch (Exception ex)
                {
                // 如果在捕获通用异常时，发现取消信号已经被触发了，那么也按“取消”处理
                if (cancellationToken.IsCancellationRequested)
                    {
                    throw new OperationCanceledException();
                    }

                // 通过检查异常消息来判断是否为网络问题
                if (ex.Message.Contains("Request timed out") || ex.Message.Contains("NameResolutionFailure"))
                    {
                    throw new ApiException(ApiErrorType.NetworkError, ServiceType, $"网络请求失败: {ex.Message}");
                    }

                // 其他所有情况，都视为API接口错误
                throw new ApiException(ApiErrorType.ApiError, ServiceType, ex.Message);
                }
            }

        public async Task<List<string>> GetModelsAsync(CancellationToken cancellationToken)
            {
            try
                {
                var generativeModel = _lazyGoogleAI.Value.GenerativeModel();
                // 【核心修改】将 cancellationToken 传递给 ListModels 方法
                var models = await generativeModel.ListModels(cancellationToken: cancellationToken);
                if (models == null || !models.Any()) return new List<string>();
                return models
                       .Where(m => m.SupportedGenerationMethods.Contains("generateContent"))
                       .Select(m => m.Name.Replace("models/", ""))
                       .ToList();
                }
            catch (TaskCanceledException)
                {
                // 【核心修改】捕获并重新抛出取消异常
                throw;
                }
            catch (Exception ex)
                {
                if (ex.Message.Contains("Request timed out") || ex.Message.Contains("NameResolutionFailure"))
                    {
                    throw new ApiException(ApiErrorType.NetworkError, ServiceType, $"获取模型列表时网络请求失败: {ex.Message}");
                    }
                throw new ApiException(ApiErrorType.ApiError, ServiceType, $"获取模型列表时发生错误: {ex.Message}");
                }
            }
        public Task<List<KeyValuePair<string, string>>> CheckBalanceAsync()
            {
            throw new NotSupportedException("Gemini API 服务不支持在线查询余额。");
            }

        public async Task<int> CountTokensAsync(string textToCount)
            {
            if (string.IsNullOrWhiteSpace(_model))
                throw new ApiException(ApiErrorType.ConfigurationError, ServiceType, "模型名称不能为空。");

            if (string.IsNullOrEmpty(textToCount))
                return 0;

            try
                {
                var generativeModel = _lazyGoogleAI.Value.GenerativeModel(model: _model);

                // 根据 Mscc.GenerativeAI 的用法，调用 CountTokens 方法
                var response = await generativeModel.CountTokens(textToCount);

                // 从返回结果中获取 Token 总数
                return response.TotalTokens;
                }
            catch (Exception ex)
                {
                // 将所有可能发生的异常，统一包装成我们自定义的 ApiException
                if (ex.Message.Contains("Request timed out") || ex.Message.Contains("NameResolutionFailure"))
                    {
                    throw new ApiException(ApiErrorType.NetworkError, ServiceType, $"计算Token时网络请求失败: {ex.Message}");
                    }
                throw new ApiException(ApiErrorType.ApiError, ServiceType, $"计算Token时发生错误: {ex.Message}");
                }
            }
        #endregion
        }
    }