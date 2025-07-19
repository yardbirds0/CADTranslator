// 文件路径: CADTranslator/UI/ViewModels/TranslatorViewModel.cs
// 【注意】这是一个完整的文件替换

using Autodesk.AutoCAD.ApplicationServices; // ◄◄◄ 【注意】为了获取Editor，这里保留
using CADTranslator.Models;
using CADTranslator.Services;
using CADTranslator.UI.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace CADTranslator.UI.ViewModels
    {
    public class TranslatorViewModel : INotifyPropertyChanged
        {
        #region --- 字段与服务 ---

        // ▼▼▼ 【核心修改】所有服务都通过接口引用 ▼▼▼
        private readonly IWindowService _windowService;
        private readonly ISettingsService _settingsService;
        private readonly IAdvancedTextService _advancedTextService;
        private readonly ICadLayoutService _cadLayoutService;
        private readonly ApiRegistry _apiRegistry;

        private AppSettings _currentSettings;
        private bool _isLoading = false;
        private List<ObjectId> _deletableSourceIds = new List<ObjectId>();
        private List<TextBlockViewModel> _failedItems = new List<TextBlockViewModel>();

        // ... (其他私有字段保持不变) ...
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
            (Brush)new BrushConverter().ConvertFromString("#1E88E5"),
            (Brush)new BrushConverter().ConvertFromString("#0CA678"),
            (Brush)new BrushConverter().ConvertFromString("#FF8F00"),
            (Brush)new BrushConverter().ConvertFromString("#FF5252"),
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

        public ObservableCollection<string> ModelList { get; set; }
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
        #endregion

        #region --- 构造函数 ---

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
            ModelList = new ObservableCollection<string>();
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
            DeleteConcurrencyOptionCommand = new RelayCommand(OnDeleteConcurrencyOption, p => p is string option && option != "2" && option != "5");
            ManageApiDefinitionsCommand = new RelayCommand(OnManageApiDefinitions);

            // 加载初始设置
            LoadSettings();
            Log("欢迎使用CAD翻译工具箱。");
            }

        #endregion

        #region --- 核心方法 (翻译、选择、应用) ---
        // (这部分方法除了使用 _windowService 外，逻辑几乎不变，因为它们已经调用了其他服务)
        // ... OnSelectText, OnTranslate, OnApplyToCad 等方法 ...
        private async void OnSelectText(object parameter)
            {
            try
                {
                var doc = Application.DocumentManager.MdiActiveDocument;
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

                    List<ParagraphInfo> paragraphInfos = _advancedTextService.ExtractAndProcessParagraphs(selRes.Value, out _deletableSourceIds);

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
                TranslateCommand.RaiseCanExecuteChanged();
                }
            }

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
                }

            await ExecuteTranslation(itemsToRetry);
            }

        private async Task ExecuteTranslation(List<TextBlockViewModel> itemsToTranslate)
            {
            if (CurrentProvider == null || CurrentProfile == null)
                {
                await _windowService.ShowInformationDialogAsync("操作无效", "请先选择一个API配置。");
                return;
                }

            if (IsModelRequired && !string.IsNullOrWhiteSpace(CurrentModelInput))
                {
                CurrentProfile.LastSelectedModel = CurrentModelInput;
                if (!ModelList.Contains(CurrentModelInput))
                    {
                    ModelList.Add(CurrentModelInput);
                    CurrentProfile.Models.Add(CurrentModelInput);
                    }
                }

            IsBusy = true;
            var totalStopwatch = new System.Diagnostics.Stopwatch();
            totalStopwatch.Start();
            Log("翻译任务开始", clearPrevious: true);

            int totalItems = itemsToTranslate.Count;
            if (totalItems == 0)
                {
                Log("没有需要翻译的新内容。");
                IsBusy = false;
                return;
                }

            int completedItems = 0;
            UpdateProgress(completedItems, totalItems);

            var cts = new CancellationTokenSource();
            var cancellationToken = cts.Token;
            int consecutiveNetworkErrors = 0;
            const int networkErrorThreshold = 2;

            try
                {
                ITranslator translator = _apiRegistry.CreateProviderForProfile(CurrentProfile);
                if (IsMultiThreadingEnabled)
                    {
                    int concurrencyLevel = int.TryParse(CurrentConcurrencyLevelInput, out int userLevel) && userLevel > 1 ? userLevel : 2;
                    Log($"启动并发翻译，总数: {totalItems}，最大并发量: {concurrencyLevel}");
                    var semaphore = new SemaphoreSlim(concurrencyLevel);

                    var translationTasks = itemsToTranslate.Select(async item =>
                    {
                        await semaphore.WaitAsync(cancellationToken);

                        try
                            {
                            cancellationToken.ThrowIfCancellationRequested();
                            string result = await CreateTranslationTask(translator, item, cancellationToken);
                            item.TranslatedText = result;
                            Interlocked.Exchange(ref consecutiveNetworkErrors, 0);
                            }
                        catch (OperationCanceledException)
                            {
                            item.TranslatedText = "[已取消] 因连续网络错误或致命错误，任务已熔断。";
                            lock (_failedItems) { _failedItems.Add(item); }
                            }
                        catch (ApiException apiEx)
                            {
                            bool isFatalError = apiEx.ErrorType == ApiErrorType.ConfigurationError || apiEx.StatusCode == System.Net.HttpStatusCode.Unauthorized;
                            if (isFatalError)
                                {
                                if (!cts.IsCancellationRequested)
                                    {
                                    Log($"检测到致命错误 ({apiEx.Message})，正在触发熔断...", isError: true);
                                    cts.Cancel();
                                    }
                                }
                            else if (apiEx.ErrorType == ApiErrorType.NetworkError)
                                {
                                int currentErrorCount = Interlocked.Increment(ref consecutiveNetworkErrors);
                                if (currentErrorCount >= networkErrorThreshold && !cts.IsCancellationRequested)
                                    {
                                    Log($"已连续遇到 {currentErrorCount} 次网络错误，正在触发熔断...", isError: true);
                                    cts.Cancel();
                                    }
                                }
                            HandleApiException(apiEx, item);
                            }
                        catch (Exception ex)
                            {
                            var errorMessage = ex.Message.Replace('\t', ' ');
                            item.TranslatedText = $"[未知错误] {errorMessage}";
                            lock (_failedItems) { _failedItems.Add(item); }
                            _windowService.InvokeOnUIThread(() => RetranslateFailedCommand.RaiseCanExecuteChanged());
                            }
                        finally
                            {
                            semaphore.Release();
                            int currentCompleted = Interlocked.Increment(ref completedItems);
                            _windowService.InvokeOnUIThread(() =>
                            {
                                UpdateProgress(currentCompleted, totalItems);
                                ApplyToCadCommand.RaiseCanExecuteChanged();
                            });
                            }
                    });

                    // ▼▼▼ 【核心修复】使用 Task.WhenAny 与一个“取消任务”进行竞赛 ▼▼▼
                    var whenAllTask = Task.WhenAll(translationTasks);
                    var cancellationTaskCompletionSource = new TaskCompletionSource<bool>();
                    using (cancellationToken.Register(() => cancellationTaskCompletionSource.TrySetResult(true)))
                        {
                        var completedTask = await Task.WhenAny(whenAllTask, cancellationTaskCompletionSource.Task);
                        if (completedTask == cancellationTaskCompletionSource.Task)
                            {
                            // 如果是“取消任务”先完成了，说明熔断已被触发。
                            // 我们立刻记录日志，然后直接跳出等待，让 finally 块执行。
                            Log("熔断已触发，停止等待所有任务完成。");
                            }
                        else
                            {
                            // 如果是 whenAllTask 先完成了，说明所有任务都正常结束了（没有触发熔断）。
                            // 我们需要 await 它来传播可能发生的、未被内部捕获的异常。
                            await whenAllTask;
                            }
                        }
                    }
                else
                    {
                    // ... (单线程代码保持不变) ...
                    Log($"启动单线程翻译，总数: {totalItems}");
                    foreach (var item in itemsToTranslate)
                        {
                        var stopwatch = new System.Diagnostics.Stopwatch();
                        string initialLog = $"[{DateTime.Now:HH:mm:ss}] -> 第 {completedItems + 1}/{totalItems} 项翻译中...";
                        Log(initialLog, addNewLine: true, isListItem: true);

                        Task<string> translationTask = CreateTranslationTask(translator, item);
                        stopwatch.Start();

                        while (!translationTask.IsCompleted)
                            {
                            await Task.Delay(500);
                            if (!translationTask.IsCompleted) UpdateLastLog($"{initialLog} 已进行 {(int)stopwatch.Elapsed.TotalSeconds} 秒");
                            }
                        stopwatch.Stop();

                        try
                            {
                            string result = await translationTask;
                            item.TranslatedText = result;
                            completedItems++;
                            UpdateProgress(completedItems, totalItems);
                            UpdateLastLog($"[{DateTime.Now:HH:mm:ss}] -> 第 {completedItems}/{totalItems} 项翻译完成。用时 {stopwatch.Elapsed.TotalSeconds:F1} 秒");
                            _windowService.InvokeOnUIThread(() => ApplyToCadCommand.RaiseCanExecuteChanged());
                            }
                        catch (ApiException apiEx)
                            {
                            HandleApiException(apiEx, item);
                            UpdateLastLog($"[{DateTime.Now:HH:mm:ss}] [翻译失败] 第 {completedItems + 1} 项，原因: {apiEx.Message}");
                            Log("任务因错误而中断。");
                            return;
                            }
                        catch (Exception ex)
                            {
                            HandleTranslationError(ex, item, stopwatch, completedItems);
                            return;
                            }
                        }
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
                if (cts.IsCancellationRequested)
                    {
                    Log("任务已熔断。部分项目可能未被处理。");
                    }
                if (_failedItems.Any())
                    {
                    Log($"任务完成，有 {_failedItems.Count} 个项目翻译失败或被取消。总用时 {totalStopwatch.Elapsed.TotalSeconds:F1} 秒");
                    }
                else
                    {
                    Log($"全部翻译任务成功完成！总用时 {totalStopwatch.Elapsed.TotalSeconds:F1} 秒");
                    }
                if (totalItems > 0 && !_failedItems.Any())
                    {
                    UpdateUsageStatistics(itemsToTranslate.Count, itemsToTranslate.Sum(i => i.OriginalText.Length), totalStopwatch.Elapsed.TotalSeconds);
                    }

                IsBusy = false;
                cts.Dispose();
                }
            }

        private async void OnApplyToCad(object parameter)
            {
            Log("正在切换到CAD窗口并应用翻译...");
            _windowService.MinimizeMainWindow();
            await Task.Delay(200);
            bool success = false;
            try
                {
                if (IsLiveLayoutEnabled)
                    {
                    Log("“实时排版”已启用，将执行智能布局...");
                    success = _cadLayoutService.ApplySmartLayoutToCad(TextBlockList, _deletableSourceIds, CurrentLineSpacingInput);
                    }
                else
                    {
                    Log("“实时排版”已关闭，将使用基本布局...");
                    success = await Task.Run(() => _cadLayoutService.ApplyTranslationToCad(TextBlockList));
                    }
                }
            catch (Exception ex)
                {
                Log($"[错误] 应用到CAD时发生意外异常: {ex.Message}");
                success = false;
                }
            finally
                {
                if (!success)
                    {
                    _windowService.ActivateMainWindow();
                    }
                if (success)
                    {
                    Log("成功将所有翻译应用到CAD图纸！");
                    }
                else
                    {
                    Log("[错误] 应用到CAD失败，请检查CAD命令行获取详细信息。");
                    }
                }
            }
        #endregion

        #region --- API与模型管理 ---

        private async void OnGetModels(object parameter)
            {
            if (CurrentProvider == null) return;
            IsBusy = true;
            Log($"正在从 {CurrentProvider.DisplayName} 获取模型列表...");
            try
                {
                var provider = _apiRegistry.CreateProviderForProfile(CurrentProfile);
                var models = await provider.GetModelsAsync();

                if (models != null && models.Any())
                    {
                    ModelList.Clear();
                    CurrentProfile.Models.Clear();
                    models.ForEach(m => { ModelList.Add(m); CurrentProfile.Models.Add(m); });
                    CurrentModelInput = ModelList.FirstOrDefault();
                    OnPropertyChanged(nameof(CurrentModelInput));
                    SaveSettings();
                    Log($"成功获取 {models.Count} 个模型！列表已更新。");
                    }
                else
                    {
                    Log("未能获取到任何模型列表。");
                    }
                }
            // ◄◄◄ 【新增】捕获我们自定义的ApiException
            catch (ApiException apiEx)
                {
                // 这里不需要向某个UI项写入错误，所以第二个参数传null
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
            var modelManagementVM = new ModelManagementViewModel(CurrentProfile.ProfileName, new List<string>(CurrentProfile.Models));

            var dialogResult = _windowService.ShowModelManagementDialog(modelManagementVM);

            if (dialogResult == true)
                {
                var finalModels = modelManagementVM.GetFinalModels();
                CurrentProfile.Models.Clear();
                finalModels.ForEach(m => CurrentProfile.Models.Add(m));
                ModelList.Clear();
                finalModels.ForEach(m => ModelList.Add(m));
                if (modelManagementVM.SelectedModel != null && !string.IsNullOrWhiteSpace(modelManagementVM.SelectedModel.Name))
                    {
                    string selectedModelName = modelManagementVM.SelectedModel.Name.Trim();
                    CurrentModelInput = selectedModelName;
                    CurrentProfile.LastSelectedModel = selectedModelName;
                    }
                else
                    {
                    CurrentProfile.LastSelectedModel = finalModels.FirstOrDefault();
                    CurrentModelInput = CurrentProfile.LastSelectedModel;
                    }
                OnPropertyChanged(nameof(CurrentModelInput));
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
            _currentSettings.LineSpacingPresets.ForEach(p => LineSpacingOptions.Add(p));
            CurrentLineSpacingInput = _currentSettings.LastSelectedLineSpacing;

            BalanceHistory.Clear();
            _currentSettings.BalanceHistory.OrderByDescending(r => r.Timestamp).ToList().ForEach(r => BalanceHistory.Add(r));
            UpdateBalanceDisplayForCurrentProvider();

            IsMultiThreadingEnabled = _currentSettings.IsMultiThreadingEnabled;
            ConcurrencyLevelOptions.Clear();
            _currentSettings.ConcurrencyPresets.ForEach(p => ConcurrencyLevelOptions.Add(p));
            CurrentConcurrencyLevelInput = _currentSettings.LastSelectedConcurrency;

            _isLoading = false;
            }

        private void SaveSettings()
            {
            if (_isLoading) return;
            if (_currentSettings == null) _currentSettings = new AppSettings();

            _currentSettings.IsLiveLayoutEnabled = IsLiveLayoutEnabled;
            _currentSettings.LastSelectedLineSpacing = CurrentLineSpacingInput;
            _currentSettings.LineSpacingPresets = LineSpacingOptions.ToList();
            _currentSettings.IsMultiThreadingEnabled = IsMultiThreadingEnabled;
            _currentSettings.LastSelectedConcurrency = CurrentConcurrencyLevelInput;
            _currentSettings.ConcurrencyPresets = ConcurrencyLevelOptions.ToList();
            _currentSettings.ApiProfiles = ApiProfiles.ToList();
            _currentSettings.BalanceHistory = BalanceHistory.ToList();

            if (CurrentProvider != null)
                _currentSettings.LastSelectedApiService = CurrentProvider.ServiceType;

            _settingsService.SaveSettings(_currentSettings);
            }

        private void UpdateUiFromCurrentProfile()
            {
            if (CurrentProfile == null) return;

            CurrentProfile.PropertyChanged -= OnCurrentProfilePropertyChanged;
            CurrentProfile.PropertyChanged += OnCurrentProfilePropertyChanged;

            ModelList.Clear();
            if (CurrentProfile.Models != null)
                {
                CurrentProfile.Models.ForEach(m => ModelList.Add(m));
                }
            CurrentModelInput = CurrentProfile.LastSelectedModel;
            OnPropertyChanged(nameof(CurrentModelInput));
            OnPropertyChanged(nameof(CurrentProfile));
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

            var formattedMessage = isListItem ? message : $"[{DateTime.Now:HH:mm:ss}] {message}";
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
            ProgressText = IsMultiThreadingEnabled ? $"({completed}/{total})" : $"({completed}/{total}) {ProgressValue}%";
            }

        private void HandleTranslationError(Exception ex, TextBlockViewModel item, System.Diagnostics.Stopwatch stopwatch, int completedItems)
            {
            if (stopwatch.IsRunning) stopwatch.Stop();
            string errorMessage = ex.Message.Replace('\t', ' ');
            UpdateLastLog($"[{DateTime.Now:HH:mm:ss}] [翻译失败] 第 {completedItems + 1} 项，原因: {errorMessage}");
            Log("任务因错误而中断。");
            item.TranslatedText = $"[翻译失败] {errorMessage}";
            }

        private void HandleApiException(ApiException apiEx, TextBlockViewModel failedItem)
            {
            // 准备一个用户友好的错误消息前缀
            string errorPrefix = apiEx.ErrorType switch
                {
                    ApiErrorType.NetworkError => "[网络错误]",
                    ApiErrorType.ConfigurationError => "[配置错误]",
                    ApiErrorType.ApiError => "[接口返回错误]",
                    ApiErrorType.InvalidResponse => "[响应无效]",
                    _ => "[未知错误]"
                    };

            // 在译文栏中显示带前缀的用户友好消息
            if (failedItem != null)
                {
                failedItem.TranslatedText = $"{errorPrefix} {apiEx.Message}";
                // 将失败项加入重试列表
                lock (_failedItems) { _failedItems.Add(failedItem); }
                // 更新重试按钮的状态
                _windowService.InvokeOnUIThread(() => RetranslateFailedCommand.RaiseCanExecuteChanged());
                }

            // 在状态栏和CAD命令行中记录包含技术细节的完整日志
            Log($"[{apiEx.Provider}] {errorPrefix} {apiEx.Message} (Status: {apiEx.StatusCode}, Code: {apiEx.ApiErrorCode})", isError: true);
            }

        #endregion

        #region --- 表格操作与属性变更 ---
        // (这部分方法也不需要改变)
        // ... OnMerge, OnDelete, OnSplit 等方法 ...
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
            foreach (var item in selectedViewModels.AsEnumerable().Reverse()) { TextBlockList.Remove(item); }
            TextBlockList.Insert(firstIndex, mergedItem);
            RenumberItems();
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
                }
            }

        private async void OnSplit(object selectedItem)
            {
            if (!(selectedItem is TextBlockViewModel selectedVM)) return;
            var lines = selectedVM.OriginalText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length <= 1)
                {
                await _windowService.ShowInformationDialogAsync("操作无效", "当前行不包含可供拆分的多行文本。");
                return;
                }
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
                _deletableSourceIds.Clear();
                _failedItems.Clear();
                RetranslateFailedCommand.RaiseCanExecuteChanged();
                UpdateProgress(0, 0);
                Log("界面已重置，请重新选择CAD文字。");
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
            if (parameter is string optionToDelete && optionToDelete != "2" && optionToDelete != "5")
                {
                ConcurrencyLevelOptions.Remove(optionToDelete);
                if (CurrentConcurrencyLevelInput == optionToDelete)
                    {
                    CurrentConcurrencyLevelInput = "5";
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

            var numberRegex = new Regex(@"^\s*(\d+[\.,、]?|\(\d+\))");

            blocks.ForEach(b =>
            {
                var match = numberRegex.Match(b.OriginalText);
                b.Character = match.Success ? Regex.Replace(match.Value, @"\D", "") : "无";
                b.BgColor = _characterBrushes[_brushIndex++ % _characterBrushes.Length];
                TextBlockList.Add(b);
            });
            RenumberItems();
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