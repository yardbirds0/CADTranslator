using System;
using System.Collections.Generic; // 确保引用了Collections.Generic
using System.Linq;
using System.Threading.Tasks;
using OpenAI.Chat;


namespace CADTranslator.Services
    {
    public class OpenAiTranslator : ITranslator
        {
        private readonly ChatClient _client;

        public OpenAiTranslator(string apiKey, string model)
            {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException(nameof(apiKey), "OpenAI API Key cannot be null or empty.");

            _client = new ChatClient(model, apiKey);
            }

        public async Task<string> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage)
            {
            try
                {
                // ▼▼▼ 这是修改后的部分 ▼▼▼
                // 我们明确地创建一个 List<ChatMessage> 类型的列表
                var messages = new List<ChatMessage>
                {
                    // 系统消息，用于设定AI的角色和任务
                    new SystemChatMessage($"You are a professional translator. Your task is to translate the user's text from {fromLanguage} to {toLanguage}. Do not add any extra explanations, just return the translated text."),
                    // 用户消息，包含需要翻译的原文
                    new UserChatMessage(textToTranslate)
                };
                // ▲▲▲ 修改结束 ▲▲▲

                // 调用API获取聊天补全结果
                ChatCompletion completion = await _client.CompleteChatAsync(messages);

                // 提取返回的文本内容
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
                return $"调用OpenAI API时发生异常: {ex.Message.Replace('\t', ' ')}";
                }
            }
        }
    }