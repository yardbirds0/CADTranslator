using Autodesk.AutoCAD.DatabaseServices;
using System.Collections.Generic;
using System.ComponentModel;
namespace CADTranslator
{
    public class TextBlockItem : INotifyPropertyChanged
    {
        private string _originalText;
        private string _translatedText;
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