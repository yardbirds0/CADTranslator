// 文件路径: CADTranslator/UI/ViewModels/TranslatorViewModel.cs
// 【注意】这是一个完整的文件替换

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;

namespace CADTranslator.UI.ViewModels
    {
    public class TranslatorViewModel : INotifyPropertyChanged
        {
        #region --- Win32 API 辅助 ---
        private void SwitchToAutoCad()
            {
            try
                {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                    {
                    doc.Window.Focus();
                    }
                }
            catch (Exception ex)
                {
                Log($"[警告] 切换到CAD窗口时出错: {ex.Message}");
                }
            }
        #endregion

        #region --- 字段 ---
        private bool _isLoading = false;
        private List<ObjectId> _deletableSourceIds = new List<ObjectId>();
        private readonly SettingsService _settingsService;
        private AppSettings _currentSettings;
        private readonly AdvancedTextService _advancedTextService;
        private readonly CadLayoutService _cadLayoutService;
        private readonly Window _ownerWindow;
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
            [ApiServiceType.Baidu] = new ApiServiceConfig(requiresUserId: true, requiresApiKey: true, requiresModelList: false, requiresApiUrl: false),
            [ApiServiceType.Gemini] = new ApiServiceConfig(requiresUserId: false, requiresApiKey: true, requiresModelList: true, requiresApiUrl: false),
            [ApiServiceType.OpenAI] = new ApiServiceConfig(requiresUserId: false, requiresApiKey: true, requiresModelList: true, requiresApiUrl: false),
            [ApiServiceType.Custom] = new ApiServiceConfig(requiresUserId: false, requiresApiKey: true, requiresModelList: true, requiresApiUrl: true),
            [ApiServiceType.SiliconFlow] = new ApiServiceConfig(requiresUserId: false, requiresApiKey: true, requiresModelList: true, requiresApiUrl: true)
            };
        // ▼▼▼ 新增字段 ▼▼▼
        private List<TextBlockViewModel> _failedItems = new List<TextBlockViewModel>();
        #endregion

        #region --- 绑定属性 ---
        private bool _isBusy;
        public bool IsBusy
            {
            get => _isBusy;
            set { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsUiEnabled)); }
            }
        public bool IsUiEnabled => !IsBusy;
        public ObservableCollection<TextBlockViewModel> TextBlockList { get; set; }
        public ObservableCollection<ApiProfile> ApiProfiles { get; set; }

        private ApiProfile _currentProfile;
        public ApiProfile CurrentProfile
            {
            get => _currentProfile;
            set
                {
                if (SetField(ref _currentProfile, value))
                    {
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
                if (SetField(ref _selectedApiService, value))
                    {
                    var config = _apiServiceConfigs[_selectedApiService];
                    OnPropertyChanged(nameof(IsUserIdEnabled));
                    OnPropertyChanged(nameof(IsApiKeyEnabled));
                    OnPropertyChanged(nameof(IsModelListEnabled));
                    OnPropertyChanged(nameof(IsApiUrlEnabled));
                    OnPropertyChanged(nameof(IsBalanceFeatureEnabled));
                    GetBalanceCommand.RaiseCanExecuteChanged();
                    ViewHistoryCommand.RaiseCanExecuteChanged();
                    var targetProfile = ApiProfiles.FirstOrDefault(p => p.ServiceType == _selectedApiService);
                    if (targetProfile == null)
                        {
                        targetProfile = new ApiProfile
                            {
                            ProfileName = $"{_selectedApiService} Profile",
                            ServiceType = _selectedApiService
                            };
                        if (_selectedApiService == ApiServiceType.Baidu)
                            {
                            targetProfile.UserId = "20250708002400901";
                            targetProfile.ApiKey = "1L_Bso6ORO8torYgecjh";
                            }
                        else if (_selectedApiService == ApiServiceType.OpenAI)
                            {
                            targetProfile.Models = new List<string> { "gpt-4o", "gpt-3.5-turbo" };
                            targetProfile.LastSelectedModel = "gpt-4o";
                            }
                        else if (_selectedApiService == ApiServiceType.Gemini)
                            {
                            targetProfile.Models = new List<string> { "gemini-1.5-pro-latest", "gemini-1.0-pro" };
                            targetProfile.LastSelectedModel = "gemini-1.5-pro-latest";
                            }
                        ApiProfiles.Add(targetProfile);
                        }
                    CurrentProfile = targetProfile;
                    var lastRecordForNewApi = BalanceHistory.FirstOrDefault(r => r.ServiceType == _selectedApiService);
                    if (lastRecordForNewApi != null)
                        {
                        LastBalanceDisplay = lastRecordForNewApi.BalanceInfo;
                        }
                    else
                        {
                        LastBalanceDisplay = "当前无余额记录";
                        }
                    if (!_isLoading) SaveSettings();
                    }
                }
            }

        private bool _isLiveLayoutEnabled;
        public bool IsLiveLayoutEnabled
            {
            get => _isLiveLayoutEnabled;
            set { if (SetField(ref _isLiveLayoutEnabled, value)) { if (!_isLoading) SaveSettings(); } }
            }

        public ObservableCollection<string> ModelList { get; set; }
        public string CurrentModelInput { get; set; }
        public List<ApiServiceDisplay> ApiServiceOptions { get; }
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

        public ObservableCollection<string> StatusLog { get; } = new ObservableCollection<string>();
        private int _progressValue;
        public int ProgressValue { get => _progressValue; set => SetField(ref _progressValue, value); }
        private string _progressText;
        public string ProgressText { get => _progressText; set => SetField(ref _progressText, value); }

        private ApiServiceConfig CurrentServiceConfig => _apiServiceConfigs[SelectedApiService];
        public bool IsUserIdEnabled => CurrentServiceConfig.RequiresUserId;
        public bool IsApiKeyEnabled => CurrentServiceConfig.RequiresApiKey;
        public bool IsModelListEnabled => CurrentServiceConfig.RequiresModelList;
        public bool IsApiUrlEnabled => CurrentServiceConfig.RequiresApiUrl;
        private string _lastBalanceDisplay = "当前无余额记录";
        public string LastBalanceDisplay
            {
            get => _lastBalanceDisplay;
            set => SetField(ref _lastBalanceDisplay, value);
            }

        public bool IsBalanceFeatureEnabled => SelectedApiService == ApiServiceType.SiliconFlow;
        private string _currentLineSpacingInput;
        public ObservableCollection<BalanceRecord> BalanceHistory { get; set; }
        public string CurrentLineSpacingInput
            {
            get => _currentLineSpacingInput;
            set
                {
                if (SetField(ref _currentLineSpacingInput, value))
                    {
                    if (!string.IsNullOrWhiteSpace(value) &&
                        double.TryParse(value, out _) &&
                        !LineSpacingOptions.Contains(value))
                        {
                        LineSpacingOptions.Add(value);
                        }
                    if (!_isLoading) SaveSettings();
                    }
                }
            }
        public ObservableCollection<string> LineSpacingOptions { get; set; }

        // ▼▼▼ 新增绑定属性 ▼▼▼
        private bool _isMultiThreadingEnabled;
        public bool IsMultiThreadingEnabled
            {
            get => _isMultiThreadingEnabled;
            set { if (SetField(ref _isMultiThreadingEnabled, value)) { if (!_isLoading) SaveSettings(); } }
            }

        public ObservableCollection<string> ConcurrencyLevelOptions { get; set; }

        private string _currentConcurrencyLevelInput;
        public string CurrentConcurrencyLevelInput
            {
            get => _currentConcurrencyLevelInput;
            set
                {
                if (SetField(ref _currentConcurrencyLevelInput, value))
                    {
                    if (!string.IsNullOrWhiteSpace(value) &&
                        int.TryParse(value, out int intVal) && intVal > 1 &&
                        !ConcurrencyLevelOptions.Contains(value))
                        {
                        ConcurrencyLevelOptions.Add(value);
                        }
                    if (!_isLoading) SaveSettings();
                    }
                }
            }
        #endregion

        #region --- 命令 ---
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

        // ▼▼▼ 新增命令 ▼▼▼
        public RelayCommand RetranslateFailedCommand { get; }
        public RelayCommand DeleteConcurrencyOptionCommand { get; }
        #endregion

        #region --- 构造函数 ---
        public TranslatorViewModel(Window owner)
            {
            _ownerWindow = owner;
            _settingsService = new SettingsService();
            _advancedTextService = new AdvancedTextService(Application.DocumentManager.MdiActiveDocument);
            _cadLayoutService = new CadLayoutService(Application.DocumentManager.MdiActiveDocument);

            ApiServiceOptions = new List<ApiServiceDisplay>
            {
                new ApiServiceDisplay { DisplayName = "百度翻译", ServiceType = ApiServiceType.Baidu },
                new ApiServiceDisplay { DisplayName = "谷歌Gemini", ServiceType = ApiServiceType.Gemini },
                new ApiServiceDisplay { DisplayName = "ChatGPT", ServiceType = ApiServiceType.OpenAI },
                new ApiServiceDisplay { DisplayName = "硅基流动", ServiceType = ApiServiceType.SiliconFlow },
                new ApiServiceDisplay { DisplayName = "自定义接口", ServiceType = ApiServiceType.Custom }
            };

            TextBlockList = new ObservableCollection<TextBlockViewModel>();
            ModelList = new ObservableCollection<string>();
            ApiProfiles = new ObservableCollection<ApiProfile>();
            LineSpacingOptions = new ObservableCollection<string>();
            BalanceHistory = new ObservableCollection<BalanceRecord>();
            // ▼▼▼ 新增初始化 ▼▼▼
            ConcurrencyLevelOptions = new ObservableCollection<string>();

            SelectTextCommand = new RelayCommand(OnSelectText);
            TranslateCommand = new RelayCommand(OnTranslate, p => TextBlockList.Any() && !IsBusy);
            ApplyToCadCommand = new RelayCommand(OnApplyToCad, p => TextBlockList.Any(i => !string.IsNullOrWhiteSpace(i.TranslatedText) && !i.TranslatedText.StartsWith("[")));
            MergeCommand = new RelayCommand(OnMerge, p => p is IList<object> list && list.Count > 1);
            DeleteCommand = new RelayCommand(OnDelete, p => p is IList<object> list && list.Count > 0);
            SplitCommand = new RelayCommand(OnSplit, p => p is TextBlockViewModel);
            EditCommand = new RelayCommand(OnEdit, p => p is TextBlockViewModel);
            ManageModelsCommand = new RelayCommand(OnManageModels, p => IsModelListEnabled);
            GetModelsCommand = new RelayCommand(OnGetModels, p => (SelectedApiService == ApiServiceType.SiliconFlow || SelectedApiService == ApiServiceType.Gemini) && !IsBusy);
            DeleteLineSpacingOptionCommand = new RelayCommand(OnDeleteLineSpacingOption, p => p is string option && option != "不指定");
            GetBalanceCommand = new RelayCommand(OnGetBalance, p => IsBalanceFeatureEnabled && !IsBusy);
            ViewHistoryCommand = new RelayCommand(OnViewHistory, p => IsBalanceFeatureEnabled);
            ResetCommand = new RelayCommand(OnReset);
            // ▼▼▼ 新增命令实现 ▼▼▼
            RetranslateFailedCommand = new RelayCommand(OnRetranslateFailed, p => _failedItems.Any() && !IsBusy);
            DeleteConcurrencyOptionCommand = new RelayCommand(OnDeleteConcurrencyOption, p => p is string option && option != "2" && option != "5");

            LoadSettings();
            Log("欢迎使用CAD翻译工具箱。");
            }
        #endregion

        #region --- 核心方法 ---

        private async void OnTranslate(object parameter)
            {
            await ExecuteTranslation(TextBlockList.Where(item => string.IsNullOrWhiteSpace(item.TranslatedText) && !string.IsNullOrWhiteSpace(item.OriginalText)).ToList());
            }

        // ▼▼▼ 新增的重翻译方法 ▼▼▼
        private async void OnRetranslateFailed(object parameter)
            {
            if (!_failedItems.Any()) return;
            Log("开始重新翻译失败的项目...");
            var itemsToRetry = new List<TextBlockViewModel>(_failedItems);
            _failedItems.Clear(); // 清空失败列表，准备下一次统计
            RetranslateFailedCommand.RaiseCanExecuteChanged(); // 更新按钮状态

            // 将失败项的译文清空，以便重新翻译
            foreach (var item in itemsToRetry)
                {
                item.TranslatedText = "";
                }

            await ExecuteTranslation(itemsToRetry);
            }

        // ▼▼▼ 【核心改造】将翻译逻辑提取到一个可重用的方法中 ▼▼▼
        private async Task ExecuteTranslation(List<TextBlockViewModel> itemsToTranslate)
            {
            if (CurrentProfile == null)
                {
                var mb = new MessageBox { Title = "操作无效", Content = "请先选择一个API配置。", CloseButtonText = "确定" };
                mb.Resources = _ownerWindow.Resources;
                await mb.ShowDialogAsync();
                return;
                }
            if (IsModelListEnabled && !string.IsNullOrWhiteSpace(CurrentModelInput))
                {
                CurrentProfile.LastSelectedModel = CurrentModelInput;
                if (!ModelList.Contains(CurrentModelInput))
                    {
                    ModelList.Add(CurrentModelInput);
                    CurrentProfile.Models.Add(CurrentModelInput);
                    }
                }

            IsBusy = true;
            try
                {
                var totalStopwatch = new System.Diagnostics.Stopwatch();
                totalStopwatch.Start();
                Log("翻译任务开始", clearPrevious: true);

                ITranslator translator = GetTranslator();
                if (translator == null)
                    {
                    IsBusy = false;
                    return;
                    }

                int totalItems = itemsToTranslate.Count;
                if (totalItems == 0)
                    {
                    Log("没有需要翻译的新内容。");
                    return;
                    }

                int completedItems = 0;
                UpdateProgress(completedItems, totalItems);

                if (IsMultiThreadingEnabled)
                    {
                    // --- 并发翻译逻辑 ---
                    int concurrencyLevel = 2; // 默认值
                    if (int.TryParse(CurrentConcurrencyLevelInput, out int userLevel) && userLevel > 1)
                        {
                        concurrencyLevel = userLevel;
                        }
                    Log($"启动并发翻译，总数: {totalItems}，最大并发量: {concurrencyLevel}");
                    var semaphore = new SemaphoreSlim(concurrencyLevel);

                    var translationTasks = itemsToTranslate.Select(async item =>
                    {
                        await semaphore.WaitAsync();
                        try
                            {
                            var stopwatch = new System.Diagnostics.Stopwatch();
                            stopwatch.Start();
                            string result = await CreateTranslationTask(item, translator);
                            stopwatch.Stop();

                            if (IsTranslationError(result)) throw new Exception(result);
                            item.TranslatedText = result;
                            }
                        catch (Exception ex)
                            {
                            var errorMessage = ex.Message.Replace('\t', ' ');
                            item.TranslatedText = $"[翻译失败] {errorMessage}";
                            lock (_failedItems) { _failedItems.Add(item); }
                            _ownerWindow.Dispatcher.Invoke(() => RetranslateFailedCommand.RaiseCanExecuteChanged());
                            }
                        finally
                            {
                            semaphore.Release();
                            int currentCompleted = Interlocked.Increment(ref completedItems);
                            _ownerWindow.Dispatcher.Invoke(() =>
                            {
                                UpdateProgress(currentCompleted, totalItems);
                                ApplyToCadCommand.RaiseCanExecuteChanged();
                            });
                            }
                    });

                    await Task.WhenAll(translationTasks);
                    }
                else
                    {
                    // --- 串行翻译逻辑 (原逻辑) ---
                    Log($"启动单线程翻译，总数: {totalItems}");
                    var prefixRegex = new Regex(@"^\s*(\d+[\.,、]\s*)");
                    var startsWithNumberRegex = new Regex(@"^\s*\d+");

                    foreach (var item in itemsToTranslate)
                        {
                        var stopwatch = new System.Diagnostics.Stopwatch();
                        string initialLog = $"[{DateTime.Now:HH:mm:ss}] -> 第 {completedItems + 1}/{totalItems} 项翻译中...";
                        Log(initialLog, addNewLine: true, isListItem: true);

                        string originalPrefix = "";
                        var match = prefixRegex.Match(item.OriginalText);
                        if (match.Success) originalPrefix = match.Groups[1].Value;

                        Task<string> translationTask = CreateTranslationTask(item, translator);
                        stopwatch.Start();

                        while (!translationTask.IsCompleted)
                            {
                            await Task.Delay(500);
                            if (!translationTask.IsCompleted) UpdateLastLog($"{initialLog} 已进行 {stopwatch.Elapsed.Seconds} 秒");
                            }
                        stopwatch.Stop();

                        try
                            {
                            string result = await translationTask;
                            if (IsTranslationError(result)) throw new Exception(result);

                            if (!string.IsNullOrEmpty(originalPrefix) && !startsWithNumberRegex.IsMatch(result))
                                {
                                item.TranslatedText = originalPrefix + result;
                                }
                            else
                                {
                                item.TranslatedText = result;
                                }

                            completedItems++;
                            UpdateProgress(completedItems, totalItems);
                            UpdateLastLog($"[{DateTime.Now:HH:mm:ss}] -> 第 {completedItems}/{totalItems} 项翻译完成。用时 {stopwatch.Elapsed.TotalSeconds:F1} 秒");
                            _ownerWindow.Dispatcher.Invoke(() => ApplyToCadCommand.RaiseCanExecuteChanged());
                            }
                        catch (Exception ex)
                            {
                            HandleTranslationError(ex, item, stopwatch, completedItems);
                            lock (_failedItems) { _failedItems.Add(item); }
                            RetranslateFailedCommand.RaiseCanExecuteChanged();
                            return; // 单线程模式下，遇到错误就中断
                            }
                        }
                    }

                totalStopwatch.Stop();
                if (_failedItems.Any())
                    {
                    Log($"任务完成，有 {_failedItems.Count} 个项目翻译失败。总用时 {totalStopwatch.Elapsed.TotalSeconds:F1} 秒");
                    }
                else
                    {
                    Log($"全部翻译任务成功完成！总用时 {totalStopwatch.Elapsed.TotalSeconds:F1} 秒");
                    }
                }
            finally
                {
                IsBusy = false;
                }
            }

        private async void OnSelectText(object parameter)
            {
            if (!(parameter is Window mainWindow)) return;
            try
                {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null)
                    {
                    var mb1 = new MessageBox { Title = "操作失败", Content = "未找到活动的CAD文档。", CloseButtonText = "确定" };
                    mb1.Resources = _ownerWindow.Resources;
                    await mb1.ShowDialogAsync();
                    return;
                    }

                mainWindow.Hide();
                var ed = doc.Editor;
                var selRes = ed.GetSelection();

                using (doc.LockDocument())
                    {
                    if (selRes.Status != PromptStatus.OK)
                        {
                        mainWindow.Show();
                        return;
                        }

                    List<ParagraphInfo> paragraphInfos = _advancedTextService.ExtractAndProcessParagraphs(selRes.Value, out _deletableSourceIds);

                    if (paragraphInfos.Count == 0)
                        {
                        Log("在选定对象中未找到任何有效文字。");
                        var mb2 = new MessageBox { Title = "提示", Content = "您选择的对象中未找到任何有效文字。", CloseButtonText = "确定" };
                        mb2.Resources = _ownerWindow.Resources;
                        await mb2.ShowDialogAsync();
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
                var mb3 = new MessageBox { Title = "提取失败", Content = $"提取文字时出错: {ex.Message}", CloseButtonText = "确定" };
                mb3.Resources = _ownerWindow.Resources;
                await mb3.ShowDialogAsync();
                }
            finally
                {
                if (!mainWindow.IsVisible)
                    {
                    mainWindow.Show();
                    }
                _ownerWindow.Dispatcher.Invoke(() => TranslateCommand.RaiseCanExecuteChanged());
                }
            }

        private async void OnApplyToCad(object parameter)
            {
            Log("正在切换到CAD窗口并应用翻译...");
            _ownerWindow.WindowState = WindowState.Minimized;
            SwitchToAutoCad();
            // 增加一个100毫秒的微小延迟，以确保AutoCAD窗口完全获得焦点，避免竞争条件
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
                    _ownerWindow.WindowState = WindowState.Normal;
                    _ownerWindow.Activate();
                    }
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

        private async void OnManageModels(object parameter)
            {
            if (CurrentProfile == null)
                {
                var mb = new MessageBox { Title = "操作无效", Content = "请先选择一个API配置。", CloseButtonText = "确定" };
                mb.Resources = _ownerWindow.Resources;
                await mb.ShowDialogAsync();
                return;
                }

            var modelManagementVM = new ModelManagementViewModel(CurrentProfile.ProfileName, new List<string>(CurrentProfile.Models));
            var modelWindow = new ModelManagementWindow(modelManagementVM) { Owner = _ownerWindow };
            if (modelWindow.ShowDialog() == true)
                {
                //... (这部分逻辑不变)
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
                    Log($"已应用新模型: {selectedModelName}");
                    }
                else
                    {
                    if (!finalModels.Any() || (CurrentProfile.LastSelectedModel != null && !finalModels.Contains(CurrentProfile.LastSelectedModel)))
                        {
                        CurrentProfile.LastSelectedModel = finalModels.FirstOrDefault();
                        CurrentModelInput = CurrentProfile.LastSelectedModel;
                        }
                    Log($"配置 '{CurrentProfile.ProfileName}' 的模型列表已更新！");
                    }
                OnPropertyChanged(nameof(CurrentModelInput));
                SaveSettings();
                }
            }

        private async void OnGetModels(object parameter)
            {
            if (CurrentProfile == null || string.IsNullOrWhiteSpace(CurrentProfile.ApiKey))
                {
                var mb1 = new MessageBox { Title = "操作失败", Content = "API Key不能为空，请先填写。", CloseButtonText = "确定" };
                mb1.Resources = _ownerWindow.Resources;
                await mb1.ShowDialogAsync();
                return;
                }

            IsBusy = true;
            Log($"正在从 {SelectedApiService} 获取模型列表...");
            try
                {
                var modelService = new ModelFetchingService();
                List<string> models;
                if (SelectedApiService == ApiServiceType.SiliconFlow)
                    {
                    models = await modelService.GetSiliconFlowModelsAsync(CurrentProfile.ApiKey);
                    }
                else
                    {
                    models = await modelService.GetGeminiModelsAsync(CurrentProfile.ApiKey);
                    }

                if (models != null && models.Any())
                    {
                    ModelList.Clear();
                    CurrentProfile.Models.Clear();
                    models.ForEach(m =>
                    {
                        ModelList.Add(m);
                        CurrentProfile.Models.Add(m);
                    });
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
            catch (Exception ex)
                {
                Log($"[错误] 获取模型列表时失败: {ex.Message}");
                var mb2 = new MessageBox { Title = "操作失败", Content = $"获取模型列表时发生错误:\n\n{ex.Message}", CloseButtonText = "确定" };
                mb2.Resources = _ownerWindow.Resources;
                await mb2.ShowDialogAsync();
                }
            finally
                {
                IsBusy = false;
                }
            }

        private void OnDeleteLineSpacingOption(object parameter)
            {
            if (parameter is string optionToDelete)
                {
                if (LineSpacingOptions.Contains(optionToDelete))
                    {
                    LineSpacingOptions.Remove(optionToDelete);
                    }
                if (CurrentLineSpacingInput == optionToDelete)
                    {
                    CurrentLineSpacingInput = "不指定";
                    }
                SaveSettings();
                Log($"已删除行间距选项: {optionToDelete}");
                }
            }

        // ▼▼▼ 新增的删除并发量选项的方法 ▼▼▼
        private async void OnDeleteConcurrencyOption(object parameter)
            {
            if (parameter is string optionToDelete)
                {
                if (optionToDelete == "2" || optionToDelete == "5")
                    {
                    var mb = new MessageBox { Title = "提示", Content = "默认并发量选项 '2' 和 '5' 无法删除。", CloseButtonText = "确定" };
                    mb.Resources = _ownerWindow.Resources;
                    await mb.ShowDialogAsync();
                    return;
                    }

                if (ConcurrencyLevelOptions.Contains(optionToDelete))
                    {
                    ConcurrencyLevelOptions.Remove(optionToDelete);
                    }
                if (CurrentConcurrencyLevelInput == optionToDelete)
                    {
                    CurrentConcurrencyLevelInput = "5";
                    }
                SaveSettings();
                Log($"已删除并发量选项: {optionToDelete}");
                }
            }

        private async void OnGetBalance(object parameter)
            {
            if (CurrentProfile == null || string.IsNullOrWhiteSpace(CurrentProfile.ApiKey))
                {
                var mb1 = new MessageBox { Title = "操作失败", Content = "API Key不能为空，请先填写。", CloseButtonText = "确定" };
                mb1.Resources = _ownerWindow.Resources;
                await mb1.ShowDialogAsync();
                return;
                }

            IsBusy = true;
            Log($"正在从 {SelectedApiService} 查询余额...");
            try
                {
                var balanceService = new BalanceService();
                BalanceRecord newRecord = null;
                switch (SelectedApiService)
                    {
                    case ApiServiceType.SiliconFlow:
                        newRecord = await balanceService.GetSiliconFlowBalanceAsync(CurrentProfile.ApiKey);
                        break;
                    default:
                        Log($"[警告] 当前API服务 {SelectedApiService} 尚不支持余额查询。");
                        break;
                    }

                if (newRecord != null)
                    {
                    LastBalanceDisplay = newRecord.BalanceInfo;
                    BalanceHistory.Add(newRecord);
                    SaveSettings();
                    Log("余额查询成功！");
                    }
                }
            catch (Exception ex)
                {
                Log($"[错误] 查询余额时失败: {ex.Message}");
                var mb2 = new MessageBox { Title = "操作失败", Content = $"查询余额时发生错误:\n\n{ex.Message}", CloseButtonText = "确定" };
                mb2.Resources = _ownerWindow.Resources;
                await mb2.ShowDialogAsync();
                }
            finally
                {
                IsBusy = false;
                }
            }

        private void OnViewHistory(object parameter)
            {
            var historyViewModel = new BalanceHistoryViewModel(this.BalanceHistory);
            var historyWindow = new BalanceHistoryWindow(historyViewModel) { Owner = _ownerWindow };
            historyWindow.ShowDialog();
            }

        private async void OnReset(object parameter)
            {
            var messageBox = new Wpf.Ui.Controls.MessageBox
                {
                Title = "确认重置",      
                Content = "您确定要清空所有已提取的文本吗？此操作不可恢复。",
                PrimaryButtonText = "确认清空",
                CloseButtonText = "取消"
                };
            messageBox.Resources = _ownerWindow.Resources;
            messageBox.Owner = _ownerWindow;
            var result = await messageBox.ShowDialogAsync();

            if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
                {
                TextBlockList.Clear();
                _deletableSourceIds.Clear();
                _failedItems.Clear(); // 同时清空失败列表
                RetranslateFailedCommand.RaiseCanExecuteChanged();
                UpdateProgress(0, 0); // 重置进度条
                Log("界面已重置，请重新选择CAD文字。");
                }
            }
        #endregion

        #region --- 辅助方法 (新增) ---

        private ITranslator GetTranslator()
            {
            try
                {
                switch (SelectedApiService)
                    {
                    case ApiServiceType.Baidu:
                        return new BaiduTranslator(CurrentProfile.UserId, CurrentProfile.ApiKey);
                    case ApiServiceType.Gemini:
                        return new GeminiTranslator(CurrentProfile.ApiKey, CurrentProfile.LastSelectedModel);
                    case ApiServiceType.OpenAI:
                        return new OpenAiTranslator(CurrentProfile.ApiKey, string.IsNullOrWhiteSpace(CurrentProfile.LastSelectedModel) ? "gpt-4o" : CurrentProfile.LastSelectedModel);
                    case ApiServiceType.Custom:
                        return new CustomTranslator(CurrentProfile.ApiEndpoint, CurrentProfile.ApiKey, CurrentProfile.LastSelectedModel);
                    case ApiServiceType.SiliconFlow:
                        return new SiliconFlowTranslator(CurrentProfile.ApiEndpoint, CurrentProfile.ApiKey, CurrentProfile.LastSelectedModel);
                    default:
                        var mb1 = new MessageBox { Title = "提示", Content = "当前选择的API服务尚未实现。", CloseButtonText = "确定" };
                        mb1.Resources = _ownerWindow.Resources;
                        // 注意：这里是同步等待，因为它在一个非async方法中
                        mb1.ShowDialog();
                        return null;
                    }
                }
            catch (ArgumentNullException ex)
                {
                string friendlyMessage = $"配置错误：{ex.ParamName} 不能为空，请在API设置中补充完整。";
                Log(friendlyMessage);
                var mb2 = new MessageBox { Title = "配置不完整", Content = friendlyMessage, CloseButtonText = "确定" };
                mb2.Resources = _ownerWindow.Resources;
                // 注意：这里是同步等待
                mb2.ShowDialog();
                return null;
                }
            }

        private Task<string> CreateTranslationTask(TextBlockViewModel item, ITranslator translator)
            {
            if (item.OriginalText.Contains(AdvancedTextService.LegendPlaceholder))
                {
                string textToTranslate = string.IsNullOrWhiteSpace(GlobalPrompt) ? item.OriginalText : $"{GlobalPrompt}\n\n{item.OriginalText}";
                return translator.TranslateAsync(textToTranslate, SourceLanguage, TargetLanguage);
                }
            string normalTextToTranslate = string.IsNullOrWhiteSpace(GlobalPrompt) ? item.OriginalText : $"{GlobalPrompt}\n\n{item.OriginalText}";
            return translator.TranslateAsync(normalTextToTranslate, SourceLanguage, TargetLanguage);
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

        private bool IsTranslationError(string result)
            {
            return result.StartsWith("翻译失败：") || result.StartsWith("调用") || result.StartsWith("请求失败:") || result.StartsWith("百度API返回错误:");
            }

        private void HandleTranslationError(Exception ex, TextBlockViewModel item, System.Diagnostics.Stopwatch stopwatch, int completedItems)
            {
            if (stopwatch.IsRunning) stopwatch.Stop();
            string errorMessage = ex.Message.Replace('\t', ' ');
            UpdateLastLog($"[{DateTime.Now:HH:mm:ss}] [翻译失败] 第 {completedItems + 1} 项，原因: {errorMessage}");
            Log("任务因错误而中断。");
            item.TranslatedText = $"[翻译失败] {errorMessage}";
            }
        #endregion

        #region --- 日志与设置管理 ---

        private void Log(string message, bool clearPrevious = false, bool addNewLine = true, bool isListItem = false)
            {
            if (clearPrevious) StatusLog.Clear();
            var formattedMessage = isListItem ? message : $"[{DateTime.Now:HH:mm:ss}] {message}";
            CadBridgeService.WriteToCommandLine(formattedMessage);
            if (addNewLine) _ownerWindow.Dispatcher.Invoke(() => StatusLog.Add(formattedMessage));
            }

        private void UpdateLastLog(string message)
            {
            CadBridgeService.WriteToCommandLine(message);
            if (StatusLog.Any())
                {
                _ownerWindow.Dispatcher.Invoke(() => StatusLog[StatusLog.Count - 1] = message);
                }
            }

        private void LoadSettings()
            {
            _isLoading = true;
            _currentSettings = _settingsService.LoadSettings();

            if (_currentSettings.ApiProfiles == null) _currentSettings.ApiProfiles = new List<ApiProfile>();

            var profilesToRemove = ApiProfiles.Where(uiProfile => !_currentSettings.ApiProfiles.Any(lp => lp.ServiceType == uiProfile.ServiceType)).ToList();
            foreach (var profileToRemove in profilesToRemove) ApiProfiles.Remove(profileToRemove);

            foreach (var loadedProfile in _currentSettings.ApiProfiles)
                {
                var existingProfileInUI = ApiProfiles.FirstOrDefault(p => p.ServiceType == loadedProfile.ServiceType);
                if (existingProfileInUI != null)
                    {
                    existingProfileInUI.ProfileName = loadedProfile.ProfileName;
                    existingProfileInUI.UserId = loadedProfile.UserId;
                    existingProfileInUI.ApiKey = loadedProfile.ApiKey;
                    existingProfileInUI.ApiEndpoint = loadedProfile.ApiEndpoint;
                    existingProfileInUI.LastSelectedModel = loadedProfile.LastSelectedModel;
                    existingProfileInUI.Models.Clear();
                    if (loadedProfile.Models != null)
                        {
                        foreach (var model in loadedProfile.Models) existingProfileInUI.Models.Add(model);
                        }
                    }
                else
                    {
                    ApiProfiles.Add(new ApiProfile(loadedProfile));
                    }
                }

            var lastServiceType = _currentSettings.LastSelectedApiService;
            var lastSelectedProfile = ApiProfiles.FirstOrDefault(p => p.ServiceType == lastServiceType);
            _selectedApiService = lastSelectedProfile?.ServiceType ?? ApiProfiles.FirstOrDefault()?.ServiceType ?? ApiServiceType.Baidu;
            OnPropertyChanged(nameof(SelectedApiService));
            CurrentProfile = ApiProfiles.FirstOrDefault(p => p.ServiceType == _selectedApiService);
            IsLiveLayoutEnabled = _currentSettings.IsLiveLayoutEnabled;

            if (_currentSettings.LineSpacingPresets == null || !_currentSettings.LineSpacingPresets.Any())
                {
                _currentSettings.LineSpacingPresets = new List<string> { "不指定", "200" };
                }
            var distinctPresets = _currentSettings.LineSpacingPresets.Distinct().ToList();
            LineSpacingOptions.Clear();
            foreach (var preset in distinctPresets) LineSpacingOptions.Add(preset);
            CurrentLineSpacingInput = _currentSettings.LastSelectedLineSpacing ?? "不指定";

            if (_currentSettings.BalanceHistory != null)
                {
                BalanceHistory.Clear();
                foreach (var record in _currentSettings.BalanceHistory.OrderByDescending(r => r.Timestamp))
                    {
                    BalanceHistory.Add(record);
                    }
                }

            var lastRecord = BalanceHistory.FirstOrDefault();
            if (lastRecord != null && lastRecord.ServiceType == SelectedApiService)
                {
                LastBalanceDisplay = lastRecord.BalanceInfo;
                }
            else
                {
                LastBalanceDisplay = "当前无余额记录";
                }

            // ▼▼▼ 加载新的多线程设置 ▼▼▼
            IsMultiThreadingEnabled = _currentSettings.IsMultiThreadingEnabled;
            if (_currentSettings.ConcurrencyPresets == null || !_currentSettings.ConcurrencyPresets.Any())
                {
                _currentSettings.ConcurrencyPresets = new List<string> { "2", "5" };
                }
            var distinctConcurrency = _currentSettings.ConcurrencyPresets.Distinct().ToList();
            if (!distinctConcurrency.Contains("2")) distinctConcurrency.Insert(0, "2");
            if (!distinctConcurrency.Contains("5")) distinctConcurrency.Insert(1, "5");
            ConcurrencyLevelOptions.Clear();
            foreach (var preset in distinctConcurrency) ConcurrencyLevelOptions.Add(preset);
            CurrentConcurrencyLevelInput = _currentSettings.LastSelectedConcurrency ?? "5";
            // ▲▲▲ 加载结束 ▲▲▲

            _isLoading = false;
            }

        private void SaveSettings()
            {
            if (_isLoading) return; // 避免在加载时就触发保存
            if (_currentSettings == null) _currentSettings = new AppSettings();

            _currentSettings.IsLiveLayoutEnabled = this.IsLiveLayoutEnabled;
            _currentSettings.ApiProfiles = this.ApiProfiles.ToList();
            _currentSettings.LastSelectedApiService = this.SelectedApiService;
            _currentSettings.LastSelectedLineSpacing = this.CurrentLineSpacingInput;
            _currentSettings.LineSpacingPresets = this.LineSpacingOptions.ToList();
            _currentSettings.BalanceHistory = this.BalanceHistory.ToList();

            // ▼▼▼ 保存新的多线程设置 ▼▼▼
            _currentSettings.IsMultiThreadingEnabled = this.IsMultiThreadingEnabled;
            _currentSettings.LastSelectedConcurrency = this.CurrentConcurrencyLevelInput;
            _currentSettings.ConcurrencyPresets = this.ConcurrencyLevelOptions.ToList();
            // ▲▲▲ 保存结束 ▲▲▲

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
                foreach (var model in CurrentProfile.Models) { ModelList.Add(model); }
                }
            CurrentModelInput = CurrentProfile.LastSelectedModel;
            OnPropertyChanged(nameof(CurrentModelInput));
            OnPropertyChanged(nameof(IsUserIdEnabled));
            OnPropertyChanged(nameof(IsApiKeyEnabled));
            OnPropertyChanged(nameof(IsModelListEnabled));
            OnPropertyChanged(nameof(IsApiUrlEnabled));
            }

        private void OnCurrentProfilePropertyChanged(object sender, PropertyChangedEventArgs e)
            {
            SaveSettings();
            }

        public void LoadTextBlocks(List<TextBlockViewModel> blocks)
            {
            TextBlockList.Clear();
            _brushIndex = 0;

            // 1. 正则表达式，用于匹配开头的编号部分，例如 "1." 或 "(1)"
            var numberRegex = new Regex(@"^\s*(\d+[\.,、]?|\(\d+\))");

            blocks.ForEach(b =>
            {
                var match = numberRegex.Match(b.OriginalText);
                if (match.Success)
                    {
                    // 2. 获取匹配到的完整编号字符串，例如 " (1) " 或 "11."
                    string fullMatch = match.Value;

                    // 3. 从匹配结果中，只提取出数字部分
                    string digitsOnly = Regex.Replace(fullMatch, @"\D", "");

                    b.Character = digitsOnly;
                    }
                else
                    {
                    b.Character = "无";
                    }

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

        #region --- 表格操作与属性变更 ---
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

            var messageBox = new MessageBox
                {
                Title = "确认删除",
                Content = $"确定要删除选中的 {itemsToDelete.Count} 行吗？",
                PrimaryButtonText = "确认删除",
                CloseButtonText = "取消"
                };
            messageBox.Resources = _ownerWindow.Resources;

            var result = await messageBox.ShowDialogAsync();

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
                var mb = new MessageBox { Title = "操作无效", Content = "当前行不包含可供拆分的多行文本。", CloseButtonText = "确定" };
                mb.Resources = _ownerWindow.Resources;
                await mb.ShowDialogAsync();
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
                var mb = new MessageBox { Title = "操作无效", Content = "不能直接编辑从CAD提取的文本。", CloseButtonText = "确定" };
                mb.Resources = _ownerWindow.Resources;
                await mb.ShowDialogAsync();
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