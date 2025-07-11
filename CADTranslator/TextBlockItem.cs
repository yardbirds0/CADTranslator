using Autodesk.AutoCAD.DatabaseServices;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media; // 需要引入此命名空间来使用 Brush 类型

namespace CADTranslator
{
    public class TextBlockItem : INotifyPropertyChanged
    {
        private string _originalText;
        private string _translatedText;

        // --- 新增属性 ---
        // 这两个属性是为了适配新的UI样式，用于显示每一行数据前面的彩色圆形首字母。
        public string Character { get; set; }
        public Brush BgColor { get; set; }
        // --- 新增结束 ---

        public int Id { get; set; }

        public string OriginalText
        {
            get => _originalText;
            set { if (_originalText != value) { _originalText = value; OnPropertyChanged(nameof(OriginalText)); } }
        }

        public string TranslatedText
        {
            get => _translatedText;
            set { if (_translatedText != value) { _translatedText = value; OnPropertyChanged(nameof(TranslatedText)); } }
        }

        public List<ObjectId> SourceObjectIds { get; set; } = new List<ObjectId>();

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}