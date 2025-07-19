using Autodesk.AutoCAD.DatabaseServices;
using System.Collections.Generic;
using System.ComponentModel;
using Autodesk.AutoCAD.Geometry;
using System.Windows.Media; // 需要引入此命名空间来使用 Brush 类型
using System.Runtime.CompilerServices; // 需要引入此命名空间来使用 [CallerMemberName]

namespace CADTranslator.ViewModels
    {
    // 【新增】定义一个枚举来清晰地表示翻译状态
    public enum TranslationStatus
        {
        Idle,         // 闲置
        Translating,  // 翻译中
        Success,      // 成功
        Failed        // 失败
        }

    public class TextBlockViewModel : INotifyPropertyChanged
        {
        #region --- 私有字段 ---

        private string _originalText;
        private string _translatedText;
        private TranslationStatus _status = TranslationStatus.Idle; // 新增：翻译状态
        private Brush _rowBackground;                             // 新增：行背景色

        #endregion

        #region --- 绑定到UI的属性 ---

        public int Id { get; set; }
        public string Character { get; set; }
        public Brush BgColor { get; set; }

        public string OriginalText
            {
            get => _originalText;
            set => SetField(ref _originalText, value);
            }

        public string TranslatedText
            {
            get => _translatedText;
            set => SetField(ref _translatedText, value);
            }

        // 【新增】用于驱动背景色变化的属性
        public TranslationStatus Status
            {
            get => _status;
            set => SetField(ref _status, value);
            }

        // 【新增】用于在DataGrid中绑定背景色的属性
        public Brush RowBackground
            {
            get => _rowBackground;
            set => SetField(ref _rowBackground, value);
            }

        #endregion

        #region --- 存储CAD信息的属性 ---

        public List<ObjectId> SourceObjectIds { get; set; } = new List<ObjectId>();
        public int OriginalSpaceCount { get; set; } = 0;
        public Point3d Position { get; set; }
        public Point3d AlignmentPoint { get; set; }
        public TextHorizontalMode HorizontalMode { get; set; }
        public TextVerticalMode VerticalMode { get; set; }
        public ObjectId AssociatedGraphicsBlockId { get; set; } = ObjectId.Null;
        public Point3d OriginalAnchorPoint { get; set; }
        public bool IsTitle { get; set; } = false;
        public string GroupKey { get; set; } = null;

        #endregion

        #region --- INotifyPropertyChanged 实现 ---

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
            {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
            }

        #endregion
        }
    }