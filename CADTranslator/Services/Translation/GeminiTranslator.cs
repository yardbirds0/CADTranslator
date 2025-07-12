using GenerativeAI;
using System;
using System.Threading.Tasks;

namespace CADTranslator.Services
    {
    public class GeminiTranslator : ITranslator
        {
        private readonly string _apiKey;
        // 我们可以指定一个默认模型，未来也可以从UI传入
        private const string DefaultModel = "models/gemini-1.5-flash";

        public GeminiTranslator(string apiKey)
            {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException(nameof(apiKey), "Gemini API Key cannot be null or empty.");
            _apiKey = apiKey;
            }

        public async Task<string> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage)
            {
            try
                {
                // 1. 初始化GoogleAI客户端
                var googleAI = new GoogleAi(_apiKey);

                // 2. 创建一个生成模型实例
                var model = googleAI.CreateGenerativeModel(DefaultModel);

                // 3. 构建翻译的Prompt
                // 为了让模型更专注于翻译任务，我们使用一个结构化的Prompt
                string prompt = $"Please translate the following text from {fromLanguage} to {toLanguage}. Do not add any extra explanations or introductory phrases, just return the translated text.\n\nText to translate:\n---\n{textToTranslate}\n---";

                // 4. 调用API生成内容
                var response = await model.GenerateContentAsync(prompt);

                // 5. 提取并返回结果
                // 新库的Text()方法可以非常方便地获取纯文本结果
                if (response != null && !string.IsNullOrEmpty(response.Text()))
                    {
                    return response.Text().Trim();
                    }
                else
                    {
                    return "翻译失败：API未返回有效内容。";
                    }
                }
            catch (Exception ex)
                {
                // 捕获并返回更详细的异常信息
                return $"调用Gemini API时发生异常: {ex.Message}";
                }
            }
        }
    }