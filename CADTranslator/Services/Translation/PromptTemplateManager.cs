// 文件路径: CADTranslator/Services/Translation/PromptTemplateManager.cs
// 【这是一个新文件】

using CADTranslator.Models.API;
using CADTranslator.Services.Settings;
using System.Collections.Generic;

namespace CADTranslator.Services.Translation
    {
    /// <summary>
    /// 提示词模板的“单一事实来源”。
    /// 负责存储、管理和提供所有提示词。
    /// </summary>
    public static class PromptTemplateManager
        {
        // 存储所有默认专业模板的私有字典
        private static readonly Dictionary<PromptTemplateType, string> _templates = new Dictionary<PromptTemplateType, string>
            {
            [PromptTemplateType.Structure] = "你是一个专业的结构专业图紙翻译家。你的任务是把用户的文本从 {fromLanguage} 翻译成 {toLanguage}. 不要添加任何额外的解释，只返回翻译好的文本。遇到符号则保留原来的样式。",
            [PromptTemplateType.Architecture] = "你是一个专业的建筑专业图纸翻译家...",
            [PromptTemplateType.HVAC] = "你是一个专业的暖通专业图纸翻译家...",
            [PromptTemplateType.Electrical] = "你是一个专业的电气专业图纸翻译家...",
            [PromptTemplateType.Plumbing] = "你是一个专业的给排水专业图纸翻译家...",
            [PromptTemplateType.Process] = "你是一个专业的工艺专业图纸翻译家...",
            [PromptTemplateType.Thermal] = "你是一个专业的热力专业图纸翻译家..."
            };

        // 存储用于UI显示的模板名称
        public static readonly Dictionary<PromptTemplateType, string> DisplayNames = new Dictionary<PromptTemplateType, string>
            {
            [PromptTemplateType.Custom] = "自定义",
            [PromptTemplateType.Structure] = "结构专业",
            [PromptTemplateType.Architecture] = "建筑专业",
            [PromptTemplateType.HVAC] = "暖通专业",
            [PromptTemplateType.Electrical] = "电气专业",
            [PromptTemplateType.Plumbing] = "给排水专业",
            [PromptTemplateType.Process] = "工艺专业",
            [PromptTemplateType.Thermal] = "热力专业"
            };

        /// <summary>
        /// 根据用户设置，获取当前应该使用的提示词模板。
        /// </summary>
        /// <param name="settings">从本地加载的 AppSettings 对象。</param>
        /// <returns>最终应该使用的提示词字符串。</returns>
        public static string GetCurrentPrompt(AppSettings settings)
            {
            string promptTemplate;
            if (settings.SelectedPromptTemplate == PromptTemplateType.Custom)
                {
                // 如果用户选择了“自定义”，就从设置中读取保存好的自定义提示词
                promptTemplate = settings.CustomPrompt;
                }
            else
                {
                // 否则，从我们的字典中查找对应的模板
                _templates.TryGetValue(settings.SelectedPromptTemplate, out promptTemplate);
                }

            // 安全检查，如果找不到模板，提供一个最终的备用方案
            if (string.IsNullOrEmpty(promptTemplate))
                {
                promptTemplate = _templates[PromptTemplateType.Structure]; // 使用“结构专业”作为默认
                }

            return promptTemplate;
            }
        }
    }