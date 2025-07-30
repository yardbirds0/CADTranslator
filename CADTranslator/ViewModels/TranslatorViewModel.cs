using CADTranslator.Models.API;
using CADTranslator.Models.CAD;
using CADTranslator.Models.UI;
using CADTranslator.Services.CAD;
using CADTranslator.Services.Settings;
using CADTranslator.Services.Tokenization;
using CADTranslator.Services.Translation;
using CADTranslator.Services.UI;
using CADTranslator.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Wpf.Ui.Controls;
using static CADTranslator.Services.Settings.SettingsService;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace CADTranslator.ViewModels
    {
    public class TranslatorViewModel : INotifyPropertyChanged
        {
        #region --- 字段与服务 ---

        // (服务和大部分字段保持不变)
        private readonly IWindowService _windowService;
        private readonly ISettingsService _settingsService;
        private readonly IAdvancedTextService _advancedTextService;
        private readonly ICadLayoutService _cadLayoutService;
        private readonly ApiRegistry _apiRegistry;
        private AppSettings _currentSettings;
        private bool _isLoading = false;
        private List<TextBlockViewModel> _failedItems = new List<TextBlockViewModel>();
        private bool _isProgressIndeterminate = true;
        private int _totalTokens = 0;
        private bool _isTokenCountAvailable = false;
        private CancellationTokenSource _translationCts;
        private readonly ITokenizationService _tokenizationService;
        // 【新增】用于控制UI状态的字段
        private bool _isTranslating = false;



        // 【新增】用于管理背景色的画刷
        private readonly Brush _translatingBrush = new SolidColorBrush(Color.FromArgb(128, 255, 236, 179)); // 淡黄色
        private readonly Brush _successBrush = new SolidColorBrush(Color.FromArgb(128, 200, 230, 201));     // 淡绿色
        private readonly Brush _failedBrush = new SolidColorBrush(Color.FromArgb(128, 255, 205, 210));      // 淡红色
        private readonly Brush _defaultBrush = Brushes.Transparent;

        // (其他私有字段保持不变)
        private bool _isBusy;
        private ITranslator _currentProvider;
        private string _lastBalanceDisplay = "当前无余额记录";
        private int _progressValue;
        private string _progressText;
        private string _currentLineSpacingInput;
        private bool _isLiveLayoutEnabled;
        private bool _isMultiThreadingEnabled;
        private string _currentConcurrencyLevelInput;
        private readonly Brush[] _characterBrushes = new Brush[]
        {
            (Brush)new BrushConverter().ConvertFromString("#1E88E5"), (Brush)new BrushConverter().ConvertFromString("#0CA678"),
            (Brush)new BrushConverter().ConvertFromString("#FF8F00"), (Brush)new BrushConverter().ConvertFromString("#FF5252"),
            (Brush)new BrushConverter().ConvertFromString("#6741D9")
        };
        private int _brushIndex = 0;
        private string _customPrompt; // 用于保存“自定义”模板的内容
        public Dictionary<PromptTemplateType, string> PromptTemplateDisplayNames => PromptTemplateManager.DisplayNames;

        public List<PromptTemplateType> PromptTemplateOptions { get; } = Enum.GetValues(typeof(PromptTemplateType)).Cast<PromptTemplateType>().ToList();

        public PromptTemplateType SelectedPromptTemplate
            {
            get => _currentSettings.SelectedPromptTemplate;
            set
                {
                if (_currentSettings.SelectedPromptTemplate != value)
                    {
                    _currentSettings.SelectedPromptTemplate = value;
                    OnPropertyChanged();
                    UpdateDisplayedPrompt();
                    OnPropertyChanged(nameof(IsPromptBoxReadOnly));
                    if (!_isLoading) SaveSettings();
                    }
                }
            }

        private string _displayedPrompt;
        public string DisplayedPrompt
            {
            get => _displayedPrompt;
            set
                {
                if (SetField(ref _displayedPrompt, value))
                    {
                    if (SelectedPromptTemplate == PromptTemplateType.Custom)
                        {
                        _customPrompt = value;
                        }
                    OnPropertyChanged(nameof(FormattedDisplayedPrompt));
                    }
                }
            }


        public string FormattedDisplayedPrompt
            {
            get
                {
                if (string.IsNullOrEmpty(DisplayedPrompt))
                    return string.Empty;

                var fromLang = SupportedLanguages.FirstOrDefault(l => l.Value == SourceLanguage)?.DisplayName ?? SourceLanguage;
                var toLang = SupportedLanguages.FirstOrDefault(l => l.Value == TargetLanguage)?.DisplayName ?? TargetLanguage;

                return DisplayedPrompt
                    .Replace("{fromLanguage}", $"[{fromLang}]")
                    .Replace("{toLanguage}", $"[{toLang}]");
                }
            }
        public bool IsPromptBoxReadOnly => SelectedPromptTemplate != PromptTemplateType.Custom;

        public PromptSendingMode SendingMode
            {
            get => _currentSettings.SendingMode;
            set
                {
                if (_currentSettings.SendingMode != value)
                    {
                    _currentSettings.SendingMode = value;
                    OnPropertyChanged();
                    if (!_isLoading) SaveSettings();
                    }
                }
            }
        #endregion

        #region --- 绑定属性 ---
        public bool IsBusy
            {
            get => _isBusy;
            set
                {
                if (SetField(ref _isBusy, value))
                    {
                    OnPropertyChanged(nameof(IsUiEnabled));
                    TranslateCommand.RaiseCanExecuteChanged();
                    CancelTranslationCommand.RaiseCanExecuteChanged();
                    RetranslateFailedCommand.RaiseCanExecuteChanged();
                    SelectTextCommand.RaiseCanExecuteChanged();
                    }
                }
            }

        public bool IsTranslating
            {
            get => _isTranslating;
            set
                {
                if (SetField(ref _isTranslating, value))
                    {
                    // 当翻译状态变化时，通知相关命令刷新其可用状态
                    TranslateCommand.RaiseCanExecuteChanged();
                    CancelTranslationCommand.RaiseCanExecuteChanged();
                    }
                }
            }

        public bool IsUiEnabled => !IsBusy;
        public bool IsCharLimitVisible => CurrentProvider?.UnitType == BillingUnit.Character;
        public bool IsTokenLimitVisible => CurrentProvider?.UnitType == BillingUnit.Token;

        public ObservableCollection<int> CharLimitOptions { get; } = new ObservableCollection<int> { 100, 500, 1000, 2000, 3000 };
        public ObservableCollection<int> TokenLimitOptions { get; } = new ObservableCollection<int> { 1000, 2000, 4000, 8000, 10000 };

        public int MaxCharsPerBatch
            {
            get => _currentSettings.MaxCharsPerBatch;
            set
                {
                // 输入验证与范围限制
                int clampedValue = Math.Max(100, Math.Min(3000, value));
                if (_currentSettings.MaxCharsPerBatch != clampedValue)
                    {
                    _currentSettings.MaxCharsPerBatch = clampedValue;
                    OnPropertyChanged();
                    if (!_isLoading) SaveSettings();
                    }
                }
            }

        public int MaxTokensPerBatch
            {
            get => _currentSettings.MaxTokensPerBatch;
            set
                {
                // 输入验证与范围限制
                int clampedValue = Math.Max(1000, Math.Min(10000, value));
                if (_currentSettings.MaxTokensPerBatch != clampedValue)
                    {
                    _currentSettings.MaxTokensPerBatch = clampedValue;
                    OnPropertyChanged();
                    if (!_isLoading) SaveSettings();
                    }
                }
            }

        public ITranslator CurrentProvider
            {
            get => _currentProvider;
            set
                {
                if (SetField(ref _currentProvider, value))
                    {
                    CurrentProfile = ApiProfiles.FirstOrDefault(p => p.ServiceType == _currentProvider.ServiceType);
                    if (CurrentProfile == null)
                        {
                        CurrentProfile = new ApiProfile { ServiceType = _currentProvider.ServiceType, ProfileName = $"{_currentProvider.ServiceType} Profile" };
                        ApiProfiles.Add(CurrentProfile);
                        }
                    UpdateUiFromCurrentProfile();
                    OnPropertyChanged(nameof(IsCharLimitVisible));
                    OnPropertyChanged(nameof(IsTokenLimitVisible));
                    OnPropertyChanged(nameof(IsUserIdEnabled));
                    OnPropertyChanged(nameof(IsApiKeyEnabled));
                    OnPropertyChanged(nameof(IsModelRequired));
                    OnPropertyChanged(nameof(IsPromptSupported));
                    OnPropertyChanged(nameof(IsModelListEnabled));
                    OnPropertyChanged(nameof(IsApiUrlEnabled));
                    OnPropertyChanged(nameof(IsBalanceFeatureEnabled));

                    GetModelsCommand.RaiseCanExecuteChanged();
                    GetBalanceCommand.RaiseCanExecuteChanged();
                    ViewHistoryCommand.RaiseCanExecuteChanged();
                    ManageModelsCommand.RaiseCanExecuteChanged();
                    ViewApiDocumentationCommand.RaiseCanExecuteChanged();

                    UpdateBalanceDisplayForCurrentProvider();

                    if (!_isLoading)
                        {
                        _currentSettings.LastSelectedApiService = _currentProvider.ServiceType;
                        SaveSettings();
                        }
                    }
                }
            }

        public bool IsUserIdEnabled => CurrentProvider?.IsUserIdRequired ?? false;
        public bool IsApiKeyEnabled => CurrentProvider?.IsApiKeyRequired ?? false;
        public bool IsModelRequired => CurrentProvider?.IsModelRequired ?? false;
        public bool IsPromptSupported => CurrentProvider?.IsPromptSupported ?? false;
        public bool IsModelListEnabled => CurrentProvider?.IsModelFetchingSupported ?? false;
        public bool IsApiUrlEnabled => CurrentProvider?.IsApiUrlRequired ?? false;
        public bool IsBalanceFeatureEnabled => CurrentProvider?.IsBalanceCheckSupported ?? false;

        public ObservableCollection<TextBlockViewModel> TextBlockList { get; set; }
        public ObservableCollection<ApiProfile> ApiProfiles { get; set; }
        public List<ITranslator> ApiServiceOptions { get; }

        public ApiProfile CurrentProfile { get; set; }
        public ObservableCollection<ModelViewModel> ModelList { get; set; }
        private ModelViewModel _selectedModel;
        public ModelViewModel SelectedModel
            {
            get => _selectedModel;
            set
                {
                if (SetField(ref _selectedModel, value))
                    {
                    var newModelName = _selectedModel?.Name;
                    CurrentModelInput = newModelName;
                    OnPropertyChanged(nameof(CurrentModelInput));
                    if (CurrentProfile != null)
                        {
                        CurrentProfile.LastSelectedModel = newModelName;
                        }
                    SaveSettings();
                    }
                }
            }
        public string CurrentModelInput { get; set; }
        public string SourceLanguage
            {
            get => _currentSettings?.SourceLanguage ?? "auto";
            set
                {
                if (_currentSettings != null && _currentSettings.SourceLanguage != value)
                    {
                    _currentSettings.SourceLanguage = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FormattedDisplayedPrompt));
                    if (!_isLoading) SaveSettings();
                    }
                }
            }

        public string TargetLanguage
            {
            get => _currentSettings?.TargetLanguage ?? "en";
            set
                {
                if (_currentSettings != null && _currentSettings.TargetLanguage != value)
                    {
                    _currentSettings.TargetLanguage = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FormattedDisplayedPrompt));
                    if (!_isLoading) SaveSettings();
                    }
                }
            }
        public string GlobalPrompt { get; set; }
        public ObservableCollection<string> StatusLog { get; } = new ObservableCollection<string>();
        public int ProgressValue { get => _progressValue; set => SetField(ref _progressValue, value); }
        public string ProgressText { get => _progressText; set => SetField(ref _progressText, value); }
        public string LastBalanceDisplay { get => _lastBalanceDisplay; set => SetField(ref _lastBalanceDisplay, value); }
        public ObservableCollection<BalanceRecord> BalanceHistory { get; set; }
        public List<LanguageItem> SupportedLanguages { get; } = new List<LanguageItem>
        {
            new LanguageItem { DisplayName = "自动检测", Value = "auto" }, new LanguageItem { DisplayName = "中文", Value = "zh" },
            new LanguageItem { DisplayName = "英文", Value = "en" }, new LanguageItem { DisplayName = "日语", Value = "ja" },
            new LanguageItem { DisplayName = "韩语", Value = "ko" }, new LanguageItem { DisplayName = "法语", Value = "fr" },
            new LanguageItem { DisplayName = "德语", Value = "de" }, new LanguageItem { DisplayName = "俄语", Value = "ru" },
            new LanguageItem { DisplayName = "印尼语", Value = "id" },
            new LanguageItem { DisplayName = "越南语", Value = "vie" },
            new LanguageItem { DisplayName = "马来语", Value = "may" },
            new LanguageItem { DisplayName = "泰语", Value = "th" }
        };

        public bool IsLiveLayoutEnabled
            {
            get => _isLiveLayoutEnabled;
            set { if (SetField(ref _isLiveLayoutEnabled, value)) { if (!_isLoading) SaveSettings(); } }
            }
        public bool AddUnderlineAfterSmartLayout
            {
            get => _currentSettings.AddUnderlineAfterSmartLayout;
            set
                {
                if (_currentSettings.AddUnderlineAfterSmartLayout != value)
                    {
                    _currentSettings.AddUnderlineAfterSmartLayout = value;
                    if (!_isLoading) SaveSettings();
                    OnPropertyChanged();
                    }
                }
            }
        public string CurrentLineSpacingInput
            {
            get => _currentLineSpacingInput;
            set
                {
                if (SetField(ref _currentLineSpacingInput, value))
                    {
                    var trimmedValue = value?.Trim();

                    if (!string.IsNullOrWhiteSpace(trimmedValue) &&
                        double.TryParse(trimmedValue, out _) &&
                        !LineSpacingOptions.Contains(trimmedValue))
                        {
                        LineSpacingOptions.Add(trimmedValue);
                        }
                    if (!_isLoading) SaveSettings();
                    }
                }
            }
        public ObservableCollection<string> LineSpacingOptions { get; set; }

        public bool IsMultiThreadingEnabled
            {
            get => _isMultiThreadingEnabled;
            set { if (SetField(ref _isMultiThreadingEnabled, value)) { if (!_isLoading) SaveSettings(); } }
            }

        public double SimilarityThreshold
            {
            get => _currentSettings.ParagraphSimilarityThreshold;
            set
                {
                if (_currentSettings.ParagraphSimilarityThreshold != value)
                    {
                    _currentSettings.ParagraphSimilarityThreshold = value;
                    if (!_isLoading) SaveSettings();
                    OnPropertyChanged();
                    }
                }
            }
        public string CurrentConcurrencyLevelInput
            {
            get => _currentConcurrencyLevelInput;
            set
                {
                if (SetField(ref _currentConcurrencyLevelInput, value))
                    {
                    var trimmedValue = value?.Trim();

                    if (!string.IsNullOrWhiteSpace(trimmedValue) &&
                        int.TryParse(trimmedValue, out int intVal) && intVal > 1 &&
                        !ConcurrencyLevelOptions.Contains(trimmedValue))
                        {
                        ConcurrencyLevelOptions.Add(trimmedValue);
                        }
                    if (!_isLoading) SaveSettings();
                    }
                }
            }
        public ObservableCollection<string> ConcurrencyLevelOptions { get; set; }
        public bool IsProgressIndeterminate
            {
            get => _isProgressIndeterminate;
            set => SetField(ref _isProgressIndeterminate, value);
            }
        public bool IsLanguageSettingsExpanded
            {
            get => _currentSettings.IsLanguageSettingsExpanded;
            set
                {
                if (_currentSettings.IsLanguageSettingsExpanded != value)
                    {
                    _currentSettings.IsLanguageSettingsExpanded = value;
                    if (!_isLoading) SaveSettings();
                    OnPropertyChanged();
                    }
                }
            }
        public bool IsApiSettingsExpanded
            {
            get => _currentSettings.IsApiSettingsExpanded;
            set
                {
                if (_currentSettings.IsApiSettingsExpanded != value)
                    {
                    _currentSettings.IsApiSettingsExpanded = value;
                    if (!_isLoading) SaveSettings();
                    OnPropertyChanged();
                    }
                }
            }
        public bool IsFunctionSettingsExpanded
            {
            get => _currentSettings.IsFunctionSettingsExpanded;
            set
                {
                if (_currentSettings.IsFunctionSettingsExpanded != value)
                    {
                    _currentSettings.IsFunctionSettingsExpanded = value;
                    if (!_isLoading) SaveSettings();
                    OnPropertyChanged();
                    }
                }
            }
        #endregion

        #region --- 命令 ---
        public RelayCommand SelectTextCommand { get; }
        public RelayCommand TranslateCommand { get; }
        public RelayCommand CancelTranslationCommand { get; }
        public RelayCommand ApplyToCadCommand { get; }
        public RelayCommand MergeCommand { get; }
        public RelayCommand SplitCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand EditCommand { get; }
        public RelayCommand ManageModelsCommand { get; }
        public RelayCommand GetModelsCommand { get; }
        public RelayCommand DeleteLineSpacingOptionCommand { get; }
        public RelayCommand GetBalanceCommand { get; }
        public RelayCommand ViewHistoryCommand { get; }
        public RelayCommand ResetCommand { get; }
        public RelayCommand RetranslateFailedCommand { get; }
        public RelayCommand DeleteConcurrencyOptionCommand { get; }
        public RelayCommand ManageApiDefinitionsCommand { get; }
        public RelayCommand ViewApiDocumentationCommand { get; }
        #endregion

        #region --- 构造函数 ---
        public TranslatorViewModel()
            {
            IsBusy = true;
            ProgressValue = 65;
            ProgressText = "65%";
            }

        public TranslatorViewModel(
            IWindowService windowService,
            ISettingsService settingsService,
            IAdvancedTextService advancedTextService,
            ICadLayoutService cadLayoutService,
             ITokenizationService tokenizationService,
            ApiRegistry apiRegistry)
            {
            _windowService = windowService ?? throw new ArgumentNullException(nameof(windowService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _advancedTextService = advancedTextService ?? throw new ArgumentNullException(nameof(advancedTextService));
            _cadLayoutService = cadLayoutService ?? throw new ArgumentNullException(nameof(cadLayoutService));
            _apiRegistry = apiRegistry ?? throw new ArgumentNullException(nameof(apiRegistry));
            _tokenizationService = tokenizationService ?? throw new ArgumentNullException(nameof(tokenizationService));

            TextBlockList = new ObservableCollection<TextBlockViewModel>();
            ModelList = new ObservableCollection<ModelViewModel>();
            ApiProfiles = new ObservableCollection<ApiProfile>();
            LineSpacingOptions = new ObservableCollection<string>();
            BalanceHistory = new ObservableCollection<BalanceRecord>();
            ConcurrencyLevelOptions = new ObservableCollection<string>();

            ApiServiceOptions = _apiRegistry.Providers;

            SelectTextCommand = new RelayCommand(OnSelectText, p => !IsBusy); // 【修改】增加CanExecute
            TranslateCommand = new RelayCommand(OnTranslate, p => TextBlockList.Any() && !IsBusy && !IsTranslating);
            CancelTranslationCommand = new RelayCommand(OnCancelTranslation, p => IsBusy && IsTranslating);
            ApplyToCadCommand = new RelayCommand(OnApplyToCad, p => TextBlockList.Any(i => !string.IsNullOrWhiteSpace(i.TranslatedText) && !i.TranslatedText.StartsWith("[")));
            MergeCommand = new RelayCommand(OnMerge, p => p is IList<object> list && list.Count > 1);
            DeleteCommand = new RelayCommand(OnDelete, p => p is IList<object> list && list.Count > 0);
            SplitCommand = new RelayCommand(OnSplit, p => p is TextBlockViewModel);
            EditCommand = new RelayCommand(OnEdit, p => p is TextBlockViewModel);
            ManageModelsCommand = new RelayCommand(OnManageModels, p => IsModelListEnabled);
            GetModelsCommand = new RelayCommand(OnGetModels, p => IsModelListEnabled && !IsBusy);
            DeleteLineSpacingOptionCommand = new RelayCommand(OnDeleteLineSpacingOption, p => p is string option && option != "不指定");
            GetBalanceCommand = new RelayCommand(OnGetBalance, p => IsBalanceFeatureEnabled && !IsBusy);
            ViewHistoryCommand = new RelayCommand(OnViewHistory, p => IsBalanceFeatureEnabled);
            ResetCommand = new RelayCommand(OnReset);
            RetranslateFailedCommand = new RelayCommand(OnRetranslateFailed, p => _failedItems.Any() && !IsBusy);
            DeleteConcurrencyOptionCommand = new RelayCommand(OnDeleteConcurrencyOption, p => p is string option && option != "2");
            ManageApiDefinitionsCommand = new RelayCommand(OnManageApiDefinitions);
            ViewApiDocumentationCommand = new RelayCommand(p => OnViewApiDocumentation(), p => !string.IsNullOrWhiteSpace(CurrentProvider?.ApiDocumentationUrl));

            LoadSettings();
            _customPrompt = _currentSettings.CustomPrompt;
            UpdateDisplayedPrompt();
            Log("欢迎使用CAD翻译工具箱。");
            }

        #endregion

        #region --- 核心方法 (选择、应用) ---
        private async void OnSelectText(object parameter)
            {
            int extractedCount = 0;

            try
                {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null)
                    {
                    await _windowService.ShowInformationDialogAsync("操作失败", "未找到活动的CAD文档。");
                    return;
                    }

                _windowService.HideMainWindow();
                var ed = doc.Editor;
                var selRes = ed.GetSelection();

                using (doc.LockDocument())
                    {
                    if (selRes.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK)
                        {
                        _windowService.ShowMainWindow();
                        return;
                        }

                    List<ParagraphInfo> paragraphInfos = _advancedTextService.ExtractAndProcessParagraphs(selRes.Value, this.SimilarityThreshold);
                    extractedCount = paragraphInfos.Count;

                    if (paragraphInfos.Count == 0)
                        {
                        Log("在选定对象中未找到任何有效文字。");
                        await _windowService.ShowInformationDialogAsync("提示", "您选择的对象中未找到任何有效文字。");
                        }
                    else
                        {
                        var textBlocks = paragraphInfos.Select(p => new TextBlockViewModel
                            {
                            OriginalText = p.Text,
                            IsTitle = p.IsTitle,
                            GroupKey = p.GroupKey,
                            SourceObjectIds = p.SourceObjectIds,
                            AssociatedGraphicsBlockId = p.AssociatedGraphicsBlockId,
                            OriginalAnchorPoint = p.OriginalAnchorPoint,
                            OriginalSpaceCount = p.OriginalSpaceCount,
                            Position = p.Position,
                            AlignmentPoint = p.AlignmentPoint,
                            HorizontalMode = p.HorizontalMode,
                            VerticalMode = p.VerticalMode,
                            Rotation = p.Rotation,
                            Oblique = p.Oblique,
                            Height = p.Height,
                            WidthFactor = p.WidthFactor,
                            TextStyleId = p.TextStyleId
                            }).ToList();
                        LoadTextBlocks(textBlocks);
                        }
                    }
                }
            catch (Exception ex)
                {
                Log($"[错误] 提取文字时出错: {ex.Message}");
                await _windowService.ShowInformationDialogAsync("提取失败", $"提取文字时出错: {ex.Message}");
                }
            finally
                {
                _windowService.ShowMainWindow();

                // ▼▼▼ 【核心修改】日志记录逻辑移动到Token计算之后 ▼▼▼
                await UpdateTokenCountAsync(extractedCount);
                // ▲▲▲ 修改结束 ▲▲▲

                TranslateCommand.RaiseCanExecuteChanged();
                IsProgressIndeterminate = false;
                }
            }


        private async void OnApplyToCad(object parameter)
            {
            var validItems = TextBlockList.Where(item => !string.IsNullOrWhiteSpace(item.TranslatedText) && !item.TranslatedText.StartsWith("["))
                                          .ToList();
            if (!validItems.Any())
                {
                await _windowService.ShowInformationDialogAsync("无内容可应用", "没有有效的翻译文本可供写入CAD。");
                return;
                }

            Log("准备将翻译应用到CAD...");

            CadBridgeService.TextBlocksToLayout = new ObservableCollection<TextBlockViewModel>(TextBlockList);

            _windowService.MinimizeMainWindow();

            CadBridgeService.InvokeApplyLayout();
            }
        #endregion

        #region --- 翻译主逻辑 (调度中心) ---

        private async void OnTranslate(object parameter)
            {
            await ExecuteTranslation(TextBlockList.Where(item => string.IsNullOrWhiteSpace(item.TranslatedText) && !string.IsNullOrWhiteSpace(item.OriginalText)).ToList());
            }

        private async void OnRetranslateFailed(object parameter)
            {
            if (!_failedItems.Any()) return;
            Log("开始重新翻译失败的项目...");
            var itemsToRetry = new List<TextBlockViewModel>(_failedItems);
            _failedItems.Clear();
            RetranslateFailedCommand.RaiseCanExecuteChanged();

            foreach (var item in itemsToRetry)
                {
                item.TranslatedText = "";
                SetItemStatus(item, TranslationStatus.Idle);
                }

            await ExecuteTranslation(itemsToRetry);
            }

        /// <summary>
        /// 【总调度员】根据 SendingMode 决定翻译策略
        /// </summary>
        private async Task ExecuteTranslation(List<TextBlockViewModel> itemsToTranslate)
            {
            // --- 准备阶段 ---
            if (CurrentProvider == null || CurrentProfile == null) { await _windowService.ShowInformationDialogAsync("操作无效", "请先选择一个API配置。"); return; }
            if (IsModelRequired)
                {
                string modelNameToUse = null;
                if (!string.IsNullOrWhiteSpace(CurrentModelInput) && (SelectedModel == null || CurrentModelInput != SelectedModel.Name)) { modelNameToUse = CurrentModelInput.Trim(); }
                else if (SelectedModel != null) { modelNameToUse = SelectedModel.Name; }
                if (string.IsNullOrWhiteSpace(modelNameToUse))
                    {
                    await _windowService.ShowInformationDialogAsync("操作无效", "必须指定一个模型才能进行翻译。");
                    return;
                    }
                CurrentProfile.LastSelectedModel = modelNameToUse;
                if (ModelList.All(m => m.Name != modelNameToUse))
                    {
                    var newModelVm = new ModelViewModel { Name = modelNameToUse, IsFavorite = false };
                    ModelList.Insert(0, newModelVm);
                    CurrentProfile.Models.Add(modelNameToUse);
                    SelectedModel = newModelVm;
                    }
                }

            IsBusy = true;
            IsTranslating = true;
            IsProgressIndeterminate = true;
            ClearAllRowHighlights();
            var totalStopwatch = new System.Diagnostics.Stopwatch();
            totalStopwatch.Start();
            // 注意: "翻译任务开始" 的日志已移除，clearPrevious 的职责已下移

            int totalItems = itemsToTranslate.Count;
            if (totalItems == 0) { Log("没有需要翻译的新内容。"); IsBusy = false; return; }

            UpdateProgress(0, totalItems);
            _translationCts = new CancellationTokenSource();

            TranslationUsage totalUsage = new TranslationUsage();

            try
                {
                ITranslator translator = _apiRegistry.CreateProviderForProfile(CurrentProfile);

                if (SendingMode == PromptSendingMode.Once && translator.IsBatchTranslationSupported)
                    {
                    var usage = await ExecuteBatchTranslation(translator, itemsToTranslate, _translationCts.Token);
                    totalUsage.Add(usage);
                    }
                else
                    {
                    var usage = await ExecuteSentenceBySentenceTranslation(translator, itemsToTranslate, _translationCts.Token);
                    }
                }
            catch (Exception ex)
                {
                Log($"[严重错误] 翻译流程发生意外中断: {ex.Message}", isError: true);
                await _windowService.ShowInformationDialogAsync("严重错误", $"翻译流程发生意外中断:\n\n{ex.Message}");
                }
            finally
                {
                totalStopwatch.Stop();
                if (_translationCts != null && _translationCts.IsCancellationRequested) { Log("任务被用户取消。"); }

                if (totalUsage.TotalTokens > 0)
                    {
                    // 根据计费单位决定日志文本
                    string unitName = CurrentProvider.UnitType == BillingUnit.Character ? "字符" : "Token";
                    Log($"本次总{unitName}用量为 {totalUsage.TotalTokens}，其中输入{unitName}用量 {totalUsage.PromptTokens}，输出{unitName}用量 {totalUsage.CompletionTokens}");
                    }

                if (_failedItems.Any()) { Log($"任务完成，有 {_failedItems.Count} 个项目失败或被取消。总用时 {totalStopwatch.Elapsed.TotalSeconds:F1} 秒"); }
                else { Log($"全部翻译任务成功完成！总用时 {totalStopwatch.Elapsed.TotalSeconds:F1} 秒"); }

                if (totalItems > 0 && !_failedItems.Any()) { UpdateUsageStatistics(itemsToTranslate.Count, itemsToTranslate.Sum(i => i.OriginalText.Length), totalStopwatch.Elapsed.TotalSeconds); }

                IsBusy = false;
                IsTranslating = false;
                IsProgressIndeterminate = false;
                _translationCts?.Dispose();
                _translationCts = null;
                }
            }

        /// <summary>
        /// 【新版批量翻译】带详尽实时反馈
        /// </summary>
        private async Task<TranslationUsage> ExecuteBatchTranslation(ITranslator translator, List<TextBlockViewModel> itemsToTranslate, CancellationToken cancellationToken)
            {
            TranslationUsage accumulatedUsage = new TranslationUsage();
            List<List<TextBlockViewModel>> allChunks;

            string unitName = translator.UnitType == BillingUnit.Character ? "字符" : "Token";
            int limit = translator.UnitType == BillingUnit.Character ? MaxCharsPerBatch : MaxTokensPerBatch;
            Log($"采用[多句话合并为一次请求]模式，当前接口按[{unitName}]计费，最大[{unitName}数]为[{limit}]", clearPrevious: true);

            if (translator.UnitType == BillingUnit.Character)
                {
                allChunks = new List<List<TextBlockViewModel>>();
                var currentChunk = new List<TextBlockViewModel>();
                int currentLength = 0;
                foreach (var item in itemsToTranslate)
                    {
                    int itemLength = item.OriginalText.Length;
                    if (currentChunk.Any() && currentLength + itemLength + 1 > MaxCharsPerBatch)
                        {
                        allChunks.Add(currentChunk);
                        currentChunk = new List<TextBlockViewModel>();
                        currentLength = 0;
                        }
                    currentChunk.Add(item);
                    currentLength += itemLength + 1;
                    }
                if (currentChunk.Any()) allChunks.Add(currentChunk);
                }
            else // BillingUnit.Token
                {
                allChunks = new List<List<TextBlockViewModel>>();
                var currentChunk = new List<TextBlockViewModel>();
                int currentTokens = 0;
                foreach (var item in itemsToTranslate)
                    {
                    var (itemTokens, _) = _tokenizationService.CountTokens(item.OriginalText, CurrentProfile?.LastSelectedModel);
                    if (itemTokens < 0) itemTokens = item.OriginalText.Length / 2;
                    if (currentChunk.Any() && currentTokens + itemTokens > MaxTokensPerBatch)
                        {
                        allChunks.Add(currentChunk);
                        currentChunk = new List<TextBlockViewModel>();
                        currentTokens = 0;
                        }
                    currentChunk.Add(item);
                    currentTokens += itemTokens;
                    }
                if (currentChunk.Any()) allChunks.Add(currentChunk);
                }

            // 步骤 3: 丰富的分块结果日志
            long totalAmount = (translator.UnitType == BillingUnit.Character)
        ? itemsToTranslate.Sum(i => (long)i.OriginalText.Length)
        : itemsToTranslate.Sum(i => {
            var (tokenCount, _) = _tokenizationService.CountTokens(i.OriginalText, CurrentProfile?.LastSelectedModel);
            return (long)(tokenCount >= 0 ? tokenCount : i.OriginalText.Length / 2); // 使用估算作为备用
        });
            Log($"共计[{totalAmount}]个[{unitName}]，已分割为[{allChunks.Count}]个批次进行处理。");

            IsProgressIndeterminate = false;

            // 步骤 4: 智能并发日志
            int concurrencyLevel = IsMultiThreadingEnabled ? (int.TryParse(CurrentConcurrencyLevelInput, out int userLevel) && userLevel > 1 ? userLevel : 2) : 1;
            if (IsMultiThreadingEnabled && allChunks.Count > 1)
                {
                Log($"启动并发批量翻译！最大并发量: {concurrencyLevel}");
                }

            var semaphore = new SemaphoreSlim(concurrencyLevel);
            var allTasks = new List<Task>();
            int completedChunks = 0;


            foreach (var chunk in allChunks)
                {
                if (cancellationToken.IsCancellationRequested) break;
                await semaphore.WaitAsync(cancellationToken);

                allTasks.Add(Task.Run(async () =>
                {
                    var stopwatch = new System.Diagnostics.Stopwatch();
                    stopwatch.Start();
                    var chunkLogPrefix = $"[批次{completedChunks + 1}/{allChunks.Count}]";
                    Log($"{chunkLogPrefix} 开始处理 (包含 {chunk.Count} 项)...");

                    try
                        {
                        var timerCts = new CancellationTokenSource();
                        var timerTask = Task.Run(async () =>
                        {
                            while (!timerCts.IsCancellationRequested)
                                {
                                await Task.Delay(1000, timerCts.Token);
                                if (stopwatch.IsRunning)
                                    {
                                    var statusText = $"{chunkLogPrefix} 翻译中... 已用时 {stopwatch.Elapsed.TotalSeconds:F0} 秒";
                                    _windowService.InvokeOnUIThread(() =>
                                    {
                                        chunk.ForEach(item => {
                                            if (item.Status == TranslationStatus.Translating)
                                                item.TranslatedText = statusText;
                                        });
                                    });
                                    UpdateLastLog($"[{DateTime.Now:HH:mm:ss}] {statusText}");
                                    }
                                }
                        }, timerCts.Token);

                        _windowService.InvokeOnUIThread(() =>
                        {
                            _windowService.ScrollToGridItem(chunk.First());
                            var initialStatusText = $"{chunkLogPrefix} 翻译中...";
                            chunk.ForEach(item => { SetItemStatus(item, TranslationStatus.Translating); item.TranslatedText = initialStatusText; });
                        });

                        var originalTexts = chunk.Select(i => i.OriginalText).ToList();
                        var (translatedTexts, usage) = await translator.TranslateBatchAsync(originalTexts, SourceLanguage, TargetLanguage, this.DisplayedPrompt, cancellationToken);

                        lock (accumulatedUsage) { accumulatedUsage.Add(usage); }

                        timerCts.Cancel();
                        try { await timerTask; } catch (OperationCanceledException) { }

                        _windowService.InvokeOnUIThread(() =>
                        {
                            for (int i = 0; i < chunk.Count; i++)
                                {
                                chunk[i].TranslatedText = translatedTexts[i];
                                SetItemStatus(chunk[i], TranslationStatus.Success);
                                }
                        });

                        stopwatch.Stop();
                        UpdateLastLog($"[{DateTime.Now:HH:mm:ss}] ✓ {chunkLogPrefix} 处理完成。用时 {stopwatch.Elapsed.TotalSeconds:F1} 秒。");
                        }
                    catch (Exception ex)
                        {
                        if (cancellationToken.IsCancellationRequested) return;
                        stopwatch.Stop();
                        _windowService.InvokeOnUIThread(() =>
                        {
                            chunk.ForEach(item => { SetItemStatus(item, TranslationStatus.Failed); HandleApiException(ex as ApiException ?? new ApiException(ApiErrorType.Unknown, CurrentProvider.ServiceType, ex.Message), item); });
                        });
                        UpdateLastLog($"[{DateTime.Now:HH:mm:ss}] ✗ {chunkLogPrefix} 处理失败: {ex.Message}");
                        }
                    finally
                        {
                        int currentCompleted = Interlocked.Increment(ref completedChunks);
                        _windowService.InvokeOnUIThread(() => UpdateProgress(currentCompleted, allChunks.Count));
                        semaphore.Release();
                        }
                }, cancellationToken));
                }

            await Task.WhenAll(allTasks);
            return accumulatedUsage;
            }

        /// <summary>
        /// 【完全恢复的】逐句翻译逻辑
        /// </summary>
        private async Task<TranslationUsage> ExecuteSentenceBySentenceTranslation(ITranslator translator, List<TextBlockViewModel> itemsToTranslate, CancellationToken cancellationToken)
            {
            // 步骤 1: 聚合策略日志
            Log($"采用[每句话发起一次请求]模式。", clearPrevious: true);
            int completedItems = 0;
            IsProgressIndeterminate = false;
            TranslationUsage accumulatedUsage = new TranslationUsage();

            if (IsMultiThreadingEnabled)
                {
                #region --- 多线程逐句翻译 ---
                int concurrencyLevel = int.TryParse(CurrentConcurrencyLevelInput, out int userLevel) && userLevel > 1 ? userLevel : 2;

                // 步骤 2: 丰富的分块/任务说明日志
                Log($"共计[{itemsToTranslate.Count}]个项目。");

                // 步骤 3: 智能并发日志
                if (itemsToTranslate.Count > 1)
                    {
                    Log($"启动并发翻译！最大并发量: {concurrencyLevel}");
                    }

                var batches = itemsToTranslate
                    .Select((item, index) => new { item, index })
                    .GroupBy(x => x.index / concurrencyLevel)
                    .Select(g => g.Select(x => x.item).ToList())
                    .ToList();

                int batchNumber = 0;
                foreach (var batch in batches)
                    {
                    if (cancellationToken.IsCancellationRequested) break;
                    batchNumber++;
                    string batchPrefix = $"第 {batchNumber}/{batches.Count} 批";
                    Log($"{batchPrefix} 开始处理，包含 {batch.Count} 个项目...");

                    var batchStopwatch = new System.Diagnostics.Stopwatch();
                    batchStopwatch.Start();

                    using (var timerCts = new CancellationTokenSource())
                        {
                        var timerTask = Task.Run(async () =>
                        {
                            while (!timerCts.IsCancellationRequested)
                                {
                                await Task.Delay(1000, timerCts.Token);
                                for (int i = 0; i < batch.Count; i++)
                                    {
                                    var item = batch[i];
                                    if (item.Status == TranslationStatus.Translating)
                                        {
                                        string itemSentenceNumber = $"第 {i + 1} 句";
                                        var statusText = $"{batchPrefix} {itemSentenceNumber}: 翻译中... 已用时 {batchStopwatch.Elapsed.TotalSeconds:F0} 秒";
                                        _windowService.InvokeOnUIThread(() => item.TranslatedText = statusText);
                                        UpdateLastLog($"[{DateTime.Now:HH:mm:ss}] {statusText}");
                                        }
                                    }
                                }
                        }, timerCts.Token);

                        var translationTasksInBatch = batch.Select(async (item, index) =>
                        {
                            try
                                {
                                // ▼▼▼ 修改开始 ▼▼▼
                                _windowService.InvokeOnUIThread(() =>
                                {
                                    _windowService.ScrollToGridItem(item);
                                    SetItemStatus(item, TranslationStatus.Translating);
                                    // 立即为每个项设置包含编号的初始状态文本
                                    string itemSentenceNumber = $"第 {index + 1} 句";
                                    var initialStatusText = $"{batchPrefix} {itemSentenceNumber}: 翻译中...";
                                    item.TranslatedText = initialStatusText;
                                });
                                // ▲▲▲ 修改结束 ▲▲▲

                                var (result, usage) = await translator.TranslateAsync(item.OriginalText, SourceLanguage, TargetLanguage, this.DisplayedPrompt, cancellationToken);

                                // 累加用量
                                lock (accumulatedUsage) { accumulatedUsage.Add(usage); }

                                _windowService.InvokeOnUIThread(() =>
                                {
                                    item.TranslatedText = result;
                                    SetItemStatus(item, TranslationStatus.Success);
                                });
                                }                                
                            catch (Exception ex)
                                {
                                if (cancellationToken.IsCancellationRequested) return;
                                _windowService.InvokeOnUIThread(() =>
                                {
                                    SetItemStatus(item, TranslationStatus.Failed);
                                    HandleApiException(ex as ApiException ?? new ApiException(ApiErrorType.Unknown, CurrentProvider.ServiceType, ex.Message), item);
                                });
                                }
                            finally
                                {
                                int currentCompleted = Interlocked.Increment(ref completedItems);
                                _windowService.InvokeOnUIThread(() => UpdateProgress(currentCompleted, itemsToTranslate.Count));
                                }
                        }).ToList();

                        await Task.WhenAll(translationTasksInBatch);

                        batchStopwatch.Stop();
                        timerCts.Cancel();
                        try { await timerTask; } catch (OperationCanceledException) { }
                        }
                    Log($"{batchPrefix} 处理完成。用时 {batchStopwatch.Elapsed.TotalSeconds:F1} 秒。");
                    }
                #endregion
                }
            else
                {
                #region --- 单线程逐句翻译 (已恢复您原有逻辑) ---
                Log($"共计[{itemsToTranslate.Count}]个项目，将逐一处理。");
                foreach (var item in itemsToTranslate)
                    {
                    if (cancellationToken.IsCancellationRequested) break;

                    string statusPrefix = $"第 {completedItems + 1}/{itemsToTranslate.Count} 项";

                    // ▼▼▼ 修改开始 ▼▼▼
                    _windowService.InvokeOnUIThread(() =>
                    {
                        _windowService.ScrollToGridItem(item);
                        SetItemStatus(item, TranslationStatus.Translating);
                        // 立即设置包含编号的初始状态文本
                        item.TranslatedText = $"{statusPrefix}: 翻译中...";
                    });
                    // ▲▲▲ 修改结束 ▲▲▲

                    var stopwatch = new System.Diagnostics.Stopwatch();
                    stopwatch.Start();
                    Log($"{statusPrefix} 开始处理...");

                    using (var timerCts = new CancellationTokenSource())
                        {
                        var timerTask = Task.Run(async () =>
                        {
                            while (!timerCts.IsCancellationRequested)
                                {
                                await Task.Delay(1000, timerCts.Token);
                                if (item.Status == TranslationStatus.Translating)
                                    {
                                    var statusText = $"{statusPrefix}: 翻译中... 已用时 {stopwatch.Elapsed.TotalSeconds:F0} 秒";
                                    _windowService.InvokeOnUIThread(() => item.TranslatedText = statusText);
                                    UpdateLastLog($"[{DateTime.Now:HH:mm:ss}] {statusText}");
                                    }
                                }
                        }, timerCts.Token);

                        try
                            {
                            var (result, usage) = await translator.TranslateAsync(item.OriginalText, SourceLanguage, TargetLanguage, this.DisplayedPrompt, cancellationToken);

                            // 累加用量
                            accumulatedUsage.Add(usage);

                            _windowService.InvokeOnUIThread(() =>
                            {
                                item.TranslatedText = result;
                                SetItemStatus(item, TranslationStatus.Success);
                            });
                            }
                        catch (Exception ex)
                            {
                            if (cancellationToken.IsCancellationRequested) break;
                            _windowService.InvokeOnUIThread(() =>
                            {
                                SetItemStatus(item, TranslationStatus.Failed);
                                HandleApiException(ex as ApiException ?? new ApiException(ApiErrorType.Unknown, CurrentProvider.ServiceType, ex.Message), item);
                            });
                            UpdateLastLog($"[{DateTime.Now:HH:mm:ss}] {statusPrefix} 翻译失败: {ex.Message}");
                            break;
                            }
                        finally
                            {
                            stopwatch.Stop();
                            timerCts.Cancel();
                            try { await timerTask; } catch (OperationCanceledException) { }
                            completedItems++;
                            UpdateLastLog($"[{DateTime.Now:HH:mm:ss}] ✓ {statusPrefix} 处理完成。用时 {stopwatch.Elapsed.TotalSeconds:F1} 秒。");
                            _windowService.InvokeOnUIThread(() =>
                            {
                                UpdateProgress(completedItems, itemsToTranslate.Count);
                                ApplyToCadCommand.RaiseCanExecuteChanged();
                            });
                            }
                        }
                    }
                #endregion
                }
            return accumulatedUsage;
            }

        #endregion

        #region --- API与模型管理 ---
        private async void OnGetModels(object parameter)
            {
            if (CurrentProvider == null) return;
            IsBusy = true;
            Log($"正在从 {CurrentProvider.DisplayName} 获取模型列表...");

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
                {
                var provider = _apiRegistry.CreateProviderForProfile(CurrentProfile);
                var models = await provider.GetModelsAsync(cts.Token);

                if (models != null && models.Any())
                    {
                    CurrentProfile.Models = new List<string>(models);
                    RefreshModelList();
                    SaveSettings();
                    Log($"成功获取 {models.Count} 个模型！列表已更新。");
                    }
                else
                    {
                    Log("未能获取到任何模型列表。");
                    }
                }
            catch (OperationCanceledException)
                {
                Log("[错误] 获取模型列表超时（超过10秒），操作已取消。", isError: true);
                await _windowService.ShowInformationDialogAsync("操作超时", "获取模型列表超时（超过10秒），请检查您的网络连接或稍后重试。");
                }
            catch (ApiException apiEx)
                {
                HandleApiException(apiEx, null);
                await _windowService.ShowInformationDialogAsync("操作失败", $"获取模型列表时发生错误:\n\n{apiEx.Message}");
                }
            catch (Exception ex)
                {
                Log($"[错误] 获取模型列表时失败: {ex.Message}", isError: true);
                await _windowService.ShowInformationDialogAsync("操作失败", $"获取模型列表时发生错误:\n\n{ex.Message}");
                }
            finally
                {
                IsBusy = false;
                cts.Dispose();
                }
            }

        private async void OnGetBalance(object parameter)
            {
            if (CurrentProvider == null) return;
            IsBusy = true;
            Log($"正在从 {CurrentProvider.DisplayName} 查询余额...");
            try
                {
                var provider = _apiRegistry.CreateProviderForProfile(CurrentProfile);
                var balanceData = await provider.CheckBalanceAsync();

                var newRecord = new BalanceRecord
                    {
                    Timestamp = DateTime.Now,
                    ServiceType = CurrentProvider.ServiceType,
                    Data = balanceData
                    };

                BalanceHistory.Insert(0, newRecord);

                UpdateBalanceDisplayForCurrentProvider();

                SaveSettings();
                Log("余额查询成功！");
                }
            catch (ApiException apiEx)
                {
                HandleApiException(apiEx, null);
                await _windowService.ShowInformationDialogAsync("操作失败", $"查询余额时发生错误:\n\n{apiEx.Message}");
                }
            catch (Exception ex)
                {
                Log($"[错误] 查询余额时失败: {ex.Message}", isError: true);
                await _windowService.ShowInformationDialogAsync("操作失败", $"查询余额时发生错误:\n\n{ex.Message}");
                }
            finally
                {
                IsBusy = false;
                }
            }

        private void OnCancelTranslation(object parameter)
            {
            _translationCts?.Cancel();
            Log("正在取消翻译任务...");
            }

        private async void OnManageModels(object parameter)
            {
            if (CurrentProfile == null)
                {
                await _windowService.ShowInformationDialogAsync("操作无效", "请先选择一个API配置。");
                return;
                }
            var modelManagementVM = new ModelManagementViewModel(CurrentProfile.ProfileName, new List<string>(CurrentProfile.Models), new List<string>(CurrentProfile.FavoriteModels), _windowService);

            var dialogResult = _windowService.ShowModelManagementDialog(modelManagementVM);

            if (dialogResult == true)
                {
                var (finalModels, favoriteModels) = modelManagementVM.GetFinalModels();
                CurrentProfile.Models = finalModels;
                CurrentProfile.FavoriteModels = favoriteModels;

                if (modelManagementVM.SelectedModel != null && !string.IsNullOrWhiteSpace(modelManagementVM.SelectedModel.Name))
                    {
                    CurrentProfile.LastSelectedModel = modelManagementVM.SelectedModel.Name.Trim();
                    }
                else
                    {
                    CurrentProfile.LastSelectedModel = finalModels.FirstOrDefault();
                    }

                RefreshModelList();
                SaveSettings();
                Log("模型列表已更新。");
                }
            }

        private async void OnManageApiDefinitions(object parameter)
            {
            // 【修改】在创建 ApiDefinitionViewModel 时，将 _windowService 传进去
            var vm = new ApiDefinitionViewModel(null, _windowService);
            var window = new ApiDefinitionWindow(vm);

            if (window.ShowDialog() == true)
                {
                var newDefinition = vm.ApiDef;
                await _windowService.ShowInformationDialogAsync("功能待实现", $"已成功创建API定义: '{newDefinition.DisplayName}'。\n下一步我们将实现让这个配置真正生效的逻辑。");
                }
            }
        private void OnViewApiDocumentation()
            {
            if (CurrentProvider == null || string.IsNullOrWhiteSpace(CurrentProvider.ApiDocumentationUrl))
                {
                return;
                }

            try
                {
                System.Diagnostics.Process.Start(CurrentProvider.ApiDocumentationUrl);
                }
            catch (Exception ex)
                {
                Log($"[错误] 无法打开帮助文档链接: {ex.Message}", isError: true);
                _windowService.ShowInformationDialogAsync("操作失败", $"无法打开链接: {CurrentProvider.ApiDocumentationUrl}\n\n错误: {ex.Message}");
                }
            }
        #endregion

        #region --- 设置、日志与辅助方法 ---
        private void LoadSettings()
            {
            _isLoading = true;
            _currentSettings = _settingsService.LoadSettings();

            ApiProfiles.Clear();
            _currentSettings.ApiProfiles.ForEach(p => ApiProfiles.Add(new ApiProfile(p)));

            var lastServiceType = _currentSettings.LastSelectedApiService;
            CurrentProvider = ApiServiceOptions.FirstOrDefault(p => p.ServiceType == lastServiceType) ?? ApiServiceOptions.First();
            CurrentProfile = ApiProfiles.FirstOrDefault(p => p.ServiceType == CurrentProvider.ServiceType);

            if (CurrentProfile == null)
                {
                CurrentProfile = new ApiProfile { ServiceType = CurrentProvider.ServiceType, ProfileName = $"{CurrentProvider.ServiceType} Profile (新建)" };
                ApiProfiles.Add(CurrentProfile);
                }

            UpdateUiFromCurrentProfile();

            IsLiveLayoutEnabled = _currentSettings.IsLiveLayoutEnabled;
            LineSpacingOptions.Clear();
            _currentSettings.LineSpacingPresets.Distinct().ToList().ForEach(p => LineSpacingOptions.Add(p));
            CurrentLineSpacingInput = _currentSettings.LastSelectedLineSpacing;

            BalanceHistory.Clear();
            _currentSettings.BalanceHistory.OrderByDescending(r => r.Timestamp).ToList().ForEach(r => BalanceHistory.Add(r));
            UpdateBalanceDisplayForCurrentProvider();

            IsMultiThreadingEnabled = _currentSettings.IsMultiThreadingEnabled;
            ConcurrencyLevelOptions.Clear();
            _currentSettings.ConcurrencyPresets.Distinct().ToList().ForEach(p => ConcurrencyLevelOptions.Add(p));
            CurrentConcurrencyLevelInput = _currentSettings.LastSelectedConcurrency;

            OnPropertyChanged(nameof(SourceLanguage));
            OnPropertyChanged(nameof(TargetLanguage));
            _isLoading = false;
            }

        private void SaveSettings()
            {
            if (_isLoading) return;
            if (_currentSettings == null) _currentSettings = new AppSettings();

            if (CurrentProfile != null && SelectedModel != null)
                {
                CurrentProfile.LastSelectedModel = SelectedModel.Name;
                }

            _currentSettings.IsLiveLayoutEnabled = IsLiveLayoutEnabled;
            _currentSettings.LastSelectedLineSpacing = CurrentLineSpacingInput;
            _currentSettings.LineSpacingPresets = LineSpacingOptions.Distinct().ToList();
            _currentSettings.IsMultiThreadingEnabled = IsMultiThreadingEnabled;
            _currentSettings.LastSelectedConcurrency = CurrentConcurrencyLevelInput;
            _currentSettings.ConcurrencyPresets = ConcurrencyLevelOptions.Distinct().ToList();
            _currentSettings.ApiProfiles = ApiProfiles.ToList();
            _currentSettings.BalanceHistory = BalanceHistory.ToList();


            if (CurrentProvider != null)
                _currentSettings.LastSelectedApiService = CurrentProvider.ServiceType;

            if (SelectedPromptTemplate == PromptTemplateType.Custom)
                {
                _currentSettings.CustomPrompt = DisplayedPrompt;
                }

            _settingsService.SaveSettings(_currentSettings);
            }

        private void UpdateUiFromCurrentProfile()
            {
            if (CurrentProfile == null) return;

            CurrentProfile.PropertyChanged -= OnCurrentProfilePropertyChanged;
            CurrentProfile.PropertyChanged += OnCurrentProfilePropertyChanged;

            RefreshModelList();

            OnPropertyChanged(nameof(CurrentProfile));
            }

        private void RefreshModelList()
            {
            if (CurrentProfile == null) return;

            ModelList.Clear();
            var modelsWithState = CurrentProfile.Models
                .Select(m => new ModelViewModel
                    {
                    Name = m,
                    IsFavorite = CurrentProfile.FavoriteModels.Contains(m)
                    })
                .OrderByDescending(m => m.IsFavorite)
                .ThenBy(m => m.Name);

            foreach (var modelVm in modelsWithState)
                {
                ModelList.Add(modelVm);
                }

            SelectedModel = ModelList.FirstOrDefault(m => m.Name == CurrentProfile.LastSelectedModel) ?? ModelList.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedModel));
            }

        private void OnCurrentProfilePropertyChanged(object sender, PropertyChangedEventArgs e)
            {
            if (!_isLoading) SaveSettings();
            }

        private void UpdateUsageStatistics(int paragraphs, long characters, double seconds)
            {
            if (CurrentProvider == null || paragraphs == 0) return;
            var providerKey = CurrentProvider.ServiceType.ToString();

            if (!_currentSettings.UsageStatistics.ContainsKey(providerKey))
                {
                _currentSettings.UsageStatistics[providerKey] = new ApiUsageStats();
                }
            var stats = _currentSettings.UsageStatistics[providerKey];
            stats.TotalParagraphsTranslated += paragraphs;
            stats.TotalCharactersTranslated += characters;
            stats.TotalTimeInSeconds += seconds;

            SaveSettings();
            }

        private void UpdateBalanceDisplayForCurrentProvider()
            {
            if (CurrentProvider == null) return;

            var lastRecord = BalanceHistory.FirstOrDefault(r => r.ServiceType == CurrentProvider.ServiceType);

            if (lastRecord == null || lastRecord.Data == null || !lastRecord.Data.Any())
                {
                LastBalanceDisplay = "当前无余额记录";
                return;
                }

            var balanceMapping = _currentSettings.FriendlyNameMappings.FirstOrDefault(m => m.Key == "CanonicalTotalBalance").Value;

            if (balanceMapping == null)
                {
                balanceMapping = _currentSettings.FriendlyNameMappings.FirstOrDefault(m => m.Key == "CanonicalRemainingBalance").Value;
                }

            if (balanceMapping != null)
                {
                var balancePair = lastRecord.Data.FirstOrDefault(d => balanceMapping.Aliases.Contains(d.Key));
                LastBalanceDisplay = balancePair.Value ?? "未找到余额信息";
                }
            else
                {
                LastBalanceDisplay = "未配置余额规则";
                }
            }

        private void Log(string message, bool clearPrevious = false, bool isError = false)
            {
            if (clearPrevious)
                _windowService.InvokeOnUIThread(() => StatusLog.Clear());

            var formattedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";

            if (isError)
                {
                formattedMessage = $"[错误] {formattedMessage}";
                }

            CadBridgeService.WriteToCommandLine(formattedMessage);
            _windowService.InvokeOnUIThread(() => StatusLog.Add(formattedMessage));
            }

        private void UpdateLastLog(string message)
            {
            if (StatusLog.Any())
                {
                // 直接修改最后一项的内容，UI会通过绑定自动更新
                _windowService.InvokeOnUIThread(() => StatusLog[StatusLog.Count - 1] = message);
                }
            // 同时更新CAD命令行
            CadBridgeService.UpdateLastMessageOnCommandLine(message);
            }

        private void UpdateProgress(int completed, int total)
            {
            if (total == 0)
                {
                ProgressValue = 0;
                ProgressText = "无项目";
                return;
                }
            ProgressValue = (int)((double)completed / total * 100);

            string tokenInfo = _isTokenCountAvailable ? $" (Tokens: {_totalTokens})" : "";

            if (SendingMode == PromptSendingMode.Once && CurrentProvider.IsBatchTranslationSupported)
                {
                ProgressText = $"批次 ({completed}/{total}){tokenInfo}";
                }
            else
                {
                ProgressText = IsMultiThreadingEnabled ? $"({completed}/{total}){tokenInfo}" : $"({completed}/{total}) {ProgressValue}%{tokenInfo}";
                }
            }

        private async Task UpdateTokenCountAsync(int extractedCount)
            {
            string logPrefix = (extractedCount > 0) ? $"成功提取并分析了 {extractedCount} 个段落。" : "";
            if (string.IsNullOrEmpty(logPrefix))
                {
                UpdateProgress(0, TextBlockList.Count);
                return;
                }

            // 在开始计算前，先重置状态
            _isTokenCountAvailable = false;

            // 检查是否有必要和有可能计算Token
            if (CurrentProvider != null && CurrentProvider.IsTokenCountSupported && TextBlockList.Any())
                {
                string modelName = CurrentProfile?.LastSelectedModel;
                string combinedText = string.Join("\n", TextBlockList.Select(b => b.OriginalText));

                // --- “三级火箭”决策开始 ---

                // 第一级：尝试使用本地服务进行精确计算 (例如：Tiktoken for GPT models)
                if (CurrentProvider.IsLocalTokenCountSupported && _tokenizationService.CanTokenize(modelName))
                    {
                    var (tokenCount, errorMessage) = _tokenizationService.CountTokens(combinedText, modelName);
                    if (errorMessage == null)
                        {
                        _totalTokens = tokenCount;
                        _isTokenCountAvailable = true;
                        }
                    }

                // 第二级：如果第一级不适用或失败，则尝试通过API计算 (例如：Gemini)
                if (!_isTokenCountAvailable && !CurrentProvider.IsLocalTokenCountSupported)
                    {
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5))) // 5秒超时
                        {
                        try
                            {
                            var translator = _apiRegistry.CreateProviderForProfile(CurrentProfile);
                            _totalTokens = await translator.CountTokensAsync(combinedText, cts.Token);
                            _isTokenCountAvailable = true;
                            }
                        catch
                            {
                            // 失败则自动进入第三级
                            _isTokenCountAvailable = false;
                            }
                        }
                    }

                // 第三级：如果前两级都失败或不适用，使用 gpt-4o 模型进行本地估算
                if (!_isTokenCountAvailable)
                    {
                    var (tokenCount, errorMessage) = _tokenizationService.CountTokens(combinedText, "gpt-3.5-turbo");
                    if (errorMessage == null)
                        {
                        _totalTokens = tokenCount;
                        _isTokenCountAvailable = true;
                        logPrefix += " (Token为估算值)"; // 明确告知用户这是估算
                        }
                    }
                }

            // 【最终日志输出】
            // 根据 _isTokenCountAvailable 的最终状态来决定日志内容
            if (_isTokenCountAvailable)
                {
                string unitName = CurrentProvider.UnitType == BillingUnit.Character ? "字符" : "Token";
                Log($"{logPrefix} 总{unitName}量：{_totalTokens}");
                }
            else
                {
                // 如果所有方法都失败，则只显示基础信息
                Log(logPrefix);
                }

            UpdateProgress(0, TextBlockList.Count);
            }
        private void HandleApiException(ApiException apiEx, TextBlockViewModel failedItem)
            {
            string errorPrefix = apiEx.ErrorType switch
                {
                    ApiErrorType.NetworkError => "[网络错误]",
                    ApiErrorType.ConfigurationError => "[配置错误]",
                    ApiErrorType.ApiError => "[接口返回错误]",
                    ApiErrorType.InvalidResponse => "[响应无效]",
                    _ => "[未知错误]"
                    };

            if (failedItem != null)
                {
                failedItem.TranslatedText = $"{errorPrefix} {apiEx.Message}";
                lock (_failedItems) { _failedItems.Add(failedItem); }
                _windowService.InvokeOnUIThread(() => RetranslateFailedCommand.RaiseCanExecuteChanged());
                }

            Log($"[{apiEx.Provider}] {errorPrefix} {apiEx.Message} (Status: {apiEx.StatusCode}, Code: {apiEx.ApiErrorCode})", isError: true);
            }

        #endregion

        #region --- 状态与高亮辅助方法 ---

        private void SetItemStatus(TextBlockViewModel item, TranslationStatus status)
            {
            item.Status = status;
            switch (status)
                {
                case TranslationStatus.Translating:
                    item.RowBackground = _translatingBrush;
                    break;
                case TranslationStatus.Success:
                    item.RowBackground = _successBrush;
                    break;
                case TranslationStatus.Failed:
                    item.RowBackground = _failedBrush;
                    break;
                case TranslationStatus.Idle:
                default:
                    item.RowBackground = _defaultBrush;
                    break;
                }
            }

        private void ClearAllRowHighlights()
            {
            foreach (var item in TextBlockList)
                {
                SetItemStatus(item, TranslationStatus.Idle);
                }
            }
        #endregion

        #region --- 表格操作与属性变更 ---
        private async void OnMerge(object selectedItems)
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
            foreach (var item in selectedViewModels.AsEnumerable().Reverse()) { TextBlockList.Remove(item); }
            TextBlockList.Insert(firstIndex, mergedItem);
            RenumberItems();
            await UpdateTokenCountAsync(TextBlockList.Count);
            }

        private async void OnDelete(object selectedItems)
            {
            var itemsToDelete = (selectedItems as IList<object>)?.Cast<TextBlockViewModel>().ToList();
            if (itemsToDelete == null || itemsToDelete.Count == 0) return;

            var result = await _windowService.ShowConfirmationDialogAsync("确认删除", $"确定要删除选中的 {itemsToDelete.Count} 行吗？", "确认删除");

            if (result == MessageBoxResult.Primary)
                {
                foreach (var item in itemsToDelete) { TextBlockList.Remove(item); }
                RenumberItems();
                await UpdateTokenCountAsync(TextBlockList.Count);
                }
            }

        private async void OnSplit(object selectedItem)
            {
            if (!(selectedItem is TextBlockViewModel selectedVM)) return;

            var linesToSplit = new List<string>();
            bool isUndoMerge = false;

            if (selectedVM.SourceObjectIds != null && selectedVM.SourceObjectIds.Count > 1)
                {
                isUndoMerge = true;
                Log("检测到自动合并的段落，正在尝试撤销...");
                linesToSplit = _advancedTextService.GetOriginalTextsByIds(selectedVM.SourceObjectIds);
                Log($"成功将段落还原为 {linesToSplit.Count} 个原始部分。");
                }
            else
                {
                linesToSplit.AddRange(selectedVM.OriginalText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
                }

            if (linesToSplit.Count <= 1)
                {
                string message = isUndoMerge ? "无法将此段落还原为多个部分。" : "当前行不包含可供拆分的多行文本。";
                await _windowService.ShowInformationDialogAsync("操作无效", message);
                return;
                }

            int originalIndex = TextBlockList.IndexOf(selectedVM);
            TextBlockList.RemoveAt(originalIndex);

            var newGroupKey = Guid.NewGuid().ToString();

            var newBlocks = new List<TextBlockViewModel>();
            for (int i = 0; i < linesToSplit.Count; i++)
                {
                var newBlock = new TextBlockViewModel
                    {
                    OriginalText = linesToSplit[i],
                    GroupKey = newGroupKey
                    };
                if (isUndoMerge && i < selectedVM.SourceObjectIds.Count)
                    {
                    newBlock.SourceObjectIds.Add(selectedVM.SourceObjectIds[i]);
                    }
                newBlocks.Add(newBlock);
                }

            for (int i = 0; i < newBlocks.Count; i++)
                {
                TextBlockList.Insert(originalIndex + i, newBlocks[i]);
                }

            var currentBlocksState = TextBlockList.ToList();
            LoadTextBlocks(currentBlocksState);
            await UpdateTokenCountAsync(TextBlockList.Count);
            }

        private async void OnEdit(object selectedItem)
            {
            if (!(selectedItem is TextBlockViewModel selectedVM)) return;
            if (selectedVM.SourceObjectIds != null && selectedVM.SourceObjectIds.Any())
                {
                await _windowService.ShowInformationDialogAsync("操作无效", "不能直接编辑从CAD提取的文本。");
                return;
                }

            var (dialogResult, editedText) = _windowService.ShowEditDialog(selectedVM.OriginalText);

            if (dialogResult == true)
                {
                selectedVM.OriginalText = editedText;
                selectedVM.Character = string.IsNullOrWhiteSpace(selectedVM.OriginalText) ? "?" : selectedVM.OriginalText.Substring(0, 1).ToUpper();
                selectedVM.TranslatedText = "";
                }
            }

        private async void OnReset(object parameter)
            {
            var result = await _windowService.ShowConfirmationDialogAsync("确认重置", "您确定要清空所有已提取的文本吗？此操作不可恢复。", "确认清空");

            if (result == MessageBoxResult.Primary)
                {
                TextBlockList.Clear();
                _failedItems.Clear();
                RetranslateFailedCommand.RaiseCanExecuteChanged();
                UpdateProgress(0, 0);
                IsProgressIndeterminate = false;
                Log("界面已重置，请重新选择CAD文字。");
                await UpdateTokenCountAsync(TextBlockList.Count);
                }
            }

        private void OnDeleteLineSpacingOption(object parameter)
            {
            if (parameter is string optionToDelete && optionToDelete != "不指定")
                {
                LineSpacingOptions.Remove(optionToDelete);
                if (CurrentLineSpacingInput == optionToDelete)
                    {
                    CurrentLineSpacingInput = "不指定";
                    }
                SaveSettings();
                Log($"已删除行间距选项: {optionToDelete}");
                }
            }

        private void OnDeleteConcurrencyOption(object parameter)
            {
            if (parameter is string optionToDelete && optionToDelete != "2")
                {
                ConcurrencyLevelOptions.Remove(optionToDelete);
                if (CurrentConcurrencyLevelInput == optionToDelete)
                    {
                    CurrentConcurrencyLevelInput = "2";
                    }
                SaveSettings();
                Log($"已删除并发量选项: {optionToDelete}");
                }
            }

        private void OnViewHistory(object parameter)
            {
            var historyViewModel = new BalanceHistoryViewModel(this.BalanceHistory, _currentSettings, CurrentProvider.ServiceType);

            historyViewModel.DeleteRequested += (recordsToDelete) =>
            {
                if (recordsToDelete != null && recordsToDelete.Any())
                    {
                    foreach (var record in recordsToDelete)
                        {
                        BalanceHistory.Remove(record);
                        }
                    SaveSettings();
                    }
            };

            _windowService.ShowBalanceHistoryDialog(historyViewModel);
            }

        public void LoadTextBlocks(List<TextBlockViewModel> blocks)
            {
            TextBlockList.Clear();
            _brushIndex = 0;

            if (blocks == null || !blocks.Any())
                {
                RenumberItems();
                return;
                }

            foreach (var block in blocks)
                {
                var newVm = new TextBlockViewModel
                    {
                    OriginalText = block.OriginalText,
                    TranslatedText = block.TranslatedText,
                    SourceObjectIds = block.SourceObjectIds,
                    IsTitle = block.IsTitle,
                    GroupKey = block.GroupKey,
                    AssociatedGraphicsBlockId = block.AssociatedGraphicsBlockId,
                    OriginalAnchorPoint = block.OriginalAnchorPoint,
                    OriginalSpaceCount = block.OriginalSpaceCount,
                    Position = block.Position,
                    AlignmentPoint = block.AlignmentPoint,
                    HorizontalMode = block.HorizontalMode,
                    VerticalMode = block.VerticalMode,
                    Rotation = block.Rotation,
                    Oblique = block.Oblique,
                    Height = block.Height,
                    WidthFactor = block.WidthFactor,
                    TextStyleId = block.TextStyleId
                    };
                TextBlockList.Add(newVm);
                }

            try
                {
                var mainNumRegex = new Regex(@"^\s*(\d+)([.,、])");
                var subNumRegex = new Regex(@"^\s*([<(（【〔](?:\d+|[a-zA-Z])[>)）】〕]|[a-zA-Z]\s*[.,、)])");
                var analysisList = new List<(TextBlockViewModel Block, string Type, string Value, int Index)>();
                for (int i = 0; i < TextBlockList.Count; i++)
                    {
                    var vm = TextBlockList[i];
                    string text = vm.OriginalText.TrimStart();
                    var mainMatch = mainNumRegex.Match(text);
                    if (mainMatch.Success) { analysisList.Add((vm, "Main", mainMatch.Groups[1].Value, i)); continue; }
                    var subMatch = subNumRegex.Match(text);
                    if (subMatch.Success) { analysisList.Add((vm, "Sub", subMatch.Value.Trim(), i)); continue; }
                    analysisList.Add((vm, "None", "无", i));
                    }

                bool hasMainNumbers = analysisList.Any(x => x.Type == "Main");
                string lastParentNumber = null;

                foreach (var item in analysisList)
                    {
                    if (item.Block.IsTitle) { item.Block.Character = "标题"; continue; }
                    if (!hasMainNumbers && item.Type == "Sub") { item.Block.Character = item.Value; }
                    else if (item.Type == "Main") { lastParentNumber = item.Value; item.Block.Character = lastParentNumber; }
                    else if (item.Type == "Sub")
                        {
                        if (!string.IsNullOrEmpty(lastParentNumber)) { item.Block.Character = $"{lastParentNumber}.{item.Value}"; }
                        else
                            {
                            var nextMain = analysisList.FirstOrDefault(x => x.Index > item.Index && x.Type == "Main");
                            if (nextMain != default && int.TryParse(nextMain.Value, out int num)) { item.Block.Character = $"{num - 1}.{item.Value}"; }
                            else { item.Block.Character = item.Value; }
                            }
                        }
                    else { item.Block.Character = "无"; }
                    }
                }
            catch { foreach (var vm in TextBlockList) { vm.Character = "错误"; } }

            var groups = TextBlockList.Where(vm => !string.IsNullOrEmpty(vm.GroupKey)).GroupBy(vm => vm.GroupKey).ToList();
            foreach (var group in groups)
                {
                var members = group.OrderBy(m => TextBlockList.IndexOf(m)).ToList();
                if (!members.Any()) continue;

                var parent = members.First();
                string parentNumber = parent.Character;

                if (parentNumber == "无")
                    {
                    var originalParent = TextBlockList.FirstOrDefault(vm => vm.Id.ToString() == group.Key);
                    if (originalParent != null)
                        {
                        parentNumber = originalParent.Character;
                        }
                    }

                for (int i = 0; i < members.Count; i++)
                    {
                    members[i].Character = $"{parentNumber} (行{i + 1})";
                    }
                }

            foreach (var vm in TextBlockList) { vm.BgColor = _characterBrushes[_brushIndex++ % _characterBrushes.Length]; }
            RenumberItems();
            ApplyToCadCommand.RaiseCanExecuteChanged();
            TranslateCommand.RaiseCanExecuteChanged();
            }

        private void RenumberItems()
            {
            for (int i = 0; i < TextBlockList.Count; i++) { TextBlockList[i].Id = i + 1; }
            }

        #endregion

        #region --- 提示词辅助方法 ---


        private void UpdateDisplayedPrompt()
            {
            if (SelectedPromptTemplate == PromptTemplateType.Custom)
                {
                DisplayedPrompt = _customPrompt;
                }
            else
                {
                // 【修改】从“专家”那里获取模板
                DisplayedPrompt = PromptTemplateManager.GetCurrentPrompt(_currentSettings);
                }
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