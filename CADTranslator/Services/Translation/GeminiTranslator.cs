// ▼▼▼ 请用下面这个修正后的版本，完整替换 GeminiTranslator.cs 的现有全部代码 ▼▼▼
using GenerativeAI;
using System;
using System.Threading.Tasks;

namespace CADTranslator.Services
    {
    public class GeminiTranslator : ITranslator
        {
        private readonly string _apiKey;
        private readonly string _model; // 新增字段

        // 修正：构造函数接收模型参数
        public GeminiTranslator(string apiKey, string model)
            {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException(nameof(apiKey), "Gemini API Key cannot be null or empty.");
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model), "Gemini model cannot be null or empty.");

            _apiKey = apiKey;
            _model = model; // 保存模型
            }

        public async Task<string> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage)
            {
            try
                {
                var googleAI = new GoogleAi(_apiKey);
                // 修正：使用传入的模型
                var generativeModel = googleAI.CreateGenerativeModel(_model);
                string prompt = $"Please translate the following text from {fromLanguage} to {toLanguage}. Do not add any extra explanations or introductory phrases, just return the translated text.\n\nText to translate:\n---\n{textToTranslate}\n---";
                var response = await generativeModel.GenerateContentAsync(prompt);

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
                return $"调用Gemini API时发生异常: {ex.Message.Replace('\t', ' ')}";
                }
            }
        }
    }