// 文件路径: CADTranslator/UI/ViewModels/ModelManagementViewModel.cs
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CADTranslator.UI.ViewModels
    {
    public class ModelManagementViewModel : INotifyPropertyChanged
        {
        public string ProfileName { get; }

        public bool IsDirty { get; private set; } = false;

        public ObservableCollection<ModelViewModel> Models { get; set; }

        private ModelViewModel _selectedModel;
        public ModelViewModel SelectedModel
            {
            get => _selectedModel;
            set
                {
                _selectedModel = value;
                OnPropertyChanged();
                }
            }

        // 仅供XAML设计器使用的无参数构造函数
        public ModelManagementViewModel()
            {
            ProfileName = "示例API配置";
            Models = new ObservableCollection<ModelViewModel>
                {
                new ModelViewModel { Index = 1, Name = "gemini-1.5-pro-latest" },
                new ModelViewModel { Index = 2, Name = "gpt-4o" }
                };
            }

        // 【已修正】这是程序运行时使用的构造函数
        public ModelManagementViewModel(string profileName, List<string> currentModels)
            {
            ProfileName = profileName;

            // 【修正1】在初始化列表时，使用Select的重载来正确生成序号
            var initialModels = currentModels.Select((modelName, index) => new ModelViewModel
                {
                Index = index + 1,
                Name = modelName
                });
            Models = new ObservableCollection<ModelViewModel>(initialModels);

            // 【修正2】订阅集合变化事件，确保在增删行时能重新编号
            Models.CollectionChanged += (s, e) =>
            {
                IsDirty = true;
                RenumberModels(); // 关键：调用重新编号的方法
            };
            }

        // 【新增】一个私有方法，专门用于重新计算所有行的序号
        private void RenumberModels()
            {
            for (int i = 0; i < Models.Count; i++)
                {
                Models[i].Index = i + 1;
                }
            }

        public void MarkAsDirty()
            {
            IsDirty = true;
            }

        public List<string> GetFinalModels()
            {
            // 确保DataGrid中新增的空行不会被保存
            return Models.Where(m => !string.IsNullOrWhiteSpace(m.Name))
                         .Select(m => m.Name.Trim())
                         .Distinct()
                         .ToList();
            }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }