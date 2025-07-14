using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace CADTranslator.UI.ViewModels
    {
    public class ModelManagementViewModel
        {
        // 新增属性：用于在窗口标题显示
        public string ProfileName { get; }

        public bool IsDirty { get; private set; } = false;
        public ObservableCollection<ModelViewModel> Models { get; set; }

        /// <summary>
        /// 这个无参数的构造函数仅供XAML设计器在设计时使用。
        /// </summary>
        public ModelManagementViewModel()
        {
            // 提供一些示例数据，以便在设计器中看到预览效果
            ProfileName = "示例API配置";
            Models = new ObservableCollection<ModelViewModel>
            {
                new ModelViewModel { Name = "gemini-1.5-pro-latest" },
                new ModelViewModel { Name = "gpt-4o" },
                new ModelViewModel { Name = "some-custom-model" }
            };
        }

        // 修改构造函数：接收 profileName
        public ModelManagementViewModel(string profileName, List<string> currentModels)
            {
            ProfileName = profileName; // 保存名称
            Models = new ObservableCollection<ModelViewModel>(currentModels.Select(m => new ModelViewModel { Name = m }));
            Models.CollectionChanged += (s, e) => IsDirty = true;
            }

        public List<string> GetFinalModels()
            {
            return Models.Select(m => m.Name.Trim()).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
            }
        }
    }