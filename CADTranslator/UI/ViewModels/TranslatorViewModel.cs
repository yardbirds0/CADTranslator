// 文件路径: CADTranslator/UI/ViewModels/TranslatorViewModel.cs

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
        #region --- Win32 API 辅助 ---
        // 我们不再需要 SetForegroundWindow 这个 P/Invoke 调用了，可以将其删除或注释掉
        // [DllImport("user32.dll")]
        // [return: MarshalAs(UnmanagedType.Bool)]
        // private static extern bool SetForegroundWindow(IntPtr hWnd);

        private void SwitchToAutoCad()
            {
            try
                {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                    {
                    // 使用AutoCAD API内置的方法来激活当前文档窗口，这是最可靠的方式
                    doc.Window.Focus();
                    }
                }
            catch (Exception ex)
                {
                // 记录一个详细的错误日志，以防万一
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
                // 只在值真正改变时才执行
                if (SetField(ref _currentProfile, value))
                    {
                    // 这个方法现在只负责更新UI，不再调用SaveSettings
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
                    GetBalanceCommand.RaiseCanExecuteChanged(); // 强制刷新命令状态
                    ViewHistoryCommand.RaiseCanExecuteChanged();
                    var targetProfile = ApiProfiles.FirstOrDefault(p => p.ServiceType == _selectedApiService);
                    if (targetProfile == null)
                        {
                        targetProfile = new ApiProfile
                            {
                            ProfileName = $"{_selectedApiService} Profile", // 给一个统一的默认名字
                            ServiceType = _selectedApiService
                            };
                        // 【关键】为新创建的Profile提供一些有用的默认值
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

                        // 【核心】将新创建的Profile添加到集合中
                        ApiProfiles.Add(targetProfile);
                        }

                    // 无论找到还是新建，都将它设为当前Profile
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
                    // 【关键】每次切换或创建后，立即保存所有设置
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
                    // 当用户输入一个有效的新数字时，自动添加到下拉列表中
                    if (!string.IsNullOrWhiteSpace(value) &&
                        double.TryParse(value, out _) &&
                        !LineSpacingOptions.Contains(value))
                        {
                        LineSpacingOptions.Add(value);
                        }
                    // 每次输入改变都保存设置，确保用户的选择和新选项被记住
                    if (!_isLoading) SaveSettings();
                    }
                }
            }

        public ObservableCollection<string> LineSpacingOptions { get; set; }

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
        #endregion

        #region --- 构造函数 ---
        public TranslatorViewModel(Window owner)
            {
            _ownerWindow = owner;
            _settingsService = new SettingsService();
            _advancedTextService = new AdvancedTextService(Application.DocumentManager.MdiActiveDocument);
            _cadLayoutService = new CadLayoutService(Application.DocumentManager.MdiActiveDocument);

            TextBlockList = new ObservableCollection<TextBlockViewModel>();
            ModelList = new ObservableCollection<string>();
            ApiProfiles = new ObservableCollection<ApiProfile>();
            LineSpacingOptions = new ObservableCollection<string>();
            BalanceHistory = new ObservableCollection<BalanceRecord>();
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

            LoadSettings();
            Log("欢迎使用CAD翻译工具箱。");
            }
        #endregion

        #region --- 核心方法 ---

        private async void OnTranslate(object parameter)
            {
            if (CurrentProfile == null)
                {
                MessageBox.Show("请先选择一个API配置。");
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
            if (parameter is PasswordBox passwordBox)
                {
                CurrentProfile.ApiKey = passwordBox.Password;
                }

            IsBusy = true;
            try
                {
                var totalStopwatch = new System.Diagnostics.Stopwatch();
                totalStopwatch.Start();
                Log("任务开始", clearPrevious: true);

                ITranslator translator = GetTranslator();
                if (translator == null)
                    {
                    IsBusy = false;
                    return;
                    }

                var itemsToTranslate = TextBlockList.Where(item => string.IsNullOrWhiteSpace(item.TranslatedText) && !string.IsNullOrWhiteSpace(item.OriginalText)).ToList();
                int totalItems = itemsToTranslate.Count;
                int completedItems = 0;
                Log($"准备翻译 {totalItems} 个项目...");
                UpdateProgress(completedItems, totalItems);

                var prefixRegex = new Regex(@"^\s*(\d+[\.,、]\s*)");
                // 定义用于检查译文是否以数字开头的正则表达式
                var startsWithNumberRegex = new Regex(@"^\s*\d+");

                foreach (var item in itemsToTranslate)
                    {
                    var stopwatch = new System.Diagnostics.Stopwatch();
                    string initialLog = $"[{DateTime.Now:HH:mm:ss}] -> 第 {completedItems + 1}/{totalItems} 项翻译正在进行...";
                    Log(initialLog, addNewLine: true, isListItem: true);

                    // 1. 【预处理】提取原文的编号前缀
                    string originalPrefix = "";
                    var match = prefixRegex.Match(item.OriginalText);
                    if (match.Success)
                        {
                        originalPrefix = match.Groups[1].Value;
                        }

                    Task<string> translationTask = CreateTranslationTask(item, translator);
                    stopwatch.Start();

                    while (!translationTask.IsCompleted)
                        {
                        await Task.Delay(500);
                        if (!translationTask.IsCompleted)
                            {
                            UpdateLastLog($"{initialLog} 已进行 {stopwatch.Elapsed.Seconds} 秒");
                            }
                        }
                    stopwatch.Stop();

                    try
                        {
                        string result = await translationTask;
                        if (IsTranslationError(result)) throw new Exception(result);

                        // 2. 【后处理】检查并修复丢失的编号
                        if (!string.IsNullOrEmpty(originalPrefix) && !startsWithNumberRegex.IsMatch(result))
                            {
                            // 如果原文有编号，但译文没有，则自动加上
                            item.TranslatedText = originalPrefix + result;
                            }
                        else
                            {
                            // 否则，直接使用翻译结果
                            item.TranslatedText = result;
                            }

                        completedItems++;
                        UpdateProgress(completedItems, totalItems);
                        UpdateLastLog($"[{DateTime.Now:HH:mm:ss}] -> 第 {completedItems}/{totalItems} 项翻译完成。总共用时 {stopwatch.Elapsed.TotalSeconds:F1} 秒");

                        _ownerWindow.Dispatcher.Invoke(() => ApplyToCadCommand.RaiseCanExecuteChanged());
                        }
                    catch (Exception ex)
                        {
                        HandleTranslationError(ex, item, stopwatch, completedItems);
                        return;
                        }
                    }
                totalStopwatch.Stop();
                Log(totalItems > 0 ? $"全部翻译任务已成功完成！总共用时 {totalStopwatch.Elapsed.TotalSeconds:F1} 秒" : "没有需要翻译的新内容。");
                }
            finally
                {
                IsBusy = false;
                }
            }

        private void OnSelectText(object parameter)
            {
            if (!(parameter is Window mainWindow)) return;
            try
                {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) { MessageBox.Show("未找到活动的CAD文档。"); return; }

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
                        MessageBox.Show("您选择的对象中未找到任何有效文字。");
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
                MessageBox.Show($"提取文字时出错: {ex.Message}");
                }
            finally
                {
                if (!mainWindow.IsVisible)
                    {
                    mainWindow.Show();
                    }
                // 【最终修正】使用 _ownerWindow.Dispatcher
                _ownerWindow.Dispatcher.Invoke(() => TranslateCommand.RaiseCanExecuteChanged());
                }
            }

        private async void OnApplyToCad(object parameter)
            {
            Log("正在切换到CAD窗口并应用翻译...");

            // 1. 【核心】将WPF窗口最小化到任务栏
            _ownerWindow.WindowState = WindowState.Minimized;

            // 2. 将操作焦点明确交给AutoCAD
            SwitchToAutoCad();

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
                // 3. 如果操作失败，则自动恢复窗口以提示用户
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

        private void OnManageModels(object parameter)
            {
            if (CurrentProfile == null) { MessageBox.Show("请先选择一个API配置。"); return; }
            var modelManagementVM = new ModelManagementViewModel(CurrentProfile.ProfileName, CurrentProfile.Models);
            var modelWindow = new ModelManagementWindow(modelManagementVM) { Owner = _ownerWindow };
            if (modelWindow.ShowDialog() == true)
                {
                var finalModels = modelManagementVM.GetFinalModels();
                CurrentProfile.Models.Clear();
                finalModels.ForEach(m => CurrentProfile.Models.Add(m));
                ModelList.Clear();
                finalModels.ForEach(m => ModelList.Add(m));
                if (!string.IsNullOrWhiteSpace(CurrentModelInput) && !finalModels.Contains(CurrentModelInput))
                    {
                    CurrentModelInput = finalModels.FirstOrDefault();
                    OnPropertyChanged(nameof(CurrentModelInput));
                    }
                SaveSettings();
                Log($"配置 '{CurrentProfile.ProfileName}' 的模型列表已成功保存！");
                }
            }

        private async void OnGetModels(object parameter)
            {
            if (CurrentProfile == null || string.IsNullOrWhiteSpace(CurrentProfile.ApiKey))
                {
                Log("[错误] 请先在API设置中填写有效的API密钥。");
                MessageBox.Show("API Key不能为空，请先填写。", "操作失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
                }

            IsBusy = true;
            Log($"正在从 {SelectedApiService} 获取模型列表...");
            try
                {
                var modelService = new ModelFetchingService();
                List<string> models;

                // 【核心修改】根据不同的API，调用不同的方法
                if (SelectedApiService == ApiServiceType.SiliconFlow)
                    {
                    models = await modelService.GetSiliconFlowModelsAsync(CurrentProfile.ApiKey);
                    }
                else // 默认为 Gemini
                    {
                    models = await modelService.GetGeminiModelsAsync(CurrentProfile.ApiKey);
                    }

                if (models != null && models.Any())
                    {
                    // 更新UI和配置文件的模型列表
                    ModelList.Clear();
                    CurrentProfile.Models.Clear();
                    models.ForEach(m =>
                    {
                        ModelList.Add(m);
                        CurrentProfile.Models.Add(m);
                    });

                    // 自动选择第一个模型作为当前模型
                    CurrentModelInput = ModelList.FirstOrDefault();
                    OnPropertyChanged(nameof(CurrentModelInput));
                    SaveSettings(); // 保存更新后的列表

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
                MessageBox.Show($"获取模型列表时发生错误:\n\n{ex.Message}", "操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
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
                // 1. 从UI集合中删除
                if (LineSpacingOptions.Contains(optionToDelete))
                    {
                    LineSpacingOptions.Remove(optionToDelete);
                    }

                // 2. 如果删除的是当前正在显示的值，则重置为默认值
                if (CurrentLineSpacingInput == optionToDelete)
                    {
                    CurrentLineSpacingInput = "不指定";
                    // 这里我们不需要手动调用OnPropertyChanged，因为CurrentLineSpacingInput的setter会自动处理
                    }

                // 3. 保存更改到本地文件
                SaveSettings();

                Log($"已删除行间距选项: {optionToDelete}");
                }
            }
        private async void OnGetBalance(object parameter)
            {
            if (CurrentProfile == null || string.IsNullOrWhiteSpace(CurrentProfile.ApiKey))
                {
                Log("[错误] 请先在API设置中填写有效的API密钥。");
                MessageBox.Show("API Key不能为空，请先填写。", "操作失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
                }

            IsBusy = true;
            Log($"正在从 {SelectedApiService} 查询余额...");
            try
                {
                var balanceService = new BalanceService();
                BalanceRecord newRecord = null;

                // 根据当前选择的API，调用不同的服务方法
                switch (SelectedApiService)
                    {
                    case ApiServiceType.SiliconFlow:
                        newRecord = await balanceService.GetSiliconFlowBalanceAsync(CurrentProfile.ApiKey);
                        break;
                    // case ApiServiceType.Baidu:
                    //     // newRecord = await balanceService.GetBaiduBalanceAsync(...);
                    //     break;
                    default:
                        Log($"[警告] 当前API服务 {SelectedApiService} 尚不支持余额查询。");
                        break;
                    }

                if (newRecord != null)
                    {
                    LastBalanceDisplay = newRecord.BalanceInfo; // 更新UI显示
                    BalanceHistory.Add(newRecord);             // 添加到历史记录
                    SaveSettings();                            // 实时保存
                    Log("余额查询成功！");
                    }
                }
            catch (Exception ex)
                {
                Log($"[错误] 查询余额时失败: {ex.Message}");
                MessageBox.Show($"查询余额时发生错误:\n\n{ex.Message}", "操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            finally
                {
                IsBusy = false;
                }
            }

        private void OnViewHistory(object parameter)
            {
            // 创建历史记录窗口的ViewModel，并将当前的历史记录传递给它
            var historyViewModel = new BalanceHistoryViewModel(this.BalanceHistory);

            // 创建窗口实例，并将ViewModel注入
            var historyWindow = new BalanceHistoryWindow(historyViewModel)
                {
                // 设置窗口的所有者，使其可以模态地显示在主窗口之上
                Owner = _ownerWindow
                };

            // 显示窗口
            historyWindow.ShowDialog();
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
                        MessageBox.Show("当前选择的API服务尚未实现。");
                        return null;
                    }
                }
            catch (ArgumentNullException ex)
                {
                string friendlyMessage = $"配置错误：{ex.ParamName} 不能为空，请在API设置中补充完整。";
                Log(friendlyMessage);
                MessageBox.Show(friendlyMessage, "配置不完整", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
                }
            }

        private Task<string> CreateTranslationTask(TextBlockViewModel item, ITranslator translator)
            {
            // 这是为了处理“占位符注入”方案
            if (item.OriginalText.Contains(AdvancedTextService.LegendPlaceholder))
                {
                // 对于已经含有占位符的文本，直接翻译
                string textToTranslate = string.IsNullOrWhiteSpace(GlobalPrompt) ? item.OriginalText : $"{GlobalPrompt}\n\n{item.OriginalText}";
                return translator.TranslateAsync(textToTranslate, SourceLanguage, TargetLanguage);
                }

            // 正常翻译（这个分支理论上在占位符方案中不会被走到，但保留以防万一）
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
            ProgressText = $"({completed}/{total}) {ProgressValue}%";
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
            // 1. 从本地文件读取最新的设置
            _currentSettings = _settingsService.LoadSettings();

            // 安全检查
            if (_currentSettings.ApiProfiles == null)
                {
                _currentSettings.ApiProfiles = new List<ApiProfile>();
                }

            // 2. 【核心修正】调整执行顺序

            // 2A. **第一步：先处理列表！** 我们使用最安全的“同步”逻辑来填充UI列表
            // 这样做可以确保在任何“保存”操作被触发前，UI列表已经是满的、最新的状态。
            var profilesToRemove = ApiProfiles
                .Where(uiProfile => !_currentSettings.ApiProfiles.Any(lp => lp.ServiceType == uiProfile.ServiceType))
                .ToList();
            foreach (var profileToRemove in profilesToRemove)
                {
                ApiProfiles.Remove(profileToRemove);
                }

            foreach (var loadedProfile in _currentSettings.ApiProfiles)
                {
                var existingProfileInUI = ApiProfiles.FirstOrDefault(p => p.ServiceType == loadedProfile.ServiceType);
                if (existingProfileInUI != null)
                    {
                    existingProfileInUI.ProfileName = loadedProfile.ProfileName;
                    existingProfileInUI.UserId = loadedProfile.UserId;
                    existingProfileInUI.ApiKey = loadedProfile.ApiKey;
                    existingProfileInUI.ApiEndpoint = loadedProfile.ApiEndpoint;
                    existingProfileInUI.LastSelectedModel = loadedProfile.LastSelectedModel; // 直接更新
                    existingProfileInUI.Models.Clear();
                    if (loadedProfile.Models != null)
                        {
                        foreach (var model in loadedProfile.Models)
                            {
                            existingProfileInUI.Models.Add(model);
                            }
                        }
                    }
                else
                    {
                    ApiProfiles.Add(new ApiProfile(loadedProfile));
                    }
                }

            // 2B. **第二步：在所有数据准备好之后，才恢复上次选择的API**
            // 这一步会触发一系列的属性设置，但因为数据都已正确，所以是安全的
            var lastServiceType = _currentSettings.LastSelectedApiService;
            var lastSelectedProfile = ApiProfiles.FirstOrDefault(p => p.ServiceType == lastServiceType);
            // 直接设置私有字段，避免触发不必要的SaveSettings
            _selectedApiService = lastSelectedProfile?.ServiceType ?? ApiProfiles.FirstOrDefault()?.ServiceType ?? ApiServiceType.Baidu;
            OnPropertyChanged(nameof(SelectedApiService)); // 手动通知UI更新

            // 手动更新CurrentProfile，并加载它的UI状态
            CurrentProfile = ApiProfiles.FirstOrDefault(p => p.ServiceType == _selectedApiService);


            // 2C. **第三步：最后才处理简单的布尔值**
            // 即使这行代码触发了SaveSettings()，因为所有数据都已正确加载，
            // 它也只会把正确的数据保存回去，完全无害。
            IsLiveLayoutEnabled = _currentSettings.IsLiveLayoutEnabled;
            if (_currentSettings.LineSpacingPresets == null || !_currentSettings.LineSpacingPresets.Any())
                {
                _currentSettings.LineSpacingPresets = new List<string> { "不指定", "200" };
                }

            // 加载完整的选项列表
            var distinctPresets = _currentSettings.LineSpacingPresets.Distinct().ToList();

            // 用干净的列表来填充UI
            LineSpacingOptions.Clear();
            foreach (var preset in distinctPresets)
                {
                LineSpacingOptions.Add(preset);
                }
            // -- 修改到这里结束 --

            // 加载用户上一次的选择
            CurrentLineSpacingInput = _currentSettings.LastSelectedLineSpacing ?? "不指定";
            if (_currentSettings.BalanceHistory != null)
                {
                BalanceHistory.Clear();
                foreach (var record in _currentSettings.BalanceHistory.OrderByDescending(r => r.Timestamp))
                    {
                    BalanceHistory.Add(record);
                    }
                }

            // 更新上一次的余额显示
            var lastRecord = BalanceHistory.FirstOrDefault();
            if (lastRecord != null && lastRecord.ServiceType == SelectedApiService)
                {
                LastBalanceDisplay = lastRecord.BalanceInfo;
                }
            else
                {
                LastBalanceDisplay = "当前无余额记录";
                }
            // ▲▲▲ 添加结束 ▲▲▲

            _isLoading = false;
            }

        private void SaveSettings()
            {
            if (_currentSettings == null) _currentSettings = new AppSettings();
            _currentSettings.IsLiveLayoutEnabled = this.IsLiveLayoutEnabled;
            _currentSettings.ApiProfiles = this.ApiProfiles.ToList();

            _currentSettings.LastSelectedApiService = this.SelectedApiService; // 写入“记忆”
            _currentSettings.LastSelectedLineSpacing = this.CurrentLineSpacingInput;
            _currentSettings.LineSpacingPresets = this.LineSpacingOptions.ToList();
            _currentSettings.BalanceHistory = this.BalanceHistory.ToList();

            _settingsService.SaveSettings(_currentSettings);
            }

        private void UpdateUiFromCurrentProfile()
            {
            if (CurrentProfile == null) return;

            // 重新绑定事件，防止内存泄漏
            CurrentProfile.PropertyChanged -= OnCurrentProfilePropertyChanged;
            CurrentProfile.PropertyChanged += OnCurrentProfilePropertyChanged;

            // 更新模型列表和输入框
            ModelList.Clear();
            if (CurrentProfile.Models != null)
                {
                foreach (var model in CurrentProfile.Models) { ModelList.Add(model); }
                }
            CurrentModelInput = CurrentProfile.LastSelectedModel; // 这会正确加载保存的模型名称

            // 通知UI其他依赖此配置的控件进行更新
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
        private void OnDelete(object selectedItems)
            {
            var itemsToDelete = (selectedItems as IList<object>)?.Cast<TextBlockViewModel>().ToList();
            if (itemsToDelete == null || itemsToDelete.Count == 0) return;
            if (MessageBox.Show($"确定要删除选中的 {itemsToDelete.Count} 行吗？", "确认删除", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                foreach (var item in itemsToDelete) { TextBlockList.Remove(item); }
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