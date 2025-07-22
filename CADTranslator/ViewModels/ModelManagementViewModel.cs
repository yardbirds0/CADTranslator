// 文件路径: CADTranslator/UI/ViewModels/ModelManagementViewModel.cs
using Mscc.GenerativeAI;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;

namespace CADTranslator.ViewModels
    {
    public class ModelManagementViewModel : INotifyPropertyChanged
        {
        #region --- 字段与属性 ---

        public string ProfileName { get; }
        public bool IsDirty { get; private set; } = false;
        public ObservableCollection<ModelViewModel> Models { get; set; }

        private ModelViewModel _selectedModel;
        public ModelViewModel SelectedModel
            {
            get => _selectedModel;
            set
                {
                if (SetField(ref _selectedModel, value))
                    {
                    SetFavoriteCommand.RaiseCanExecuteChanged();
                    UnsetFavoriteCommand.RaiseCanExecuteChanged();
                    }
                }
            }

        #endregion

        #region --- 命令 ---

        public RelayCommand SetFavoriteCommand { get; }
        public RelayCommand UnsetFavoriteCommand { get; }

        #endregion

        #region --- 构造函数 ---

        public ModelManagementViewModel()
            {
            ProfileName = "示例API配置";
            Models = new ObservableCollection<ModelViewModel>
            {
                new ModelViewModel { Index = 1, Name = "gemini-1.5-pro-latest", IsFavorite = true },
                new ModelViewModel { Index = 2, Name = "gpt-4o" }
            };
            }

        public ModelManagementViewModel(string profileName, List<string> allModels, List<string> favoriteModels)
            {
            ProfileName = profileName;

            SetFavoriteCommand = new RelayCommand(OnSetFavorite, p => SelectedModel != null && !SelectedModel.IsFavorite);
            UnsetFavoriteCommand = new RelayCommand(UnsetFavorite, p => SelectedModel != null && SelectedModel.IsFavorite);

            var initialModels = allModels.Select(modelName => new ModelViewModel
                {
                Name = modelName,
                IsFavorite = favoriteModels?.Contains(modelName) ?? false
                }).ToList();

            Models = new ObservableCollection<ModelViewModel>(initialModels);
            SortAndRenumberModels();

            // ▼▼▼ 【核心修正】改进事件订阅逻辑 ▼▼▼
            Models.CollectionChanged += (s, e) =>
            {
                MarkAsDirty(); // 任何增删操作都标记为脏

                // 为新添加的行订阅属性变化事件
                if (e.NewItems != null)
                    {
                    foreach (ModelViewModel item in e.NewItems)
                        {
                        item.PropertyChanged += OnModelPropertyChanged;
                        }
                    }
                // 为被删除的行取消订阅，防止内存泄漏
                if (e.OldItems != null)
                    {
                    foreach (ModelViewModel item in e.OldItems)
                        {
                        item.PropertyChanged -= OnModelPropertyChanged;
                        }
                    }
                RenumberModels();
            };

            // 为初始加载的行订阅属性变化事件
            foreach (var model in Models)
                {
                model.PropertyChanged += OnModelPropertyChanged;
                }
            // ▲▲▲ 修正结束 ▲▲▲
            }

        #endregion

        #region --- 命令与事件处理 ---

        private void OnSetFavorite(object parameter)
            {
            if (SelectedModel == null) return;
            SelectedModel.IsFavorite = true;
            SortAndRenumberModels(); // 【核心修正】在属性设置完成后，再安全地调用排序
            }

        private void UnsetFavorite(object parameter)
            {
            if (SelectedModel == null) return;
            SelectedModel.IsFavorite = false;
            SortAndRenumberModels(); // 【核心修正】在属性设置完成后，再安全地调用排序
            }

        private void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e)
            {
            // 【核心修正】现在任何属性的改变（包括Name和IsFavorite）都会标记为脏
            MarkAsDirty();

            if (e.PropertyName == nameof(ModelViewModel.IsFavorite))
                {
                SetFavoriteCommand.RaiseCanExecuteChanged();
                UnsetFavoriteCommand.RaiseCanExecuteChanged();
                }
            }
        #endregion

        #region --- 辅助方法 ---

        private void SortAndRenumberModels()
            {
            var sortedModels = Models
                .OrderByDescending(m => m.IsFavorite)
                .ThenBy(m => m.Name)
                .ToList();

            Models.Clear();
            foreach (var model in sortedModels)
                {
                Models.Add(model);
                }

            RenumberModels();
            }

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

        public void MarkAsSaved()
            {
            IsDirty = false;
            }

        public (List<string> FinalModels, List<string> FavoriteModels) GetFinalModels()
            {
            var finalModels = Models
                .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                .Select(m => m.Name.Trim())
                .Distinct()
                .ToList();

            var favoriteModels = Models
                .Where(m => m.IsFavorite && !string.IsNullOrWhiteSpace(m.Name))
                .Select(m => m.Name.Trim())
                .Distinct()
                .ToList();

            return (finalModels, favoriteModels);
            }

        #endregion

        #region --- INotifyPropertyChanged 实现 ---

        public event PropertyChangedEventHandler PropertyChanged;
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

        #endregion
        }
    }