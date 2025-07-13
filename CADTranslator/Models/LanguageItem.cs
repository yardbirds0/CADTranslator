namespace CADTranslator.Models
    {
    public class LanguageItem
        {
        public string DisplayName { get; set; } // 用于UI显示，例如 "中文"
        public string Value { get; set; }      // 用于API调用，例如 "zh"
        }
    }