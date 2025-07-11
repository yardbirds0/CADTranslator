using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using CADTranslator.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CADTranslator
{
    public partial class TranslatorWindow : Window
    {
        public ObservableCollection<TextBlockItem> TextBlockList { get; set; }

        // 用于为表格首字母圆圈提供循环的颜色
        private readonly Brush[] _characterBrushes = new Brush[]
        {
            (Brush)new BrushConverter().ConvertFromString("#1E88E5"),
            (Brush)new BrushConverter().ConvertFromString("#0CA678"),
            (Brush)new BrushConverter().ConvertFromString("#FF8F00"),
            (Brush)new BrushConverter().ConvertFromString("#FF5252"),
            (Brush)new BrushConverter().ConvertFromString("#6741D9"),
            (Brush)new BrushConverter().ConvertFromString("#1098AD"),
            (Brush)new BrushConverter().ConvertFromString("#FF6D00")
        };
        private int _brushIndex = 0;


        public TranslatorWindow()
        {
            InitializeComponent();
            TextBlockList = new ObservableCollection<TextBlockItem>();
            this.DataContext = this;
            dataGridBlocks.ItemsSource = TextBlockList;

            // 绑定选择变化事件，用于更新按钮状态
            dataGridBlocks.SelectionChanged += DataGridBlocks_SelectionChanged;

            // 初始化按钮状态
            UpdateButtonsState();
        }

        // --- 核心功能按钮 ---

        private void BtnSelectText_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null)
                {
                    MessageBox.Show("未找到活动的CAD文档。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                var ed = doc.Editor;

                // 隐藏窗口以便用户选择
                this.Hide();

                var selRes = ed.GetSelection();

                // 无论选择成功与否，都显示窗口
                this.Show();
                this.Activate();

                if (selRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n用户取消选择。");
                    return;
                }

                List<TextBlockItem> textBlocks = CadCommands.ExtractAndMergeText(doc, selRes.Value);

                if (textBlocks.Count == 0)
                {
                    MessageBox.Show("您选择的对象中未找到任何有效文字。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                LoadTextBlocks(textBlocks);
            }
            catch (Exception ex)
            {
                ShowErrorDialog("在选择和提取CAD文字时发生未知错误", ex);
            }
        }


        private async void BtnTranslate_Click(object sender, RoutedEventArgs e)
        {
            if (TextBlockList.Count == 0) return;

            // 增强用户体验：翻译期间禁用按钮
            this.IsEnabled = false;

            try
            {
                var translator = new BaiduTranslator("", txtApiKey.Password); // ID为空时使用内置默认ID
                foreach (var item in TextBlockList)
                {
                    // 只翻译原文存在但译文为空的行
                    if (string.IsNullOrWhiteSpace(item.OriginalText) || !string.IsNullOrWhiteSpace(item.TranslatedText)) continue;

                    item.TranslatedText = await translator.TranslateAsync(item.OriginalText, "auto", "en"); // 目标语言可修改为"zh"等
                }
            }
            catch (Exception ex)
            {
                ShowErrorDialog($"翻译过程中出错，操作已在第 {TextBlockList.FirstOrDefault(i => string.IsNullOrEmpty(i.TranslatedText))?.Id ?? 0} 行处中断。", ex);
            }
            finally
            {
                // 确保无论成功或失败，窗口最终都会恢复可用
                this.IsEnabled = true;
            }
        }

        private void BtnApplyToCAD_Click(object sender, RoutedEventArgs e)
        {
            if (!TextBlockList.Any(item => !string.IsNullOrWhiteSpace(item.TranslatedText)))
            {
                MessageBox.Show("表格中没有任何有效的译文可以应用到CAD。", "操作取消", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // 增强用户体验：应用期间隐藏窗口
            this.Hide();

            try
            {
                using (doc.LockDocument())
                {
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);

                        foreach (var item in TextBlockList)
                        {
                            // 必须有译文，且必须有关联的源对象ID
                            if (string.IsNullOrWhiteSpace(item.TranslatedText) || item.SourceObjectIds == null || !item.SourceObjectIds.Any()) continue;

                            var firstObjectId = item.SourceObjectIds.First();
                            if (firstObjectId.IsNull || firstObjectId.IsErased) continue;

                            var baseEntity = tr.GetObject(firstObjectId, OpenMode.ForRead) as Entity;
                            if (baseEntity == null) continue;

                            // 先删除所有旧实体
                            foreach (var objectId in item.SourceObjectIds)
                            {
                                if (objectId.IsNull || objectId.IsErased) continue;
                                // 需要以写入模式打开才能删除
                                var entityToErase = tr.GetObject(objectId, OpenMode.ForWrite) as Entity;
                                entityToErase?.Erase();
                            }

                            // 将多行译文合并为单行，用空格隔开（CAD的DBText不支持原生换行）
                            string singleLineText = item.TranslatedText.Replace('\n', ' ').Replace('\r', ' ');

                            using (DBText newText = new DBText())
                            {
                                newText.TextString = singleLineText;
                                // 完美继承属性
                                newText.SetPropertiesFrom(baseEntity);

                                // 关键属性需要单独设置
                                if (baseEntity is DBText originalDbText)
                                {
                                    newText.Position = originalDbText.Position;
                                    newText.Height = originalDbText.Height;
                                    newText.Rotation = originalDbText.Rotation;
                                    newText.Oblique = originalDbText.Oblique;
                                    newText.WidthFactor = originalDbText.WidthFactor;
                                    newText.TextStyleId = originalDbText.TextStyleId;
                                    newText.HorizontalMode = originalDbText.HorizontalMode;
                                    newText.VerticalMode = originalDbText.VerticalMode;
                                    // 如果是对齐文字，需要重新设置对齐点
                                    if (newText.HorizontalMode != TextHorizontalMode.TextLeft || newText.VerticalMode != TextVerticalMode.TextBase)
                                    {
                                        newText.AlignmentPoint = originalDbText.AlignmentPoint;
                                    }
                                }
                                else if (baseEntity is MText originalMText)
                                {
                                    // 从MText继承属性时，需要做一些转换
                                    newText.Position = originalMText.Location;
                                    newText.Height = originalMText.TextHeight;
                                    newText.Rotation = originalMText.Rotation;
                                    newText.TextStyleId = originalMText.TextStyleId;
                                    // DBText不支持MText的所有对齐方式，设置为默认
                                    newText.HorizontalMode = TextHorizontalMode.TextLeft;
                                    newText.VerticalMode = TextVerticalMode.TextBase;
                                }

                                // 确保高度有效
                                if (newText.Height <= 0) newText.Height = 2.5;

                                modelSpace.AppendEntity(newText);
                                tr.AddNewlyCreatedDBObject(newText, true);
                            }
                        }
                        tr.Commit();
                        MessageBox.Show("所有翻译已成功应用到CAD图纸！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                ShowErrorDialog("将翻译应用到CAD时发生严重错误", ex);
            }
            finally
            {
                // 确保无论成功与否，窗口最终都会显示出来
                this.Show();
                this.Activate();
            }
        }


        // --- 数据加载与管理 ---
        public void LoadTextBlocks(List<TextBlockItem> blocks)
        {
            TextBlockList.Clear();
            _brushIndex = 0; // 重置颜色索引
            blocks.ForEach(b => {
                // 为新的UI设置首字母和背景色
                b.Character = string.IsNullOrWhiteSpace(b.OriginalText) ? "?" : b.OriginalText.Substring(0, 1).ToUpper();
                b.BgColor = _characterBrushes[_brushIndex++ % _characterBrushes.Length];
                TextBlockList.Add(b);
            });
            RenumberItems(); // 这会同时更新按钮状态
        }

        private void RenumberItems()
        {
            for (int i = 0; i < TextBlockList.Count; i++)
            {
                TextBlockList[i].Id = i + 1;
            }
            // 在重新编号后，立即更新按钮状态
            UpdateButtonsState();
        }

        // --- 表格手动操作 ---
        private void BtnMerge_Click(object sender, RoutedEventArgs e)
        {
            if (dataGridBlocks.SelectedItems.Count <= 1) return;
            var selectedItems = dataGridBlocks.SelectedItems.Cast<TextBlockItem>().OrderBy(i => TextBlockList.IndexOf(i)).ToList();
            var firstItem = selectedItems.First();

            var mergedItem = new TextBlockItem
            {
                OriginalText = string.Join("\n", selectedItems.Select(i => i.OriginalText)),
                TranslatedText = string.Join("\n", selectedItems.Select(i => i.TranslatedText)),
                SourceObjectIds = selectedItems.SelectMany(i => i.SourceObjectIds).ToList(),
                Character = firstItem.Character, // 继承第一个项目的UI属性
                BgColor = firstItem.BgColor
            };

            int firstIndex = TextBlockList.IndexOf(firstItem);
            // 从后往前删除，避免索引错乱
            foreach (var item in selectedItems.AsEnumerable().Reverse())
            {
                TextBlockList.Remove(item);
            }
            TextBlockList.Insert(firstIndex, mergedItem);
            RenumberItems();
        }

        private void BtnSplit_Click(object sender, RoutedEventArgs e)
        {
            if (!(dataGridBlocks.SelectedItem is TextBlockItem selectedItem)) return;
            var lines = selectedItem.OriginalText.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length <= 1)
            {
                MessageBox.Show("当前行不包含可供拆分的多行文本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // 更新当前行
            selectedItem.OriginalText = lines[0];
            selectedItem.TranslatedText = ""; // 拆分后清空译文

            int insertIndex = TextBlockList.IndexOf(selectedItem) + 1;
            for (int i = 1; i < lines.Length; i++)
            {
                // 新增的行没有源对象ID，因为它们是逻辑拆分出来的
                var newItem = new TextBlockItem
                {
                    OriginalText = lines[i],
                    SourceObjectIds = new List<ObjectId>(),
                    // 为新的UI设置首字母和背景色
                    Character = string.IsNullOrWhiteSpace(lines[i]) ? "?" : lines[i].Substring(0, 1).ToUpper(),
                    BgColor = _characterBrushes[_brushIndex++ % _characterBrushes.Length]
                };
                TextBlockList.Insert(insertIndex++, newItem);
            }
            RenumberItems();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            int insertIndex = (dataGridBlocks.SelectedItem != null) ? (TextBlockList.IndexOf(dataGridBlocks.SelectedItem as TextBlockItem) + 1) : TextBlockList.Count;
            var newItem = new TextBlockItem
            {
                OriginalText = "[请双击此处以编辑原文]",
                TranslatedText = "",
                Character = "N",
                BgColor = _characterBrushes[_brushIndex++ % _characterBrushes.Length]
            };
            TextBlockList.Insert(insertIndex, newItem);
            RenumberItems();
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (dataGridBlocks.SelectedItems.Count == 0) return;
            if (MessageBox.Show($"确定要删除选中的 {dataGridBlocks.SelectedItems.Count} 行吗？此操作不可恢复。", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                var itemsToDelete = dataGridBlocks.SelectedItems.Cast<TextBlockItem>().ToList();
                foreach (var item in itemsToDelete)
                {
                    TextBlockList.Remove(item);
                }
                RenumberItems();
            }
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (dataGridBlocks.SelectedItem is TextBlockItem selectedItem)
            {
                // 只允许编辑没有关联源对象的行，或手动添加的行
                if (selectedItem.SourceObjectIds != null && selectedItem.SourceObjectIds.Any())
                {
                    MessageBox.Show("不能直接编辑从CAD提取的文本。\n请使用“拆分”功能或在CAD中修改原文后重新提取。", "操作限制", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var editWindow = new EditWindow(selectedItem.OriginalText) { Owner = this };
                if (editWindow.ShowDialog() == true)
                {
                    selectedItem.OriginalText = editWindow.EditedText;
                    // 更新UI
                    selectedItem.Character = string.IsNullOrWhiteSpace(selectedItem.OriginalText) ? "?" : selectedItem.OriginalText.Substring(0, 1).ToUpper();
                    selectedItem.TranslatedText = ""; // 编辑原文后清空译文
                }
            }
        }


        // --- 窗口行为与UI状态管理 ---

        private void UpdateButtonsState()
        {
            bool hasItems = TextBlockList.Any();
            bool isItemSelected = dataGridBlocks.SelectedItem != null;
            int selectedItemsCount = dataGridBlocks.SelectedItems.Count;

            // 主要功能按钮
            BtnTranslate.IsEnabled = hasItems;
            BtnApplyToCAD.IsEnabled = hasItems && TextBlockList.Any(i => !string.IsNullOrWhiteSpace(i.TranslatedText));

            // 表格操作按钮
            BtnMerge.IsEnabled = selectedItemsCount > 1;
            BtnSplit.IsEnabled = isItemSelected;
            BtnDelete.IsEnabled = isItemSelected;
        }

        private void DataGridBlocks_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateButtonsState();
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            WindowBlurHelper.EnableBlur(this);
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        /// <summary>
        /// 显示一个统一的、详细的错误对话框
        /// </summary>
        /// <param name="mainMessage">给用户看的主要错误信息</param>
        /// <param name="ex">捕获到的异常</param>
        private void ShowErrorDialog(string mainMessage, Exception ex)
        {
            var fullErrorMessage = new StringBuilder();
            fullErrorMessage.AppendLine(mainMessage);
            fullErrorMessage.AppendLine("\n--- 错误详情 ---");
            fullErrorMessage.AppendLine($"错误类型: {ex.GetType().Name}");
            fullErrorMessage.AppendLine($"错误信息: {ex.Message}");
            fullErrorMessage.AppendLine("\n--- 技术堆栈跟踪 (用于开发者定位问题) ---");
            fullErrorMessage.AppendLine(ex.StackTrace);

            MessageBox.Show(fullErrorMessage.ToString(), "程序发生错误", MessageBoxButton.OK, MessageBoxImage.Error);

            // 发生严重错误时，尝试将窗口恢复到可用状态
            if (!this.IsEnabled) this.IsEnabled = true;
            if (!this.IsVisible)
            {
                this.Show();
                this.Activate();
            }
        }
    }
}