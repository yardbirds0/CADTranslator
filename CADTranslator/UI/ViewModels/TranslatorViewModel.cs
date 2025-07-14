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
using System.Text;
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

        #region --- 字段 ---
        private readonly SettingsService _settingsService;
        private readonly CadTextService _cadTextService;
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
            [ApiServiceType.Custom] = new ApiServiceConfig(requiresUserId: false, requiresApiKey: true, requiresModelList: true, requiresApiUrl: true)
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
            set { if (SetField(ref _currentProfile, value)) { UpdateUiFromCurrentProfile(); } }
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
                    var targetProfile = ApiProfiles.FirstOrDefault(p => p.ServiceType == _selectedApiService);
                    if (targetProfile == null)
                    {
                        targetProfile = new ApiProfile
                        {
                            ProfileName = $"{_selectedApiService} (默认)",
                            ServiceType = _selectedApiService
                        };
                        if (_selectedApiService == ApiServiceType.Baidu)
                        {
                            targetProfile.UserId = "20250708002400901";
                            targetProfile.ApiKey = "1L_Bso6ORO8torYgecjh";
                        }
                        ApiProfiles.Add(targetProfile);
                    }
                    CurrentProfile = targetProfile;
                }
            }
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

        // --- 状态栏与进度条绑定属性 (核心修改) ---
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
        #endregion

        #region --- 命令 ---
        public ICommand SelectTextCommand { get; }
        public ICommand TranslateCommand { get; }
        public ICommand ApplyToCadCommand { get; }
        public ICommand MergeCommand { get; }
        public ICommand SplitCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand ManageModelsCommand { get; }
        #endregion

        #region --- 构造函数 ---
        public TranslatorViewModel(Window owner)
        {
            _ownerWindow = owner;
            _settingsService = new SettingsService();
            _cadTextService = new CadTextService(Application.DocumentManager.MdiActiveDocument);
            _cadLayoutService = new CadLayoutService(Application.DocumentManager.MdiActiveDocument);

            TextBlockList = new ObservableCollection<TextBlockViewModel>();
            ModelList = new ObservableCollection<string>();
            ApiProfiles = new ObservableCollection<ApiProfile>();

            SelectTextCommand = new RelayCommand(OnSelectText);
            TranslateCommand = new RelayCommand(OnTranslate, p => TextBlockList.Any() && !IsBusy);
            ApplyToCadCommand = new RelayCommand(OnApplyToCad, p => TextBlockList.Any(i => !string.IsNullOrWhiteSpace(i.TranslatedText)));
            MergeCommand = new RelayCommand(OnMerge, p => p is IList<object> list && list.Count > 1);
            DeleteCommand = new RelayCommand(OnDelete, p => p is IList<object> list && list.Count > 0);
            SplitCommand = new RelayCommand(OnSplit, p => p is TextBlockViewModel);
            EditCommand = new RelayCommand(OnEdit, p => p is TextBlockViewModel);
            ManageModelsCommand = new RelayCommand(OnManageModels, p => IsModelListEnabled);

            LoadSettings();
            Log("欢迎使用CAD翻译工具箱。");
        }
        #endregion

        #region --- 核心方法 ---

        private async void OnTranslate(object parameter)
            {
            // --- API配置检查 ---
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
            try // ▼▼▼ 核心修改：所有主要逻辑都包裹在一个大的 try 块中 ▼▼▼
                {
                var totalStopwatch = new System.Diagnostics.Stopwatch();
                totalStopwatch.Start();

                Log("任务开始", clearPrevious: true);

                ITranslator translator;
                try
                    {
                    // (创建 translator 实例的代码保持不变)
                    switch (SelectedApiService)
                        {
                        case ApiServiceType.Baidu:
                            translator = new BaiduTranslator(CurrentProfile.UserId, CurrentProfile.ApiKey);
                            break;
                        case ApiServiceType.Gemini:
                            translator = new GeminiTranslator(CurrentProfile.ApiKey, CurrentProfile.LastSelectedModel);
                            break;
                        case ApiServiceType.OpenAI:
                            translator = new OpenAiTranslator(CurrentProfile.ApiKey, string.IsNullOrWhiteSpace(CurrentProfile.LastSelectedModel) ? "gpt-4o" : CurrentProfile.LastSelectedModel);
                            break;
                        case ApiServiceType.Custom:
                            translator = new CustomTranslator(CurrentProfile.ApiEndpoint, CurrentProfile.ApiKey, CurrentProfile.LastSelectedModel);
                            break;
                        default:
                            MessageBox.Show("当前选择的API服务尚未实现。");
                            return; // 直接返回，finally 块会自动执行
                        }
                    }
                catch (ArgumentNullException ex)
                    {
                    string friendlyMessage = $"配置错误：{ex.ParamName} 不能为空，请在API设置中补充完整。";
                    Log(friendlyMessage);
                    MessageBox.Show(friendlyMessage, "配置不完整", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return; // 直接返回，finally 块会自动执行
                    }

                var itemsToTranslate = TextBlockList.Where(item => string.IsNullOrWhiteSpace(item.TranslatedText) && !string.IsNullOrWhiteSpace(item.OriginalText)).ToList();
                int totalItems = itemsToTranslate.Count;
                int completedItems = 0;

                Log($"准备翻译 {totalItems} 个项目...");
                ProgressValue = 0;
                ProgressText = $"(0/{totalItems}) 0%";

                foreach (var item in itemsToTranslate)
                    {
                    var stopwatch = new System.Diagnostics.Stopwatch();
                    string textToTranslate = string.IsNullOrWhiteSpace(GlobalPrompt) ? item.OriginalText : $"{GlobalPrompt}\n\n{item.OriginalText}";
                    string initialLog = $"[{DateTime.Now:HH:mm:ss}] -> 第 {completedItems + 1}/{totalItems} 项翻译正在进行...";
                    Log(initialLog, addNewLine: true, isListItem: true);
                    var translationTask = translator.TranslateAsync(textToTranslate, SourceLanguage, TargetLanguage);
                    stopwatch.Start();

                    // ▼▼▼ 核心修改：在等待翻译完成的同时，启动一个实时计时器 ▼▼▼
                    while (!translationTask.IsCompleted)
                        {
                        // 每隔 500 毫秒（半秒）更新一次计时，让刷新看起来更平滑
                        await Task.Delay(500);

                        // 只有当任务仍在运行时才更新日志
                        if (!translationTask.IsCompleted)
                            {
                            UpdateLastLog($"{initialLog} 已进行 {stopwatch.Elapsed.Seconds} 秒");
                            }
                        }
                    // ▲▲▲ 修改结束 ▲▲▲

                    stopwatch.Stop();

                    try
                        {
                        // 获取已经完成的翻译任务的结果
                        string result = await translationTask;

                        if (result.StartsWith("翻译失败：") || result.StartsWith("调用") || result.StartsWith("请求失败:") || result.StartsWith("百度API返回错误:"))
                            {
                            throw new Exception(result);
                            }

                        item.TranslatedText = result;
                        completedItems++;
                        ProgressValue = (int)((double)completedItems / totalItems * 100);
                        ProgressText = $"({completedItems}/{totalItems}) {ProgressValue}%";
                        UpdateLastLog($"[{DateTime.Now:HH:mm:ss}] -> 第 {completedItems}/{totalItems} 项翻译完成。总共用时 {stopwatch.Elapsed.TotalSeconds:F1} 秒");
                        }
                    catch (Exception ex)
                        {
                        // --- 统一的失败处理中心 ---
                        if (stopwatch.IsRunning) stopwatch.Stop();

                        string errorMessage = ex.Message.Replace('\t', ' ');
                        UpdateLastLog($"[{DateTime.Now:HH:mm:ss}] [翻译失败] 第 {completedItems + 1} 项，原因: {errorMessage}");
                        Log("任务因错误而中断。");
                        item.TranslatedText = errorMessage; // 将错误信息填入译文栏

                        return; // 中断任务，提前退出方法。finally块依然会执行。
                        }
                    } // foreach 循环结束

                totalStopwatch.Stop();
                if (totalItems > 0)
                    {
                    Log($"全部翻译任务已成功完成！总共用时 {totalStopwatch.Elapsed.TotalSeconds:F1} 秒");
                    }
                else
                    {
                    Log("没有需要翻译的新内容。");
                    }
                }
            finally
                {
                // ▼▼▼ 无论发生什么，这里都将被执行，确保UI被解锁 ▼▼▼
                IsBusy = false;
                }
            }


        private void OnSelectText(object parameter)
        {
            if (!(parameter is Window mainWindow)) return;
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null)
                {
                    MessageBox.Show("未找到活动的CAD文档。");
                    return;
                }
                mainWindow.Hide();
                var ed = doc.Editor;
                var selRes = ed.GetSelection();
                mainWindow.Show();
                mainWindow.Activate();
                if (selRes.Status != PromptStatus.OK) return;
                List<TextBlockViewModel> textBlocks = _cadTextService.ExtractAndMergeText(selRes.Value);
                if (textBlocks.Count == 0)
                {
                    Log("在选定对象中未找到任何有效文字。");
                    MessageBox.Show("您选择的对象中未找到任何有效文字。");
                    return;
                }
                LoadTextBlocks(textBlocks);
                Log($"成功提取并合并了 {textBlocks.Count} 个文本块。");
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
                    mainWindow.Activate();
                }
            }
        }

        private async void OnApplyToCad(object parameter)
        {
            Log("正在切换到CAD窗口并应用翻译...");
            SwitchToAutoCad();
            bool success = await Task.Run(() => _cadLayoutService.ApplyTranslationToCad(TextBlockList));
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

        #endregion

        #region --- 日志与设置管理 ---

        private void Log(string message, bool clearPrevious = false, bool addNewLine = true, bool isListItem = false)
        {
            if (clearPrevious)
            {
                StatusLog.Clear();
            }

            var formattedMessage = isListItem ? message : $"[{DateTime.Now:HH:mm:ss}] {message}";

            CadBridgeService.WriteToCommandLine(formattedMessage);

            if (addNewLine)
            {
                _ownerWindow.Dispatcher.Invoke(() => StatusLog.Add(formattedMessage));
            }
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
            var profiles = _settingsService.LoadApiProfiles();
            ApiProfiles.Clear();
            foreach (var profile in profiles) { ApiProfiles.Add(profile); }
            var firstProfile = ApiProfiles.FirstOrDefault();
            SelectedApiService = firstProfile?.ServiceType ?? ApiServiceType.Baidu;
        }

        private void SaveSettings(object parameter = null)
        {
            if (CurrentProfile != null)
            {
                CurrentProfile.ServiceType = this.SelectedApiService;
                var profilesList = ApiProfiles.ToList();
                if (profilesList.Contains(CurrentProfile))
                {
                    profilesList.Remove(CurrentProfile);
                    profilesList.Insert(0, CurrentProfile);
                }
                _settingsService.SaveApiProfiles(profilesList);
            }
            else
            {
                _settingsService.SaveApiProfiles(ApiProfiles.ToList());
            }
        }

        private void UpdateUiFromCurrentProfile()
        {
            if (CurrentProfile == null) return;
            CurrentProfile.PropertyChanged -= OnCurrentProfilePropertyChanged;
            CurrentProfile.PropertyChanged += OnCurrentProfilePropertyChanged;

            SelectedApiService = CurrentProfile.ServiceType;
            ModelList.Clear();
            if (CurrentProfile.Models != null) { foreach (var model in CurrentProfile.Models) { ModelList.Add(model); } }
            CurrentModelInput = CurrentProfile.LastSelectedModel;

            OnPropertyChanged(nameof(CurrentProfile));
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