// 文件路径: CADTranslator/UI/ViewModels/TranslatorViewModel.cs
// 【注意】这是一个完整的文件替换

using CADTranslator.Models;
using CADTranslator.Services;
using CADTranslator.UI.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;
using MessageBox = Wpf.Ui.Controls.MessageBox;
using MessageBoxResult = Wpf.Ui.Controls.MessageBoxResult;
using ObjectId = Autodesk.AutoCAD.DatabaseServices.ObjectId;

namespace CADTranslator.UI.ViewModels
    {
    public class TranslatorViewModel : INotifyPropertyChanged
        {
        #region --- 字段与服务 ---

        private readonly Window _ownerWindow;
        private readonly SettingsService _settingsService;
        private readonly AdvancedTextService _advancedTextService;
        private readonly CadLayoutService _cadLayoutService;
        private readonly ApiRegistry _apiRegistry; // 【新增】API注册中心

        private AppSettings _currentSettings;
        private bool _isLoading = false;
        private List<ObjectId> _deletableSourceIds = new List<ObjectId>();
        private List<TextBlockViewModel> _failedItems = new List<TextBlockViewModel>();

        // 用于UI绑定的私有字段
        private bool _isBusy;
        private ITranslator _currentProvider; // 【核心替换】用当前服务提供商替代旧的ApiProfile
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

        public bool IsBusy
            {
            get => _isBusy;
            set { SetField(ref _isBusy, value); OnPropertyChanged(nameof(IsUiEnabled)); }
            }

        public bool IsUiEnabled => !IsBusy;

        /// <summary>
        /// 【核心替换】当前选中的API服务提供商
        /// </summary>
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
        public ObservableCollection<string> ConcurrencyLevelOptions { get; set; }

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
        public RelayCommand RetranslateFailedCommand { get; }
        public RelayCommand DeleteConcurrencyOptionCommand { get; }
        public RelayCommand ManageApiDefinitionsCommand { get; }

        #endregion

        #region --- 构造函数 ---

        public TranslatorViewModel(Window owner)
            {
            _ownerWindow = owner;
            // 初始化服务
            _settingsService = new SettingsService();
            _advancedTextService = new AdvancedTextService(Application.DocumentManager.MdiActiveDocument);
            _cadLayoutService = new CadLayoutService(Application.DocumentManager.MdiActiveDocument);
            _apiRegistry = new ApiRegistry(); // 【新增】创建注册中心实例

            // 初始化UI集合
            TextBlockList = new ObservableCollection<TextBlockViewModel>();
            ModelList = new ObservableCollection<string>();
            ApiProfiles = new ObservableCollection<ApiProfile>();
            LineSpacingOptions = new ObservableCollection<string>();
            BalanceHistory = new ObservableCollection<BalanceRecord>();
            ConcurrencyLevelOptions = new ObservableCollection<string>();

            // 【修改】API下拉列表的数据源现在来自注册中心
            ApiServiceOptions = _apiRegistry.Providers;

            // 初始化命令
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

            LoadSettings();
            Log("欢迎使用CAD翻译工具箱。");
            }

        #endregion

        #region --- 核心方法 (翻译、选择、应用) ---

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
                    if (selRes.Status != Autodesk.AutoCAD.EditorInput.PromptStatus.OK)
                        {
                        mainWindow.Show();
                        return;
                        }

                    List<ParagraphInfo> paragraphInfos = _advancedTextService.ExtractAndProcessParagraphs(selRes.Value, out _deletableSourceIds);

                    if (paragraphInfos.Count == 0)
                        {
                        Log("在选定对象中未找到任何有效文字。");
                        ShowMessageBox("提示", "您选择的对象中未找到任何有效文字。");
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
                ShowMessageBox("提取失败", $"提取文字时出错: {ex.Message}");
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
                ShowMessageBox("操作无效", "请先选择一个API配置。");
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
                        await semaphore.WaitAsync();
                        try
                            {
                            string result = await CreateTranslationTask(translator, item);
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
                            if (!translationTask.IsCompleted) UpdateLastLog($"{initialLog} 已进行 {stopwatch.Elapsed.Seconds} 秒");
                            }
                        stopwatch.Stop();

                        try
                            {
                            string result = await translationTask;
                            if (IsTranslationError(result)) throw new Exception(result);
                            item.TranslatedText = result;

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
                            return;
                            }
                        }
                    }
                }
            catch (Exception ex)
                {
                Log($"[错误] 创建翻译服务时失败: {ex.Message}");
                ShowMessageBox("配置错误", $"创建翻译服务时失败，请检查API配置：\n\n{ex.Message}");
                }
            finally
                {
                totalStopwatch.Stop();
                if (_failedItems.Any())
                    {
                    Log($"任务完成，有 {_failedItems.Count} 个项目翻译失败。总用时 {totalStopwatch.Elapsed.TotalSeconds:F1} 秒");
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
                }
            }

        private async void OnApplyToCad(object parameter)
            {
            Log("正在切换到CAD窗口并应用翻译...");
            _ownerWindow.WindowState = WindowState.Minimized;
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
            catch (Exception ex)
                {
                Log($"[错误] 获取模型列表时失败: {ex.Message}");
                ShowMessageBox("操作失败", $"获取模型列表时发生错误:\n\n{ex.Message}");
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

                // 【核心修正】
                // 1. 创建一个新的 BalanceRecord 实例
                var newRecord = new BalanceRecord
                    {
                    Timestamp = DateTime.Now,
                    ServiceType = CurrentProvider.ServiceType,
                    Data = balanceData // 2. 将完整的原始数据存入 Data 属性
                    };

                BalanceHistory.Insert(0, newRecord); // 插入到列表开头，方便查看

                // 3. 更新主界面的余额显示
                UpdateBalanceDisplayForCurrentProvider();

                SaveSettings();
                Log("余额查询成功！");
                }
            catch (Exception ex)
                {
                Log($"[错误] 查询余额时失败: {ex.Message}");
                ShowMessageBox("操作失败", $"查询余额时发生错误:\n\n{ex.Message}");
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
                var mb = new MessageBox { Title = "操作无效", Content = "请先选择一个API配置。", CloseButtonText = "确定" };
                mb.Resources = _ownerWindow.Resources;
                await mb.ShowDialogAsync();
                return;
                }
            var modelManagementVM = new ModelManagementViewModel(CurrentProfile.ProfileName, new List<string>(CurrentProfile.Models));
            var modelWindow = new ModelManagementWindow(modelManagementVM) { Owner = _ownerWindow };
            if (modelWindow.ShowDialog() == true)
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

        private void OnManageApiDefinitions(object parameter)
            {
            // 这里只是一个框架，完整的实现会更复杂，
            // 涉及到列表管理、删除、编辑等。
            // 我们先实现最核心的“新增”功能。

            var vm = new ApiDefinitionViewModel();
            var window = new ApiDefinitionWindow(vm) { Owner = _ownerWindow };

            if (window.ShowDialog() == true)
                {
                // 用户点击了保存
                var newDefinition = vm.ApiDef;

                // 理论上，我们应该有一个 "GenericTranslator" 来使用这个Definition。
                // 并且需要更新 ApiRegistry 和 SettingsService 来处理自定义API。
                // 这部分作为我们重构的下一步，现在我们先弹窗展示成果。
                ShowMessageBox("功能待实现", $"已成功创建API定义: '{newDefinition.DisplayName}'。\n下一步我们将实现让这个配置真正生效的逻辑。");

                // TODO:
                // 1. 将 newDefinition 保存到 _currentSettings.CustomApiDefinitions 中。
                // 2. 调用 SaveSettings()。
                // 3. 更新 ApiRegistry 以包含这个新的自定义API。
                // 4. 刷新主界面的API下拉列表。
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

            // 查找当前API最新的一条历史记录
            var lastRecord = BalanceHistory.FirstOrDefault(r => r.ServiceType == CurrentProvider.ServiceType);

            if (lastRecord == null || lastRecord.Data == null || !lastRecord.Data.Any())
                {
                LastBalanceDisplay = "当前无余额记录";
                return;
                }

            // 【核心修正】
            // 根据我们讨论的“别名系统”，查找代表余额的那个Key-Value对
            // 我们预定义了 "totalBalance", "balance" 等都是 "CanonicalBalance" 的别名
            var balanceMapping = _currentSettings.FriendlyNameMappings.FirstOrDefault(m => m.Key == "CanonicalBalance");

            if (balanceMapping.Value != null)
                {
                // 在记录的Data中，查找第一个匹配别名列表的Key
                var balancePair = lastRecord.Data.FirstOrDefault(d => balanceMapping.Value.Aliases.Contains(d.Key));
                // 如果找到了，就只显示它的值
                LastBalanceDisplay = balancePair.Value ?? "未找到余额信息";
                }
            else
                {
                // 如果连映射规则都没有，就显示一个通用提示
                LastBalanceDisplay = "未配置余额规则";
                }
            }

        private Task<string> CreateTranslationTask(ITranslator translator, TextBlockViewModel item)
            {
            string textToTranslate = item.OriginalText;
            if (translator.IsPromptSupported && !string.IsNullOrWhiteSpace(GlobalPrompt))
                {
                textToTranslate = $"{GlobalPrompt}\n\n{item.OriginalText}";
                }
            return translator.TranslateAsync(textToTranslate, SourceLanguage, TargetLanguage);
            }

        private bool IsTranslationError(string result) => result.StartsWith("[") || result.StartsWith("翻译失败") || result.StartsWith("调用") || result.StartsWith("请求失败") || result.StartsWith("百度API返回错误");

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

        private async void ShowMessageBox(string title, string content)
            {
            var mb = new MessageBox { Title = title, Content = content, CloseButtonText = "确定" };
            mb.Resources = _ownerWindow.Resources;
            await mb.ShowDialogAsync();
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
            editWindow.Owner = _ownerWindow;
            if (editWindow.ShowDialog() == true)
                {
                selectedVM.OriginalText = editWindow.EditedText;
                selectedVM.Character = string.IsNullOrWhiteSpace(selectedVM.OriginalText) ? "?" : selectedVM.OriginalText.Substring(0, 1).ToUpper();
                selectedVM.TranslatedText = "";
                }
            }

        private async void OnReset(object parameter)
            {
            var messageBox = new MessageBox
                {
                Title = "确认重置",
                Content = "您确定要清空所有已提取的文本吗？此操作不可恢复。",
                PrimaryButtonText = "确认清空",
                CloseButtonText = "取消"
                };
            messageBox.Resources = _ownerWindow.Resources;
            messageBox.Owner = _ownerWindow;
            var result = await messageBox.ShowDialogAsync();

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
            // 1. 创建ViewModel实例，把主列表传递进去
            var historyViewModel = new BalanceHistoryViewModel(this.BalanceHistory);

            // 2. 【核心】订阅删除请求事件
            historyViewModel.DeleteRequested += (recordsToDelete) =>
            {
                if (recordsToDelete != null && recordsToDelete.Any())
                    {
                    foreach (var record in recordsToDelete)
                        {
                        // 从主列表中移除
                        BalanceHistory.Remove(record);
                        }
                    // 立即保存更改
                    SaveSettings();
                    }
            };

            // 3. 创建并显示窗口
            var historyWindow = new BalanceHistoryWindow(historyViewModel) { Owner = _ownerWindow };
            historyWindow.ShowDialog();
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