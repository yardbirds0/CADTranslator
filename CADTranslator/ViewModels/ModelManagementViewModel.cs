using CADTranslator.Services.UI; // 新增引用
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks; // 新增引用
using System.Windows;
using System.Windows.Input; // 新增引用
using Wpf.Ui.Controls;     // 新增引用

namespace CADTranslator.ViewModels
    {
    public class ModelManagementViewModel : INotifyPropertyChanged
        {
        #region --- 字段与属性 ---

        private readonly IWindowService _windowService; // 新增字段
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
        public ICommand SaveCommand { get; }
        public ICommand ApplyCommand { get; }
        public ICommand CancelCommand { get; }

        #endregion

        #region --- 构造函数 ---

        // 设计时构造函数 (保持不变)
        public ModelManagementViewModel()
            {
            ProfileName = "示例API配置";
            Models = new ObservableCollection<ModelViewModel>
            {
                new ModelViewModel { Index = 1, Name = "gemini-1.5-pro-latest", IsFavorite = true },
                new ModelViewModel { Index = 2, Name = "gpt-4o" }
            };
            }

        // 运行时构造函数 (修改)
        public ModelManagementViewModel(string profileName, List<string> allModels, List<string> favoriteModels, IWindowService windowService)
            {
            ProfileName = profileName;
            _windowService = windowService; // 保存服务实例

            // 初始化命令 (部分新增)
            SetFavoriteCommand = new RelayCommand(OnSetFavorite, p => SelectedModel != null && !SelectedModel.IsFavorite);
            UnsetFavoriteCommand = new RelayCommand(UnsetFavorite, p => SelectedModel != null && SelectedModel.IsFavorite);
            SaveCommand = new RelayCommand(ExecuteSave);
            ApplyCommand = new RelayCommand(ExecuteApply, p => SelectedModel != null);
            CancelCommand = new RelayCommand(ExecuteCancel);


            var initialModels = allModels.Select(modelName => new ModelViewModel
                {
                Name = modelName,
                IsFavorite = favoriteModels?.Contains(modelName) ?? false
                }).ToList();

            Models = new ObservableCollection<ModelViewModel>(initialModels);
            SortAndRenumberModels();

            Models.CollectionChanged += (s, e) =>
            {
                MarkAsDirty();
                if (e.NewItems != null)
                    {
                    foreach (ModelViewModel item in e.NewItems) { item.PropertyChanged += OnModelPropertyChanged; }
                    }
                if (e.OldItems != null)
                    {
                    foreach (ModelViewModel item in e.OldItems) { item.PropertyChanged -= OnModelPropertyChanged; }
                    }
                RenumberModels();
            };

            foreach (var model in Models)
                {
                model.PropertyChanged += OnModelPropertyChanged;
                }
            }

        #endregion

        #region --- 命令与事件处理 ---

        private void OnSetFavorite(object parameter)
            {
            if (SelectedModel == null) return;
            SelectedModel.IsFavorite = true;
            SortAndRenumberModels();
            }

        private void UnsetFavorite(object parameter)
            {
            if (SelectedModel == null) return;
            SelectedModel.IsFavorite = false;
            SortAndRenumberModels();
            }

        private void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e)
            {
            MarkAsDirty();
            if (e.PropertyName == nameof(ModelViewModel.IsFavorite))
                {
                SetFavoriteCommand.RaiseCanExecuteChanged();
                UnsetFavoriteCommand.RaiseCanExecuteChanged();
                }
            }

        private async void ExecuteSave(object parameter)
            {
            if (parameter is Window window)
                {
                await _windowService.ShowInformationDialogAsync("操作成功", "模型列表已成功保存！", "好的");
                window.DialogResult = true;
                window.Close();
                }
            }

        private async void ExecuteApply(object parameter)
            {
            if (parameter is Window window)
                {
                if (SelectedModel == null)
                    {
                    await _windowService.ShowInformationDialogAsync("提示", "请先在列表中选择一个模型，然后再点击应用。", "好的");
                    return;
                    }
                window.DialogResult = true;
                window.Close();
                }
            }

        private void ExecuteCancel(object parameter)
            {
            if (parameter is Window window)
                {
                window.DialogResult = false;
                window.Close();
                }
            }

        // 新增方法：处理窗口关闭请求
        public async Task<bool?> RequestCloseAsync()
            {
            if (IsDirty)
                {
                var result = await _windowService.ShowConfirmationDialogAsync(
                    "确认保存",
                    "模型列表已修改，是否在关闭前保存？",
                    "是",
                    "否"
                );

                if (result == Wpf.Ui.Controls.MessageBoxResult.Primary) // 用户点击 "是"
                    {
                    return true; // 代表需要保存并关闭
                    }
                else if (result == Wpf.Ui.Controls.MessageBoxResult.Secondary) // 用户点击 "否"
                    {
                    return false; // 代表不保存并关闭
                    }
                else // 用户点击 "取消" 或关闭对话框
                    {
                    return null; // 代表取消关闭操作
                    }
                }

            // 如果没有修改，则直接允许关闭（不保存）
            return false;
            }


        #endregion

        #region --- 辅助方法 ---

        // (此区域所有方法保持不变)
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

        // (此区域所有方法保持不变)
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