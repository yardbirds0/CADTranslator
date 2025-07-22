using Autodesk.AutoCAD.ApplicationServices;
using CADTranslator.Models.API;
using CADTranslator.Models.CAD;
using CADTranslator.Models.UI;
using CADTranslator.Services.CAD;
using CADTranslator.Services.Settings;
using CADTranslator.Services.Translation;
using CADTranslator.Services.UI;
using CADTranslator.Views;
using System.Windows;
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
        #endregion

        #region --- 绑定属性 ---
        // (所有绑定属性保持不变)
        public bool IsBusy
            {
            get => _isBusy;
            set
                {
                if (SetField(ref _isBusy, value))
                    {
                    OnPropertyChanged(nameof(IsUiEnabled));
                    // ▼▼▼ 【核心修改】在这里添加对命令状态的更新通知 ▼▼▼
                    RetranslateFailedCommand.RaiseCanExecuteChanged();
                    TranslateCommand.RaiseCanExecuteChanged(); // 顺便也更新一下主翻译按钮的状态
                    }
                }
            }

        public bool IsUiEnabled => !IsBusy;

        public ITranslator CurrentProvider
            {
            get => _currentProvider;
            set
                {
                if (SetField(ref _currentProvider, value))
                    {
                    // 当提供商变化时，找到其对应的Profile
                    CurrentProfile = ApiProfiles.FirstOrDefault(p => p.ServiceType == _currentProvider.ServiceType);
                    if (CurrentProfile == null)
                        {
                        CurrentProfile = new ApiProfile { ServiceType = _currentProvider.ServiceType, ProfileName = $"{_currentProvider.ServiceType} Profile" };
                        ApiProfiles.Add(CurrentProfile);
                        }
                    UpdateUiFromCurrentProfile();


                    // 通知所有相关的UI属性进行更新
                    OnPropertyChanged(nameof(IsUserIdEnabled));
                    OnPropertyChanged(nameof(IsApiKeyEnabled));
                    OnPropertyChanged(nameof(IsModelRequired));
                    OnPropertyChanged(nameof(IsPromptSupported));
                    OnPropertyChanged(nameof(IsModelListEnabled));
                    OnPropertyChanged(nameof(IsApiUrlEnabled));
                    OnPropertyChanged(nameof(IsBalanceFeatureEnabled));

                    // 更新命令状态
                    GetModelsCommand.RaiseCanExecuteChanged();
                    GetBalanceCommand.RaiseCanExecuteChanged();
                    ViewHistoryCommand.RaiseCanExecuteChanged();
                    ManageModelsCommand.RaiseCanExecuteChanged();
                    ViewApiDocumentationCommand.RaiseCanExecuteChanged();

                    // 更新余额显示
                    UpdateBalanceDisplayForCurrentProvider();

                    if (!_isLoading)
                        {
                        // 保存当前选择的服务类型
                        _currentSettings.LastSelectedApiService = _currentProvider.ServiceType;
                        SaveSettings();
                        }
                    }
                }
            }

        // --- 绑定到 CurrentProvider 的能力声明属性 ---
        public bool IsUserIdEnabled => CurrentProvider?.IsUserIdRequired ?? false;
        public bool IsApiKeyEnabled => CurrentProvider?.IsApiKeyRequired ?? false;
        public bool IsModelRequired => CurrentProvider?.IsModelRequired ?? false;
        public bool IsPromptSupported => CurrentProvider?.IsPromptSupported ?? false;
        public bool IsModelListEnabled => CurrentProvider?.IsModelFetchingSupported ?? false;
        public bool IsApiUrlEnabled => CurrentProvider?.IsApiUrlRequired ?? false;
        public bool IsBalanceFeatureEnabled => CurrentProvider?.IsBalanceCheckSupported ?? false;
        // ---

        public ObservableCollection<TextBlockViewModel> TextBlockList { get; set; }
        public ObservableCollection<ApiProfile> ApiProfiles { get; set; } // 仍然需要这个来管理配置
        public List<ITranslator> ApiServiceOptions { get; } // 【修改】数据源变为ITranslator列表

        public ApiProfile CurrentProfile { get; set; } // 存储与CurrentProvider对应的配置信息
        public ObservableCollection<ModelViewModel> ModelList { get; set; }
        private ModelViewModel _selectedModel;
        public ModelViewModel SelectedModel
            {
            get => _selectedModel;
            set
                {
                // ▼▼▼ 【核心修正】使用全新的、更健壮的逻辑 ▼▼▼
                if (SetField(ref _selectedModel, value))
                    {
                    // 无论 _selectedModel 是一个新对象还是 null，都执行以下逻辑

                    // 1. 获取新模型的名称，如果 _selectedModel 是 null，则 newModelName 也为 null
                    var newModelName = _selectedModel?.Name;

                    // 2. 更新输入框的文本
                    CurrentModelInput = newModelName;
                    OnPropertyChanged(nameof(CurrentModelInput));

                    // 3. 更新用于持久化的配置 (可以安全地将 null 赋给 LastSelectedModel)
                    if (CurrentProfile != null)
                        {
                        CurrentProfile.LastSelectedModel = newModelName;
                        }

                    // 4. 立即保存设置，确保状态被持久化
                    SaveSettings();
                    }
                }
            }
        public string CurrentModelInput { get; set; } 
        public string SourceLanguage { get; set; } = "auto";
        public string TargetLanguage { get; set; } = "en";
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
            new LanguageItem { DisplayName = "德语", Value = "de" }, new LanguageItem { DisplayName = "俄语", Value = "ru" }
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
        // (命令声明保持不变)
        public RelayCommand SelectTextCommand { get; }
        public RelayCommand TranslateCommand { get; }
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
            // 初始化设计时所需的数据
            IsBusy = true;       // 让进度条在设计器中可见
            ProgressValue = 65;  // 设置一个预览用的进度值
            ProgressText = "65%"; // 设置预览用的文字
            }
        // ▼▼▼ 【核心修改】构造函数现在接收所有需要的服务接口 ▼▼▼
        public TranslatorViewModel(
            IWindowService windowService,
            ISettingsService settingsService,
            IAdvancedTextService advancedTextService,
            ICadLayoutService cadLayoutService,
            ApiRegistry apiRegistry)
            {
            // 将注入的服务赋值给私有字段
            _windowService = windowService ?? throw new ArgumentNullException(nameof(windowService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _advancedTextService = advancedTextService ?? throw new ArgumentNullException(nameof(advancedTextService));
            _cadLayoutService = cadLayoutService ?? throw new ArgumentNullException(nameof(cadLayoutService));
            _apiRegistry = apiRegistry ?? throw new ArgumentNullException(nameof(apiRegistry));

            // 初始化UI集合
            TextBlockList = new ObservableCollection<TextBlockViewModel>();
            ModelList = new ObservableCollection<ModelViewModel>();
            ApiProfiles = new ObservableCollection<ApiProfile>();
            LineSpacingOptions = new ObservableCollection<string>();
            BalanceHistory = new ObservableCollection<BalanceRecord>();
            ConcurrencyLevelOptions = new ObservableCollection<string>();

            ApiServiceOptions = _apiRegistry.Providers;

            // 初始化命令 (这部分代码不需要改变)
            SelectTextCommand = new RelayCommand(OnSelectText);
            TranslateCommand = new RelayCommand(OnTranslate, p => TextBlockList.Any() && !IsBusy);
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
            ViewApiDocumentationCommand = new RelayCommand(
    p => OnViewApiDocumentation(),
    p => !string.IsNullOrWhiteSpace(CurrentProvider?.ApiDocumentationUrl)
);

            // 加载初始设置
            LoadSettings();
            Log("欢迎使用CAD翻译工具箱。");
            }

        #endregion

        #region --- 核心方法 (翻译、选择、应用) ---
        private async void OnSelectText(object parameter)
            {
            try
                {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc == null)
                    {
                    await _windowService.ShowInformationDialogAsync("操作失败", "未找到活动的CAD文档。");
                    return;
                    }

                // ▼▼▼ 【核心修改】使用_windowService控制窗口 ▼▼▼
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
                            VerticalMode = p.VerticalMode
                            }).ToList();
                        LoadTextBlocks(textBlocks);
                        Log($"成功提取并分析了 {textBlocks.Count} 个段落。");
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
                // ▼▼▼ 【核心修改】使用_windowService控制窗口 ▼▼▼
                _windowService.ShowMainWindow();
                await UpdateTokenCountAsync();
                TranslateCommand.RaiseCanExecuteChanged();
                IsProgressIndeterminate = false;
                }
            }

        private async void OnTranslate(object parameter) { await ExecuteTranslation(TextBlockList.Where(item => string.IsNullOrWhiteSpace(item.TranslatedText) && !string.IsNullOrWhiteSpace(item.OriginalText)).ToList()); }

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
                }

            await ExecuteTranslation(itemsToRetry);
            }

        private async Task ExecuteTranslation(List<TextBlockViewModel> itemsToTranslate)
            {
            if (CurrentProvider == null || CurrentProfile == null) { await _windowService.ShowInformationDialogAsync("操作无效", "请先选择一个API配置。"); return; }

            // ▼▼▼ 【核心修改】处理ComboBox输入和选择的逻辑更新 ▼▼▼
            if (IsModelRequired)
                {
                string modelNameToUse = null;
                // 优先使用用户在可编辑框中输入的文本
                if (!string.IsNullOrWhiteSpace(CurrentModelInput) && (SelectedModel == null || CurrentModelInput != SelectedModel.Name))
                    {
                    modelNameToUse = CurrentModelInput.Trim();
                    }
                // 否则，使用下拉框中选中的项
                else if (SelectedModel != null)
                    {
                    modelNameToUse = SelectedModel.Name;
                    }

                if (string.IsNullOrWhiteSpace(modelNameToUse))
                    {
                    await _windowService.ShowInformationDialogAsync("操作无效", "必须指定一个模型才能进行翻译。");
                    return;
                    }

                CurrentProfile.LastSelectedModel = modelNameToUse;
                // 如果这个模型不在列表中，自动添加它
                if (ModelList.All(m => m.Name != modelNameToUse))
                    {
                    var newModelVm = new ModelViewModel { Name = modelNameToUse, IsFavorite = false };
                    ModelList.Insert(0, newModelVm); // 插入到最前面，让用户能立刻看到
                    CurrentProfile.Models.Add(modelNameToUse);
                    SelectedModel = newModelVm;
                    }
                }

            IsBusy = true;
            IsProgressIndeterminate = true;
            ClearAllRowHighlights();
            var totalStopwatch = new System.Diagnostics.Stopwatch();
            totalStopwatch.Start();
            Log("翻译任务开始", clearPrevious: true);

            int totalItems = itemsToTranslate.Count;
            if (totalItems == 0) { Log("没有需要翻译的新内容。"); IsBusy = false; return; }

            int completedItems = 0;
            UpdateProgress(completedItems, totalItems);

            var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;

            try
                {
                IsProgressIndeterminate = false;
                ITranslator translator = _apiRegistry.CreateProviderForProfile(CurrentProfile);
                if (IsMultiThreadingEnabled)
                    {
                    // ▼▼▼ 【核心修改】多线程逻辑全面重构 ▼▼▼
                    #region --- 多线程翻译逻辑 ---
                    int concurrencyLevel = int.TryParse(CurrentConcurrencyLevelInput, out int userLevel) && userLevel > 1 ? userLevel : 2;
                    Log($"启动并发翻译，总数: {totalItems}，最大并发量: {concurrencyLevel}");

                    // 1. 将所有任务分批
                    var batches = itemsToTranslate
                        .Select((item, index) => new { item, index })
                        .GroupBy(x => x.index / concurrencyLevel)
                        .Select(g => g.Select(x => x.item).ToList())
                        .ToList();

                    int batchNumber = 0;
                    foreach (var batch in batches)
                        {
                        batchNumber++;
                        string batchPrefix = $"第 {batchNumber}/{batches.Count} 批";
                        Log($"{batchPrefix} 开始处理，包含 {batch.Count} 个项目...");

                        var batchStopwatch = new System.Diagnostics.Stopwatch();
                        batchStopwatch.Start();

                        // 2. 为当前批次创建一个共享的计时器
                        var timerCts = new CancellationTokenSource();
                        var timerTask = Task.Run(async () =>
                        {
                            while (!timerCts.IsCancellationRequested)
                                {
                                await Task.Delay(1000, timerCts.Token);
                                // 计时器每秒更新所有仍在翻译中的批内成员的状态
                                for (int i = 0; i < batch.Count; i++)
                                    {
                                    var item = batch[i];
                                    if (item.Status == TranslationStatus.Translating)
                                        {
                                        string itemSentenceNumber = $"第 {i + 1} 句";
                                        _windowService.InvokeOnUIThread(() => item.TranslatedText = $"{batchPrefix} {itemSentenceNumber}: 翻译中... 已用时 {batchStopwatch.Elapsed.TotalSeconds:F0} 秒");
                                        }
                                    }
                                }
                        }, timerCts.Token);

                        // 3. 准备当前批次的所有翻译任务
                        var translationTasksInBatch = batch.Select(async (item, index) => // <-- 这里增加了 index
                        {
                            try
                                {
                                // 3.1 任务开始前，立即更新UI
                                string itemSentenceNumber = $"第 {index + 1} 句"; // <-- 新增：创建句内编号
                                _windowService.ScrollToGridItem(item);
                                SetItemStatus(item, TranslationStatus.Translating);
                                item.TranslatedText = $"{batchPrefix} {itemSentenceNumber}: 准备中..."; // <-- 这里使用了句内编号

                                // 3.2 执行翻译
                                string result = await CreateTranslationTask(translator, item, cancellationToken);

                                var matchResult = FindLegendPosMatch(result); // 使用新的方法
                                if (item.OriginalText.Contains(AdvancedTextService.LegendPlaceholder) && matchResult.Success)
                                    {
                                    // 根据雷达报告，决定是否添加引号
                                    string finalJigPlaceholder = matchResult.IsQuoted
                                        ? $"\"{AdvancedTextService.JigPlaceholder}\""
                                        : AdvancedTextService.JigPlaceholder;

                                    result = result.Remove(matchResult.Index, matchResult.Length).Insert(matchResult.Index, finalJigPlaceholder);
                                    }

                                SetItemStatus(item, TranslationStatus.Success);
                                item.TranslatedText = result;
                                }
                            catch (Exception ex)
                                {
                                // 3.4 失败后更新UI (不会中断其他任务)
                                SetItemStatus(item, TranslationStatus.Failed);
                                HandleApiException(ex as ApiException ?? new ApiException(ApiErrorType.Unknown, CurrentProvider.ServiceType, ex.Message), item);
                                }
                            finally
                                {
                                // 3.5 更新总体进度
                                int currentCompleted = Interlocked.Increment(ref completedItems);
                                _windowService.InvokeOnUIThread(() =>
                                {
                                    UpdateProgress(currentCompleted, totalItems);
                                    ApplyToCadCommand.RaiseCanExecuteChanged();
                                });
                                }
                        }).ToList();

                        // 4. 等待当前批次的所有任务完成
                        await Task.WhenAll(translationTasksInBatch);

                        // 5. 停止当前批次的计时器
                        batchStopwatch.Stop();
                        timerCts.Cancel();
                        try { await timerTask; } catch (OperationCanceledException) { }
                        Log($"{batchPrefix} 处理完成。用时 {batchStopwatch.Elapsed.TotalSeconds:F1} 秒。");
                        }
                    #endregion
                    }
                else
                    {
                    // (单线程逻辑保持不变)
                    #region --- 单线程翻译逻辑 ---
                    Log($"启动单线程翻译，总数: {totalItems}");
                    foreach (var item in itemsToTranslate)
                        {
                        _windowService.ScrollToGridItem(item);
                        SetItemStatus(item, TranslationStatus.Translating);
                        string statusPrefix = $"第 {completedItems + 1}/{totalItems} 项";
                        item.TranslatedText = $"{statusPrefix}: 发送请求...";
                        var stopwatch = new System.Diagnostics.Stopwatch();
                        stopwatch.Start();
                        var timerCts = new CancellationTokenSource();
                        var timerTask = Task.Run(async () =>
                        {
                            while (!timerCts.IsCancellationRequested)
                                {
                                await Task.Delay(1000, timerCts.Token);
                                if (item.Status == TranslationStatus.Translating)
                                    {
                                    _windowService.InvokeOnUIThread(() => item.TranslatedText = $"{statusPrefix}: 翻译中... 已用时 {stopwatch.Elapsed.TotalSeconds:F0} 秒");
                                    }
                                }
                        }, timerCts.Token);
                        try
                            {
                            string result = await CreateTranslationTask(translator, item, cancellationToken);
                            var matchResult = FindLegendPosMatch(result); // 使用新的方法
                                                                          // 检查原始文本是否包含图例，以及翻译结果是否也模糊匹配到了图例
                            if (item.OriginalText.Contains(AdvancedTextService.LegendPlaceholder) && matchResult.Success)
                                {
                                // 根据雷达报告，决定是否添加引号
                                string finalJigPlaceholder = matchResult.IsQuoted
                                    ? $"\"{AdvancedTextService.JigPlaceholder}\""
                                    : AdvancedTextService.JigPlaceholder;

                                result = result.Remove(matchResult.Index, matchResult.Length).Insert(matchResult.Index, finalJigPlaceholder);
                                }
                            item.TranslatedText = result;
                            SetItemStatus(item, TranslationStatus.Success);
                            }
                        catch (Exception ex)
                            {
                            timerCts.Cancel();
                            HandleApiException(ex as ApiException ?? new ApiException(ApiErrorType.Unknown, CurrentProvider.ServiceType, ex.Message), item);
                            SetItemStatus(item, TranslationStatus.Failed);
                            Log("任务因错误而中断。");
                            break;
                            }
                        finally
                            {
                            stopwatch.Stop();
                            timerCts.Cancel();
                            try { await timerTask; } catch (OperationCanceledException) { }
                            completedItems++;
                            UpdateProgress(completedItems, totalItems);
                            Log($"{statusPrefix} 处理完成。用时 {stopwatch.Elapsed.TotalSeconds:F1} 秒。");
                            _windowService.InvokeOnUIThread(() => ApplyToCadCommand.RaiseCanExecuteChanged());
                            }
                        }
                    #endregion
                    }
                }
            catch (ApiException apiEx)
                {
                Log($"[错误] 创建翻译服务时失败: {apiEx.Message}", isError: true);
                await _windowService.ShowInformationDialogAsync("配置错误", $"创建翻译服务时失败，请检查API配置：\n\n{apiEx.Message}");
                }
            catch (Exception ex)
                {
                Log($"[错误] 翻译任务发生未知异常: {ex.Message}", isError: true);
                await _windowService.ShowInformationDialogAsync("未知错误", $"翻译过程中发生未知错误:\n\n{ex.Message}");
                }
            finally
                {
                totalStopwatch.Stop();
                if (cts.IsCancellationRequested) { Log("任务因连续错误而熔断。"); }
                ClearAllRowHighlights();
                if (_failedItems.Any()) { Log($"任务完成，有 {_failedItems.Count} 个项目失败或被取消。总用时 {totalStopwatch.Elapsed.TotalSeconds:F1} 秒"); }
                else { Log($"全部翻译任务成功完成！总用时 {totalStopwatch.Elapsed.TotalSeconds:F1} 秒"); }
                if (totalItems > 0 && !_failedItems.Any()) { UpdateUsageStatistics(itemsToTranslate.Count, itemsToTranslate.Sum(i => i.OriginalText.Length), totalStopwatch.Elapsed.TotalSeconds); }
                IsBusy = false;
                IsProgressIndeterminate = false;
                cts.Dispose();
                }
            }

        private async void OnApplyToCad(object parameter)
            {
            // 1. 检查是否有可应用的内容
            var validItems = TextBlockList.Where(item => !string.IsNullOrWhiteSpace(item.TranslatedText) && !item.TranslatedText.StartsWith("["))
                                          .ToList();
            if (!validItems.Any())
                {
                await _windowService.ShowInformationDialogAsync("无内容可应用", "没有有效的翻译文本可供写入CAD。");
                return;
                }

            Log("准备将翻译应用到CAD...");

            // 2. 【核心修改】将数据存放到静态桥梁中
            CadBridgeService.TextBlocksToLayout = new ObservableCollection<TextBlockViewModel>(TextBlockList);

            // 3. 【核心修改】隐藏WPF窗口，而不是最小化
            _windowService.MinimizeMainWindow();

            // 4. 等待一小段时间，确保WPF窗口完成隐藏动画，让CAD窗口能平滑地获得焦点
            //await Task.Delay(200);

            // 5. 【核心修改】通过桥梁服务，要求AutoCAD执行内部命令
            CadBridgeService.SendCommandToAutoCAD("WZPB_APPLY\n"); // WZPB_APPLY 是我们的新内部命令

            // 注意：这里不再有 try-catch 和 finally，也不再有重新激活窗口的逻辑。
            // 因为从现在起，控制权已经完全交给AutoCAD了。
            }
        #endregion

        #region --- API与模型管理 ---

        private async void OnGetModels(object parameter)
            {
            if (CurrentProvider == null) return;
            IsBusy = true;
            Log($"正在从 {CurrentProvider.DisplayName} 获取模型列表...");

            // 【核心修改】创建带10秒超时的CancellationTokenSource
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            try
                {
                var provider = _apiRegistry.CreateProviderForProfile(CurrentProfile);
                // 【核心修改】将Token传递给服务
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
            // 【核心修改】专门捕获操作取消（包括超时）的异常
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
                cts.Dispose(); // 【核心修改】释放资源
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
            // ◄◄◄ 【新增】捕获我们自定义的ApiException
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

        private async void OnManageModels(object parameter)
            {
            if (CurrentProfile == null)
                {
                await _windowService.ShowInformationDialogAsync("操作无效", "请先选择一个API配置。");
                return;
                }
            // 【核心修改】传入常用模型列表
            var modelManagementVM = new ModelManagementViewModel(CurrentProfile.ProfileName, new List<string>(CurrentProfile.Models), new List<string>(CurrentProfile.FavoriteModels));

            var dialogResult = _windowService.ShowModelManagementDialog(modelManagementVM);

            if (dialogResult == true)
                {
                // 【核心修改】接收包含常用模型的结果
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

                RefreshModelList(); // 调用新的刷新方法
                SaveSettings();
                Log("模型列表已更新。");
                }
            }

        private async void OnManageApiDefinitions(object parameter)
            {
            // 这部分逻辑暂时不变，因为它本来就是创建新的ViewModel和Window
            // 如果要彻底解耦，也需要通过IWindowService来做
            var vm = new ApiDefinitionViewModel();
            var window = new ApiDefinitionWindow(vm); // 暂时保留，后续可一并改造

            // 为了避免错误，我们不再设置Owner
            // window.Owner = Application.Current.MainWindow; 

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
                // 这会调用系统的默认浏览器来打开链接
                System.Diagnostics.Process.Start(CurrentProvider.ApiDocumentationUrl);
                }
            catch (Exception ex)
                {
                Log($"[错误] 无法打开帮助文档链接: {ex.Message}", isError: true);
                // 如果需要，这里可以弹出一个错误提示框
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


            _isLoading = false;

            }

        private void SaveSettings()
            {
            if (_isLoading) return;
            if (_currentSettings == null) _currentSettings = new AppSettings();

            // 【新增】确保保存的是下拉框中最终选定的模型名称
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

            _settingsService.SaveSettings(_currentSettings);
            }

        private void UpdateUiFromCurrentProfile()
            {
            if (CurrentProfile == null) return;

            // 重新订阅事件，确保在文本框里修改密钥等信息时，能被正确保存
            CurrentProfile.PropertyChanged -= OnCurrentProfilePropertyChanged;
            CurrentProfile.PropertyChanged += OnCurrentProfilePropertyChanged;

            // 刷新模型列表的下拉框
            RefreshModelList();

            // 【这是被错误删除的关键代码】通知UI：整个Profile对象已经更换，请刷新所有相关绑定
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

            // 2. 如果没找到，再查找“总余额”相关的规则
            if (balanceMapping == null)
                {
                balanceMapping = _currentSettings.FriendlyNameMappings.FirstOrDefault(m => m.Key == "CanonicalRemainingBalance").Value;
                }

            if (balanceMapping != null)
                {
                // 在记录的Data中，查找第一个匹配别名列表的Key
                var balancePair = lastRecord.Data.FirstOrDefault(d => balanceMapping.Aliases.Contains(d.Key));

                // 如果找到了，就只显示它的值，否则提示未找到
                LastBalanceDisplay = balancePair.Value ?? "未找到余额信息";
                }
            else
                {
                // 如果两种余额规则都没有配置，才显示这个最终提示
                LastBalanceDisplay = "未配置余额规则";
                }
            }

        private Task<string> CreateTranslationTask(ITranslator translator, TextBlockViewModel item, CancellationToken cancellationToken = default)
            {
            string textToTranslate = item.OriginalText;
            if (translator.IsPromptSupported && !string.IsNullOrWhiteSpace(GlobalPrompt))
                {
                textToTranslate = $"{GlobalPrompt}\n\n{item.OriginalText}";
                }
            // 将cancellationToken传递给真正的翻译服务
            return translator.TranslateAsync(textToTranslate, SourceLanguage, TargetLanguage, cancellationToken);
            }


        private void Log(string message, bool clearPrevious = false, bool addNewLine = true, bool isListItem = false, bool isError = false)
            {
            if (clearPrevious)
                _windowService.InvokeOnUIThread(() => StatusLog.Clear());

            // 核心修改：在这里追加Token信息
            string tokenInfo = _isTokenCountAvailable ? $"，预计Tokens: {_totalTokens}" : "";
            var formattedMessage = isListItem ? message : $"[{DateTime.Now:HH:mm:ss}] {message}{tokenInfo}";

            if (isError)
                {
                formattedMessage = $"[错误] {formattedMessage}";
                }

            CadBridgeService.WriteToCommandLine(formattedMessage);

            if (addNewLine) _windowService.InvokeOnUIThread(() => StatusLog.Add(formattedMessage));    
            }

        private void UpdateLastLog(string message)
            {
            CadBridgeService.UpdateLastMessageOnCommandLine(message);

            if (StatusLog.Any())
                {
                _windowService.InvokeOnUIThread(() => StatusLog[StatusLog.Count - 1] = message);
                }
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

            // 核心修改：在这里追加Token信息
            string tokenInfo = _isTokenCountAvailable ? $" (Tokens: {_totalTokens})" : "";
            ProgressText = IsMultiThreadingEnabled ? $"({completed}/{total}){tokenInfo}" : $"({completed}/{total}) {ProgressValue}%{tokenInfo}";
            }

        private void HandleTranslationError(Exception ex, TextBlockViewModel item, System.Diagnostics.Stopwatch stopwatch, int completedItems)
            {
            if (stopwatch.IsRunning) stopwatch.Stop();
            string errorMessage = ex.Message.Replace('\t', ' ');
            UpdateLastLog($"[{DateTime.Now:HH:mm:ss}] [翻译失败] 第 {completedItems + 1} 项，原因: {errorMessage}");
            Log("任务因错误而中断。");
            item.TranslatedText = $"[翻译失败] {errorMessage}";
            }

        private async Task UpdateTokenCountAsync()
            {
            // 检查当前API是否支持此功能，以及列表是否有内容
            if (CurrentProvider == null || !CurrentProvider.IsTokenCountSupported || !TextBlockList.Any())
                {
                _isTokenCountAvailable = false;
                return;
                }

            try
                {
                // 创建一个临时的翻译器实例
                var translator = _apiRegistry.CreateProviderForProfile(CurrentProfile);
                // 合并所有原文
                string combinedText = string.Join("\n", TextBlockList.Select(b => b.OriginalText));

                // 异步计算Token
                _totalTokens = await translator.CountTokensAsync(combinedText);
                _isTokenCountAvailable = true;
                }
            catch (Exception ex)
                {
                // 如果计算失败，则标记为不可用，并在后台日志中记录错误
                _isTokenCountAvailable = false;
                Log($"[Token计算失败] {ex.Message}", isError: true, addNewLine: false);
                }
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
                // 在译文栏中显示最终的错误信息
                failedItem.TranslatedText = $"{errorPrefix} {apiEx.Message}";
                // 将失败项加入重试列表
                lock (_failedItems) { _failedItems.Add(failedItem); }
                // 更新重试按钮的状态
                _windowService.InvokeOnUIThread(() => RetranslateFailedCommand.RaiseCanExecuteChanged());
                }

            Log($"[{apiEx.Provider}] {errorPrefix} {apiEx.Message} (Status: {apiEx.StatusCode}, Code: {apiEx.ApiErrorCode})", isError: true);
            }

        #endregion

        #region --- 状态与高亮辅助方法 ---

        /// <summary>
        /// 【新增】统一设置某个数据项的翻译状态和对应的背景色。
        /// </summary>
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

        /// <summary>
        /// 【新增】清除表格中所有行的背景高亮。
        /// </summary>
        private void ClearAllRowHighlights()
            {
            foreach (var item in TextBlockList)
                {
                SetItemStatus(item, TranslationStatus.Idle);
                }
            }
        private (bool Success, int Index, int Length, bool IsQuoted) FindLegendPosMatch(string text)
            {
            var regex = new System.Text.RegularExpressions.Regex(@"[\W_]*LEGEND[\W_]*POS[\W_\d]*", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var match = regex.Match(text);

            if (!match.Success)
                {
                return (false, 0, 0, false);
                }

            // 获取匹配到的完整字符串
            string matchedValue = match.Value;

            // 判断匹配到的内容是否以 " 开头并且以 " 结尾
            bool isQuoted = matchedValue.Trim().StartsWith("\"") && matchedValue.Trim().EndsWith("\"");

            return (true, match.Index, match.Length, isQuoted);
            }
        #endregion

        #region --- 表格操作与属性变更 ---
        // (这部分方法也不需要改变)
        // ... OnMerge, OnDelete, OnSplit 等方法 ...
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
            await UpdateTokenCountAsync(); // 现在这行代码是正确的
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
                await UpdateTokenCountAsync(); // 现在这行代码是正确的
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

            // 【核心修正】为拆分后的新行创建一个唯一的“团队身份证”
            var newGroupKey = Guid.NewGuid().ToString();

            var newBlocks = new List<TextBlockViewModel>();
            for (int i = 0; i < linesToSplit.Count; i++)
                {
                var newBlock = new TextBlockViewModel
                    {
                    OriginalText = linesToSplit[i],
                    GroupKey = newGroupKey // 为所有新行打上相同的烙印
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

            // 重新加载并应用所有显示逻辑
            var currentBlocksState = TextBlockList.ToList();
            LoadTextBlocks(currentBlocksState);
            await UpdateTokenCountAsync(); // 现在这行代码是正确的
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
                await UpdateTokenCountAsync();
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
                    CurrentConcurrencyLevelInput = "2"; // 默认回退到 "2"
                    }
                SaveSettings();
                Log($"已删除并发量选项: {optionToDelete}");
                }
            }

        private void OnViewHistory(object parameter)
            {
            var historyViewModel = new BalanceHistoryViewModel(this.BalanceHistory, _currentSettings);

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

            // 步骤 1: 完整填充ViewModel列表
            foreach (var block in blocks)
                {
                var newVm = new TextBlockViewModel
                    {
                    OriginalText = block.OriginalText,
                    TranslatedText = block.TranslatedText,
                    SourceObjectIds = block.SourceObjectIds,
                    AssociatedGraphicsBlockId = block.AssociatedGraphicsBlockId,
                    OriginalAnchorPoint = block.OriginalAnchorPoint,
                    OriginalSpaceCount = block.OriginalSpaceCount,
                    Position = block.Position,
                    AlignmentPoint = block.AlignmentPoint,
                    HorizontalMode = block.HorizontalMode,
                    VerticalMode = block.VerticalMode,
                    IsTitle = block.IsTitle,
                    GroupKey = block.GroupKey
                    };
                TextBlockList.Add(newVm);
                }

            // 步骤 2: 第一次遍历，设置基础“编号”和样式
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

            // 步骤 3: 在智能序号的基础上，应用分组显示格式
            var groups = TextBlockList.Where(vm => !string.IsNullOrEmpty(vm.GroupKey)).GroupBy(vm => vm.GroupKey).ToList();
            foreach (var group in groups)
                {
                var members = group.OrderBy(m => TextBlockList.IndexOf(m)).ToList();
                if (!members.Any()) continue;

                // 【核心修正】找到父级的正确基础编号
                var parent = members.First();
                string parentNumber = parent.Character;

                // 【核心修正】如果父级的基础编号是“无”，则尝试从组ID（即原始父级的ID）找到正确的编号
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

            // 步骤 4: 设置背景色并最终刷新
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

        #region --- INotifyPropertyChanged 实现 ---
        // (这部分代码不需要改变)
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