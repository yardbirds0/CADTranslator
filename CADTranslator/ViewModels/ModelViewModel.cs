// CADTranslator/UI/ViewModels/ModelViewModel.cs
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CADTranslator.ViewModels
    {
    // 一个简单的包装类，用于在DataGrid中显示和编辑模型名称
    public class ModelViewModel : INotifyPropertyChanged
        {
        private int _index;
        private string _name;
        private bool _isFavorite; // 【新增】私有字段

        public int Index
            {
            get => _index;
            set => SetField(ref _index, value);
            }

        public string Name
            {
            get => _name;
            set => SetField(ref _name, value);
            }

        // ▼▼▼ 【新增】公开的 IsFavorite 属性 ▼▼▼
        public bool IsFavorite
            {
            get => _isFavorite;
            set => SetField(ref _isFavorite, value);
            }

        public event PropertyChangedEventHandler PropertyChanged;

        // ▼▼▼ 【新增】一个通用的 SetField 方法来简化代码 ▼▼▼
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
            {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
            }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }