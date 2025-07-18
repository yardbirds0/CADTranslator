// 文件路径: CADTranslator/Services/Translation/OpenAiTranslator.cs

using CADTranslator.Models;
using OpenAI.Chat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CADTranslator.Services
    {
    public class OpenAiTranslator : ITranslator
        {
        #region --- 字段 ---

        // 【核心修改】使用 Lazy<T> 来实现惰性加载
        private readonly Lazy<ChatClient> _lazyClient;

        // 我们仍然需要存储这些值，以便在需要时创建客户端
        private readonly string _model;
        private readonly string _apiKey;

        #endregion

        #region --- 构造函数 ---

        public OpenAiTranslator(string apiKey, string model)
            {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException(nameof(apiKey), "OpenAI API Key cannot be null or empty.");
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model), "OpenAI 模型名称不能为空。");

            _apiKey = apiKey;
            _model = model;

            // 【核心修改】
            // 我们不再直接创建 ChatClient。
            // 相反，我们初始化 _lazyClient，并告诉它“将来”应该如何创建 ChatClient。
            // 里面的代码只有在第一次访问 _lazyClient.Value 时才会执行。
            _lazyClient = new Lazy<ChatClient>(() => new ChatClient(_model, _apiKey));
            }

        #endregion

        #region --- 1. 身份标识 (ITranslator 实现) ---

        public ApiServiceType ServiceType => ApiServiceType.OpenAI;
        public string DisplayName => "ChatGPT";
        public string ApiDocumentationUrl => "https://platform.openai.com/docs/introduction";

        #endregion

        #region --- 2. 能力声明 (ITranslator 实现) ---

        public bool IsApiKeyRequired => true;
        public bool IsUserIdRequired => false;
        public bool IsApiUrlRequired => false;
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
                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage($"You are a professional translator for Civil Engineering drawings. Your task is to translate the user's text from {fromLanguage} to {toLanguage}. Do not add any extra explanations, just return the translated text. If you encounter symbols, keep their original style."),
                    new UserChatMessage(textToTranslate)
                };

                // 【核心修改】使用 _lazyClient.Value 来获取客户端实例。
                // 如果这是第一次调用，它会自动创建 ChatClient。
                ChatCompletion completion = await _lazyClient.Value.CompleteChatAsync(messages);

                if (completion.Content != null && completion.Content.Any())
                    {
                    return completion.Content[0].Text.Trim();
                    }
                else
                    {
                    return $"翻译失败：API返回内容为空。完成原因: {completion.FinishReason}";
                    }
                }
            catch (Exception ex)
                {
                // 如果在创建或使用客户端时出错（例如，因为依赖项真的丢失了），
                // 错误会在这里被捕获，而不会在程序启动时就崩溃。
                return $"调用OpenAI API时发生异常: {ex.Message.Replace('\t', ' ')}";
                }
            }

        public Task<List<string>> GetModelsAsync()
            {
            throw new NotSupportedException("当前OpenAI集成不支持在线获取模型列表。请在模型管理中手动添加。");
            }

        public Task<List<KeyValuePair<string, string>>> CheckBalanceAsync()
            {
            throw new NotSupportedException("OpenAI API 服务不支持在线查询余额。");
            }

        #endregion
        }
    }