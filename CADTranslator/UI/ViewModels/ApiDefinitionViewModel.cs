// 文件路径: CADTranslator/UI/ViewModels/ApiDefinitionViewModel.cs

using CADTranslator.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CADTranslator.UI.ViewModels
    {
    public class ApiDefinitionViewModel : INotifyPropertyChanged
        {
        private ApiDefinition _apiDefinition;
        public ApiDefinition ApiDef => _apiDefinition;

        public ApiDefinitionViewModel(ApiDefinition apiDefinition = null)
            {
            // 如果传入了现有的definition，则编辑它；否则，创建一个全新的。
            _apiDefinition = apiDefinition ?? new ApiDefinition();
            }

        #region --- 绑定属性 ---

        public string WindowTitle => _apiDefinition.Id == System.Guid.Empty ? "新增API配置" : "编辑API配置";

        public string DisplayName
            {
            get => _apiDefinition.DisplayName;
            set { if (_apiDefinition.DisplayName != value) { _apiDefinition.DisplayName = value; OnPropertyChanged(); } }
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

        #region --- INotifyPropertyChanged 实现 ---
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        #endregion
        }
    }