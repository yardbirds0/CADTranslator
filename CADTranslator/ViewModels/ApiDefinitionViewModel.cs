// 文件路径: CADTranslator/UI/ViewModels/ApiDefinitionViewModel.cs

using CADTranslator.Models;
using CADTranslator.Models.API;
using CADTranslator.Services.UI;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace CADTranslator.ViewModels
    {
    public class ApiDefinitionViewModel : INotifyPropertyChanged
        {
        private ApiDefinition _apiDefinition;
        public ApiDefinition ApiDef => _apiDefinition;


        private readonly IWindowService _windowService;

        // 【修改】更新构造函数以接收 IWindowService
        public ApiDefinitionViewModel(ApiDefinition apiDefinition = null, IWindowService windowService = null)
            {
            _apiDefinition = apiDefinition ?? new ApiDefinition();
            _windowService = windowService; // 保存服务实例

            // 初始化命令
            SaveCommand = new RelayCommand(ExecuteSave, CanExecuteSave);
            CancelCommand = new RelayCommand(ExecuteCancel);
            }

        #region --- 绑定属性 ---

        public string WindowTitle => _apiDefinition.Id == System.Guid.Empty ? "新增API配置" : "编辑API配置";

        public string DisplayName
            {
            get => _apiDefinition.DisplayName;
            set
                {
                if (_apiDefinition.DisplayName != value)
                    {
                    _apiDefinition.DisplayName = value;
                    OnPropertyChanged();
                    // 当DisplayName变化时，通知SaveCommand重新评估其可用性
                    SaveCommand.RaiseCanExecuteChanged();
                    }
                }
            }

        public string ApiDocumentationUrl
            {
            get => _apiDefinition.ApiDocumentationUrl;
            set { if (_apiDefinition.ApiDocumentationUrl != value) { _apiDefinition.ApiDocumentationUrl = value; OnPropertyChanged(); } }
            }

        public string ApiEndpoint
            {
            get => _apiDefinition.ApiEndpoint;
            set { if (_apiDefinition.ApiEndpoint != value) { _apiDefinition.ApiEndpoint = value; OnPropertyChanged(); } }
            }

        public string ApiKey
            {
            get => _apiDefinition.ApiKey;
            set { if (_apiDefinition.ApiKey != value) { _apiDefinition.ApiKey = value; OnPropertyChanged(); } }
            }

        public string UserId
            {
            get => _apiDefinition.UserId;
            set { if (_apiDefinition.UserId != value) { _apiDefinition.UserId = value; OnPropertyChanged(); } }
            }

        // --- 能力声明绑定 ---
        public bool IsApiKeyRequired
            {
            get => _apiDefinition.IsApiKeyRequired;
            set { if (_apiDefinition.IsApiKeyRequired != value) { _apiDefinition.IsApiKeyRequired = value; OnPropertyChanged(); } }
            }
        public bool IsUserIdRequired
            {
            get => _apiDefinition.IsUserIdRequired;
            set { if (_apiDefinition.IsUserIdRequired != value) { _apiDefinition.IsUserIdRequired = value; OnPropertyChanged(); } }
            }
        public bool IsApiUrlRequired
            {
            get => _apiDefinition.IsApiUrlRequired;
            set { if (_apiDefinition.IsApiUrlRequired != value) { _apiDefinition.IsApiUrlRequired = value; OnPropertyChanged(); } }
            }
        public bool IsModelRequired
            {
            get => _apiDefinition.IsModelRequired;
            set { if (_apiDefinition.IsModelRequired != value) { _apiDefinition.IsModelRequired = value; OnPropertyChanged(); } }
            }
        public bool IsPromptSupported
            {
            get => _apiDefinition.IsPromptSupported;
            set { if (_apiDefinition.IsPromptSupported != value) { _apiDefinition.IsPromptSupported = value; OnPropertyChanged(); } }
            }

        #endregion

        #region --- 命令 ---

        public RelayCommand SaveCommand { get; }
        public ICommand CancelCommand { get; }

        private bool CanExecuteSave(object parameter)
            {
            // 只有当DisplayName不为空时，保存按钮才可用
            return !string.IsNullOrWhiteSpace(DisplayName);
            }

        private void ExecuteSave(object parameter)
            {
            // 这是原来 SaveButton_Click 中的逻辑
            // 注意：现在我们通过 IWindowService 来关闭窗口
            if (parameter is Window window)
                {
                window.DialogResult = true;
                window.Close();
                }
            }

        private void ExecuteCancel(object parameter)
            {
            // 这是原来 CancelButton_Click 中的逻辑
            if (parameter is Window window)
                {
                window.DialogResult = false;
                window.Close();
                }
            }

        #endregion


        #region --- INotifyPropertyChanged 实现 ---
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        #endregion
        }
    }