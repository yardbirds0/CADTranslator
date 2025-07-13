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