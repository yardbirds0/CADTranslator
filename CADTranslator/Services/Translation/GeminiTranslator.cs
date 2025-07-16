// 文件路径: CADTranslator/Services/Translation/GeminiTranslator.cs

using Mscc.GenerativeAI; // <-- 1. 引用新的命名空间
using System;
using System.Threading.Tasks;

namespace CADTranslator.Services
    {
    public class GeminiTranslator : ITranslator
        {
        private readonly IGenerativeAI _googleAI; // <-- 2. 使用接口，更灵活
        private readonly string _model;

        public GeminiTranslator(string apiKey, string model)
            {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentNullException(nameof(apiKey), "Gemini API Key 不能为空。");
            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentNullException(nameof(model), "Gemini 模型名称不能为空。");

            // 3. 初始化方式改变
            _googleAI = new GoogleAI(apiKey: apiKey);
            _model = model;
            }

        public async Task<string> TranslateAsync(string textToTranslate, string fromLanguage, string toLanguage)
            {
            try
                {
                // 4. 获取模型和生成内容的方式改变
                var generativeModel = _googleAI.GenerativeModel(model: _model);
                string prompt = $"You are a professional translator for Civil Engineering drawings. Your task is to translate the user's text from {fromLanguage} to {toLanguage}. Do not add any extra explanations, just return the translated text. If you encounter symbols, keep their original style.\n\nText to translate:\n---\n{textToTranslate}\n---";

                var response = await generativeModel.GenerateContent(prompt);

                // 5. 获取结果的方式改变 (从 response.Text() 方法变为 response.Text 属性)
                if (response != null && !string.IsNullOrEmpty(response.Text))
                    {
                    return response.Text.Trim();
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