// CADTranslator/UI/ViewModels/ModelViewModel.cs
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CADTranslator.ViewModels
    {
    // 一个简单的包装类，用于在DataGrid中显示和编辑模型名称
    public class ModelViewModel : INotifyPropertyChanged
        {
        private int _index;
        private string _name;

        public int Index
            {
            get => _index;
            set
                {
                if (_index != value)
                    {
                    _index = value;
                    OnPropertyChanged();
                    }
                }
            }

        public string Name
            {
            get => _name;
            set
                {
                if (_name != value)
                    {
                    _name = value;
                    OnPropertyChanged();
                    }
                }
            }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }