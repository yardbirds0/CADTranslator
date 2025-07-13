using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using CADTranslator.Models;
using CADTranslator.Services;
using CADTranslator.UI.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;


namespace CADTranslator.UI.ViewModels
    {
    public class TranslatorViewModel : INotifyPropertyChanged
        {
        // ▼▼▼ 在类的最开始（字段区域的上方或下方）添加这些代码 ▼▼▼
        #region --- Win32 API 辅助 ---
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private void SwitchToAutoCad()
            {
            try
                {
                SetForegroundWindow(Autodesk.AutoCAD.ApplicationServices.Application.MainWindow.Handle);
                }
            catch { /* 忽略可能的错误 */ }
            }
        #endregion




        #region --- 字段 (Fields) ---

        // 服务
        private readonly SettingsService _settingsService;
        private readonly CadTextService _cadTextService;
        private readonly CadLayoutService _cadLayoutService;
        private readonly Window _ownerWindow;

        //API辅助
        private string _statusMessage;
        public string StatusMessage
            {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
            }


        // UI辅助
        private readonly Brush[] _characterBrushes = new Brush[]
        {
            (Brush)new BrushConverter().ConvertFromString("#1E88E5"),
            (Brush)new BrushConverter().ConvertFromString("#0CA678"),
            (Brush)new BrushConverter().ConvertFromString("#FF8F00"),
            (Brush)new BrushConverter().ConvertFromString("#FF5252"),
            (Brush)new BrushConverter().ConvertFromString("#6741D9")
        };
        private int _brushIndex = 0;
        private readonly Dictionary<ApiServiceType, ApiServiceConfig> _apiServiceConfigs = new Dictionary<ApiServiceType, ApiServiceConfig>
            {
            // 这里的配置严格按照您的参数列表来定义
            [ApiServiceType.Baidu] = new ApiServiceConfig(requiresUserId: true, requiresApiKey: true, requiresModelList: false, requiresApiUrl: false),
            [ApiServiceType.Gemini] = new ApiServiceConfig(requiresUserId: false, requiresApiKey: true, requiresModelList: true, requiresApiUrl: false),
            [ApiServiceType.OpenAI] = new ApiServiceConfig(requiresUserId: false, requiresApiKey: true, requiresModelList: true, requiresApiUrl: false),
            [ApiServiceType.Custom] = new ApiServiceConfig(requiresUserId: false, requiresApiKey: true, requiresModelList: true, requiresApiUrl: true)
            };

        #endregion

        #region --- 绑定属性 (Properties for Binding) ---

        // UI状态属性
        private bool _isBusy;
        public bool IsBusy
            {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsUiEnabled)); }
            }
        public bool IsUiEnabled => !IsBusy;

        // 主数据列表
        public ObservableCollection<TextBlockViewModel> TextBlockList { get; set; }

        // --- API配置相关属性 ---
        public ObservableCollection<ApiProfile> ApiProfiles { get; set; }

        private ApiProfile _currentProfile;
        public ApiProfile CurrentProfile
            {
            get => _currentProfile;
            set
                {
                if (_currentProfile != value)
                    {
                    // 在切换配置文件前，为旧的配置文件添加属性变更监听
                    if (_currentProfile != null)
                        {
                        _currentProfile.PropertyChanged -= OnCurrentProfilePropertyChanged;
                        }

                    _currentProfile = value;

                    // 为新的配置文件添加属性变更监听
                    if (_currentProfile != null)
                        {
                        _currentProfile.PropertyChanged += OnCurrentProfilePropertyChanged;
                        }

                    OnPropertyChanged();
                    UpdateUiFromCurrentProfile();
                    }
                }
            }

        private ApiServiceType _selectedApiService;
        public ApiServiceType SelectedApiService
            {
            get => _selectedApiService;
            set
                {
                if (_selectedApiService != value)
                    {
                    _selectedApiService = value;
                    OnPropertyChanged();
                    UpdateApiControlStates();
                    }
                }
            }

        public ObservableCollection<string> ModelList { get; set; }
        public string CurrentModelInput { get; set; }

        // --- 语言和Prompt属性 ---
        public IEnumerable<ApiServiceType> ApiServiceOptions => Enum.GetValues(typeof(ApiServiceType)).Cast<ApiServiceType>();

        public List<LanguageItem> SupportedLanguages { get; } = new List<LanguageItem>
{
    new LanguageItem { DisplayName = "自动检测", Value = "auto" },
    new LanguageItem { DisplayName = "中文", Value = "zh" },
    new LanguageItem { DisplayName = "英文", Value = "en" },
    new LanguageItem { DisplayName = "日语", Value = "ja" },
    new LanguageItem { DisplayName = "韩语", Value = "ko" },
    new LanguageItem { DisplayName = "法语", Value = "fr" },
    new LanguageItem { DisplayName = "德语", Value = "de" },
    new LanguageItem { DisplayName = "俄语", Value = "ru" }
};

        public string SourceLanguage { get; set; } = "auto";
        public string TargetLanguage { get; set; } = "en";
        public string GlobalPrompt { get; set; }

        private ApiServiceConfig CurrentServiceConfig => _apiServiceConfigs[SelectedApiService];

        public bool IsUserIdEnabled => CurrentServiceConfig.RequiresUserId;
        public bool IsApiKeyEnabled => CurrentServiceConfig.RequiresApiKey;
        public bool IsModelListEnabled => CurrentServiceConfig.RequiresModelList;
        public bool IsApiUrlEnabled => CurrentServiceConfig.RequiresApiUrl;


        #endregion

        #region --- 命令 (Commands) ---
        public ICommand SelectTextCommand { get; }
        public ICommand TranslateCommand { get; }
        public ICommand ApplyToCadCommand { get; }
        public ICommand MergeCommand { get; }
        public ICommand SplitCommand { get; }
        public ICommand AddCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand GetModelsCommand { get; }
        public ICommand AddDefaultModelCommand { get; }
        public ICommand ManageModelsCommand { get; } // 将 AddDefaultModelCommand 重命名

        #endregion

        #region --- 构造函数 (Constructor) ---

        public TranslatorViewModel(Window owner)
            {
            // 初始化服务
            _ownerWindow = owner;
            _settingsService = new SettingsService();
            _cadTextService = new CadTextService(Application.DocumentManager.MdiActiveDocument);
            _cadLayoutService = new CadLayoutService(Application.DocumentManager.MdiActiveDocument); // 传入Doc

            // 初始化集合
            TextBlockList = new ObservableCollection<TextBlockViewModel>();
            ModelList = new ObservableCollection<string>();
            ApiProfiles = new ObservableCollection<ApiProfile>();

            // 绑定命令
            SelectTextCommand = new RelayCommand(OnSelectText);
            TranslateCommand = new RelayCommand(OnTranslate, p => TextBlockList.Any() && !IsBusy);
            ApplyToCadCommand = new RelayCommand(OnApplyToCad, p => TextBlockList.Any(i => !string.IsNullOrWhiteSpace(i.TranslatedText)));
            MergeCommand = new RelayCommand(OnMerge, p => p is IList<object> list && list.Count > 1);
            DeleteCommand = new RelayCommand(OnDelete, p => p is IList<object> list && list.Count > 0);
            SplitCommand = new RelayCommand(OnSplit, p => p is TextBlockViewModel);
            AddCommand = new RelayCommand(OnAdd);
            EditCommand = new RelayCommand(OnEdit, p => p is TextBlockViewModel);
            ManageModelsCommand = new RelayCommand(OnManageModels, p => IsModelListEnabled); // 绑定到新方法

            // 启动时加载设置
            LoadSettings();
            }

        #endregion

        #region --- 设置管理 (Settings Management) ---

        private void LoadSettings()
            {
            var profiles = _settingsService.LoadApiProfiles();
            ApiProfiles.Clear();
            foreach (var profile in profiles)
                {
                ApiProfiles.Add(profile);
                }
            // 简单处理：总是加载第一个配置作为当前配置
            CurrentProfile = ApiProfiles.FirstOrDefault() ?? new ApiProfile { ProfileName = "百度翻译 (默认)", ServiceType = ApiServiceType.Baidu };
            }

        private void SaveSettings(object parameter = null)
            {
            // 将当前UI上的选择保存回CurrentProfile，以防用户切换了服务类型
            if (CurrentProfile != null)
                {
                CurrentProfile.ServiceType = this.SelectedApiService;
                }
            _settingsService.SaveApiProfiles(ApiProfiles.ToList());
            }

        private void UpdateUiFromCurrentProfile()
            {
            if (CurrentProfile == null) return;

            SelectedApiService = CurrentProfile.ServiceType;
            ModelList.Clear();
            if (CurrentProfile.Models != null)
                {
                foreach (var model in CurrentProfile.Models)
                    {
                    ModelList.Add(model);
                    }
                }
            OnPropertyChanged(nameof(SourceLanguage));
            OnPropertyChanged(nameof(TargetLanguage));
            OnPropertyChanged(nameof(GlobalPrompt));
            CurrentModelInput = CurrentProfile.LastSelectedModel;
            OnPropertyChanged(nameof(CurrentModelInput)); // 通知UI更新
            }

        private void UpdateApiControlStates()
            {
            if (CurrentProfile == null) return;

            // 如果选择的是百度翻译，并且当前配置是新建的或空的，则填入默认值
            if (SelectedApiService == ApiServiceType.Baidu)
                {
                if (string.IsNullOrWhiteSpace(CurrentProfile.UserId))
                    {
                    CurrentProfile.UserId = "20250708002400901"; // 默认App ID
                    }
                if (string.IsNullOrWhiteSpace(CurrentProfile.ApiKey))
                    {
                    CurrentProfile.ApiKey = "1L_Bso6ORO8torYgecjh"; // 默认App Key
                    }
                }

            // 将UI上的服务类型选择同步到当前配置文件
            CurrentProfile.ServiceType = SelectedApiService;

            // 更新模型列表
            ModelList.Clear();
            if (CurrentProfile.Models != null)
                {
                foreach (var model in CurrentProfile.Models)
                    {
                    ModelList.Add(model);
                    }
                }

            // 触发所有相关属性的UI更新
            OnPropertyChanged(nameof(CurrentProfile));
            OnPropertyChanged(nameof(IsUserIdEnabled));
            OnPropertyChanged(nameof(IsApiKeyEnabled));
            OnPropertyChanged(nameof(IsModelListEnabled));
            OnPropertyChanged(nameof(IsApiUrlEnabled)); 

            // 自动保存
            SaveSettings();
            }

        private void OnCurrentProfilePropertyChanged(object sender, PropertyChangedEventArgs e)
            {
            // 当Profile的任何属性（如UserId, ApiKey等）改变时，自动保存
            SaveSettings();
            }
        #endregion

        #region --- 命令实现 (Command Implementations) ---

        // ▼▼▼ 请用这个最终修正版，完整替换现有的 OnSelectText 方法 ▼▼▼
        private void OnSelectText(object parameter)
            {
            // 1. 将参数转换为WPF窗口，这里的 'Window' 是 'System.Windows.Window'
            if (!(parameter is System.Windows.Window mainWindow)) return;

            try
                {
                // 2. 这里的 'Application' 是指 'Autodesk.AutoCAD.ApplicationServices.Application' (由文件顶部的using别名定义)
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null)
                    {
                    System.Windows.MessageBox.Show("未找到活动的CAD文档。");
                    return;
                    }

                // 3. 直接操作我们从参数中获取的确切的WPF窗口实例
                mainWindow.Hide();

                var ed = doc.Editor;
                var selRes = ed.GetSelection();

                // 4. 再次操作WPF窗口实例，使其显示和激活
                mainWindow.Show();
                mainWindow.Activate();

                if (selRes.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK) return;

                List<TextBlockViewModel> textBlocks = _cadTextService.ExtractAndMergeText(selRes.Value);
                if (textBlocks.Count == 0)
                    {
                    MessageBox.Show("您选择的对象中未找到任何有效文字。");
                    return;
                    }

                LoadTextBlocks(textBlocks);
                }
            catch (System.Exception ex)
                {
                MessageBox.Show($"提取文字时出错: {ex.Message}");
                }
            finally
                {
                // 最终保障：确保窗口在任何情况下都能显示回来
                if (!mainWindow.IsVisible)
                    {
                    mainWindow.Show();
                    mainWindow.Activate();
                    }
                }
            }

        private async void OnTranslate(object parameter)
            {
            if (IsModelListEnabled && !string.IsNullOrWhiteSpace(CurrentModelInput))
                {
                // 优先使用用户实时输入的模型文本
                CurrentProfile.LastSelectedModel = CurrentModelInput;

                // 如果这个模型是全新的，则自动添加到当前配置的模型列表中
                if (!ModelList.Contains(CurrentModelInput))
                    {
                    ModelList.Add(CurrentModelInput);
                    CurrentProfile.Models.Add(CurrentModelInput);
                    }
                }
            if (parameter is PasswordBox passwordBox)
                {
                CurrentProfile.ApiKey = passwordBox.Password;
                }
            if (CurrentProfile == null)
                {
                MessageBox.Show("请先完成API配置。");
                return;
                }

            IsBusy = true;

            ITranslator translator;
            // 根据当前选择的服务类型，创建对应的翻译器实例
            switch (SelectedApiService)
                {
                case ApiServiceType.Baidu:
                    translator = new BaiduTranslator(CurrentProfile.UserId, CurrentProfile.ApiKey);
                    break;
                case ApiServiceType.Gemini:
                    // 修正：将模型传递给构造函数
                    translator = new GeminiTranslator(CurrentProfile.ApiKey, CurrentProfile.LastSelectedModel);
                    break;
                case ApiServiceType.OpenAI:
                    string selectedModel = string.IsNullOrWhiteSpace(CurrentProfile.LastSelectedModel) ? "gpt-4o" : CurrentProfile.LastSelectedModel;
                    translator = new OpenAiTranslator(CurrentProfile.ApiKey, selectedModel);
                    break;
                case ApiServiceType.Custom:
                    // 修正：将模型传递给构造函数
                    translator = new CustomTranslator(CurrentProfile.ApiEndpoint, CurrentProfile.ApiKey, CurrentProfile.LastSelectedModel);
                    break;
                default:
                    MessageBox.Show("当前选择的API服务尚未实现。");
                    IsBusy = false;
                    return;
                }

            try
                {
                foreach (var item in TextBlockList)
                    {
                    if (string.IsNullOrWhiteSpace(item.OriginalText) || !string.IsNullOrWhiteSpace(item.TranslatedText)) continue;

                    string textToTranslate = string.IsNullOrWhiteSpace(GlobalPrompt)
                        ? item.OriginalText
                        : $"{GlobalPrompt}\n\n{item.OriginalText}";

                    item.TranslatedText = await translator.TranslateAsync(textToTranslate, SourceLanguage, TargetLanguage);
                    }
                }
            catch (System.Exception ex) { MessageBox.Show($"翻译过程中出错: {ex.Message}"); }
            finally { IsBusy = false; }
            }

        private async void OnApplyToCad(object parameter)
            {
            StatusMessage = ""; // 清空旧消息
            SwitchToAutoCad(); // 切换到CAD窗口

            // 将核心操作放到后台线程，防止UI卡顿
            bool success = await Task.Run(() => _cadLayoutService.ApplyTranslationToCad(TextBlockList));

            if (!success)
                {
                StatusMessage = "错误：应用到CAD失败，请检查CAD命令行获取详细信息。";
                }
            }

        private void OnManageModels(object parameter)
            {
            if (CurrentProfile == null)
                {
                MessageBox.Show("请先选择一个API配置。");
                return;
                }

            // 1. 创建视图模型时，传入当前配置的名称和模型列表
            var modelManagementVM = new ModelManagementViewModel(CurrentProfile.ProfileName, CurrentProfile.Models);

            // 2. 创建窗口
            var modelWindow = new ModelManagementWindow(modelManagementVM)
                {
                Owner = _ownerWindow
                };

            // 3. 显示对话框并处理结果
            if (modelWindow.ShowDialog() == true)
                {
                var finalModels = modelManagementVM.GetFinalModels();

                // 更新当前API配置的模型列表
                CurrentProfile.Models.Clear();
                finalModels.ForEach(m => CurrentProfile.Models.Add(m));

                // 更新UI上的下拉列表
                ModelList.Clear();
                finalModels.ForEach(m => ModelList.Add(m));

                // 检查并更新当前选中的模型
                if (!string.IsNullOrWhiteSpace(CurrentModelInput) && !finalModels.Contains(CurrentModelInput))
                    {
                    CurrentModelInput = finalModels.FirstOrDefault();
                    OnPropertyChanged(nameof(CurrentModelInput));
                    }

                // 保存所有配置到文件
                SaveSettings();
                StatusMessage = $"配置 '{CurrentProfile.ProfileName}' 的模型列表已成功保存！";
                }
            }

        // --- 表格操作命令 ---
        private void OnMerge(object selectedItems)
            {
            var selectedViewModels = (selectedItems as IList<object>)?.Cast<TextBlockViewModel>().OrderBy(i => TextBlockList.IndexOf(i)).ToList();
            if (selectedViewModels == null || selectedViewModels.Count <= 1) return;

            var firstItem = selectedViewModels.First();
            var mergedItem = new TextBlockViewModel
                {
                OriginalText = string.Join("\n", selectedViewModels.Select(i => i.OriginalText)),
                TranslatedText = string.Join("\n", selectedViewModels.Select(i => i.TranslatedText)),
                SourceObjectIds = selectedViewModels.SelectMany(i => i.SourceObjectIds).ToList(),
                Character = firstItem.Character,
                BgColor = firstItem.BgColor
                };

            int firstIndex = TextBlockList.IndexOf(firstItem);
            foreach (var item in selectedViewModels.AsEnumerable().Reverse())
                {
                TextBlockList.Remove(item);
                }
            TextBlockList.Insert(firstIndex, mergedItem);
            RenumberItems();
            }

        private void OnDelete(object selectedItems)
            {
            var itemsToDelete = (selectedItems as IList<object>)?.Cast<TextBlockViewModel>().ToList();
            if (itemsToDelete == null || itemsToDelete.Count == 0) return;

            if (MessageBox.Show($"确定要删除选中的 {itemsToDelete.Count} 行吗？", "确认删除", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                foreach (var item in itemsToDelete)
                    {
                    TextBlockList.Remove(item);
                    }
                RenumberItems();
                }
            }

        private void OnSplit(object selectedItem)
            {
            if (!(selectedItem is TextBlockViewModel selectedVM)) return;
            var lines = selectedVM.OriginalText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length <= 1) { MessageBox.Show("当前行不包含可供拆分的多行文本。"); return; }

            selectedVM.OriginalText = lines[0];
            selectedVM.TranslatedText = "";

            int insertIndex = TextBlockList.IndexOf(selectedVM) + 1;
            for (int i = 1; i < lines.Length; i++)
                {
                var newItem = new TextBlockViewModel
                    {
                    OriginalText = lines[i],
                    Character = string.IsNullOrWhiteSpace(lines[i]) ? "?" : lines[i].Substring(0, 1).ToUpper(),
                    BgColor = _characterBrushes[_brushIndex++ % _characterBrushes.Length]
                    };
                TextBlockList.Insert(insertIndex++, newItem);
                }
            RenumberItems();
            }

        private void OnAdd(object parameter)
            {
            int insertIndex = TextBlockList.Count;
            var newItem = new TextBlockViewModel
                {
                OriginalText = "[请双击此处以编辑原文]",
                Character = "N",
                BgColor = _characterBrushes[_brushIndex++ % _characterBrushes.Length]
                };
            TextBlockList.Insert(insertIndex, newItem);
            RenumberItems();
            }

        private void OnEdit(object selectedItem)
            {
            if (!(selectedItem is TextBlockViewModel selectedVM)) return;
            if (selectedVM.SourceObjectIds != null && selectedVM.SourceObjectIds.Any())
                {
                MessageBox.Show("不能直接编辑从CAD提取的文本。");
                return;
                }

            var editWindow = new EditWindow(selectedVM.OriginalText);
            if (editWindow.ShowDialog() == true)
                {
                selectedVM.OriginalText = editWindow.EditedText;
                selectedVM.Character = string.IsNullOrWhiteSpace(selectedVM.OriginalText) ? "?" : selectedVM.OriginalText.Substring(0, 1).ToUpper();
                selectedVM.TranslatedText = "";
                }
            }

        #endregion

        #region --- 私有辅助方法 (Private Helper Methods) ---

        public void LoadTextBlocks(List<TextBlockViewModel> blocks)
            {
            TextBlockList.Clear();
            _brushIndex = 0;
            blocks.ForEach(b =>
            {
                b.Character = string.IsNullOrWhiteSpace(b.OriginalText) ? "?" : b.OriginalText.Substring(0, 1).ToUpper();
                b.BgColor = _characterBrushes[_brushIndex++ % _characterBrushes.Length];
                TextBlockList.Add(b);
            });
            RenumberItems();
            }

        private void RenumberItems()
            {
            for (int i = 0; i < TextBlockList.Count; i++)
                {
                TextBlockList[i].Id = i + 1;
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