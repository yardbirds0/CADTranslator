using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CADTranslator.UI.ViewModels
    {
    // 一个简单的包装类，用于在DataGrid中显示和编辑模型名称
    public class ModelViewModel : INotifyPropertyChanged
        {
        private string _name;

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