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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;


namespace CADTranslator.UI.ViewModels
    {
    public class TranslatorViewModel : INotifyPropertyChanged
        {
        #region --- 字段 (Fields) ---

        // 服务
        private readonly SettingsService _settingsService;
        private readonly CadTextService _cadTextService;
        private readonly CadLayoutService _cadLayoutService;

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
            set { _currentProfile = value; OnPropertyChanged(); UpdateUiFromCurrentProfile(); }
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

        // --- 语言和Prompt属性 ---
        public IEnumerable<ApiServiceType> ApiServiceOptions => Enum.GetValues(typeof(ApiServiceType)).Cast<ApiServiceType>();

        public List<string> SupportedLanguages { get; } = new List<string> { "auto", "zh", "en", "ja", "ko", "fr", "de", "ru" };
        public string SourceLanguage { get; set; } = "auto";
        public string TargetLanguage { get; set; } = "en";
        public string GlobalPrompt { get; set; }

        // --- 控制UI灰化状态的属性 ---
        public bool IsUserIdRequired { get; private set; }
        public bool IsApiKeyRequired { get; private set; }
        public bool IsModelListVisible { get; private set; }
        public bool IsCustomEndpointVisible { get; private set; }

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
        public ICommand SaveSettingsCommand { get; }

        #endregion

        #region --- 构造函数 (Constructor) ---

        public TranslatorViewModel()
            {
            // 初始化服务
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
            GetModelsCommand = new RelayCommand(OnGetModels, p => IsModelListVisible);
            AddDefaultModelCommand = new RelayCommand(OnAddDefaultModel);
            SaveSettingsCommand = new RelayCommand(SaveSettings);

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
            }

        private void UpdateApiControlStates()
            {
            switch (SelectedApiService)
                {
                case ApiServiceType.Baidu:
                    IsUserIdRequired = true; IsApiKeyRequired = true; IsModelListVisible = false; IsCustomEndpointVisible = false;
                    break;
                case ApiServiceType.Gemini:
                case ApiServiceType.OpenAI:
                    IsUserIdRequired = false; IsApiKeyRequired = true; IsModelListVisible = true; IsCustomEndpointVisible = false;
                    break;
                case ApiServiceType.Custom:
                    IsUserIdRequired = true; IsApiKeyRequired = true; IsModelListVisible = true; IsCustomEndpointVisible = true;
                    break;
                }
            OnPropertyChanged(nameof(IsUserIdRequired));
            OnPropertyChanged(nameof(IsApiKeyRequired));
            OnPropertyChanged(nameof(IsModelListVisible));
            OnPropertyChanged(nameof(IsCustomEndpointVisible));
            }

        #endregion

        #region --- 命令实现 (Command Implementations) ---

        private void OnSelectText(object parameter)
            {
            try
                {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) { MessageBox.Show("未找到活动的CAD文档。"); return; }
                var ed = doc.Editor;
                var selRes = ed.GetSelection();
                if (selRes.Status != PromptStatus.OK) return;

                List<TextBlockViewModel> textBlocks = _cadTextService.ExtractAndMergeText(selRes.Value);
                if (textBlocks.Count == 0) { MessageBox.Show("您选择的对象中未找到任何有效文字。"); return; }

                LoadTextBlocks(textBlocks);
                }
            catch (System.Exception ex) { MessageBox.Show($"提取文字时出错: {ex.Message}"); }
            }

        private async void OnTranslate(object parameter)
            {
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
                    translator = new GeminiTranslator(CurrentProfile.ApiKey);
                    break;
                case ApiServiceType.OpenAI:
                    // 我们将用户在UI上选择的模型(LastSelectedModel)传递给翻译器
                    string selectedModel = string.IsNullOrWhiteSpace(CurrentProfile.LastSelectedModel) ? "gpt-3.5-turbo" : CurrentProfile.LastSelectedModel;
                    translator = new OpenAiTranslator(CurrentProfile.ApiKey, selectedModel);
                    break;
                case ApiServiceType.Custom:
                    translator = new CustomTranslator(CurrentProfile.ApiEndpoint, CurrentProfile.ApiKey);
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

        private void OnApplyToCad(object parameter)
            {
            _cadLayoutService.ApplyTranslationToCad(TextBlockList);
            }

        private void OnGetModels(object parameter)
            {
            MessageBox.Show("此功能待实现：将调用API获取可用模型列表。");
            }

        private void OnAddDefaultModel(object parameter)
            {
            MessageBox.Show("此功能待实现。");
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