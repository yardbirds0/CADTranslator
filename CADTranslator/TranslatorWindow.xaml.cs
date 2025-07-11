using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CADTranslator
{
    public partial class TranslatorWindow : Window
    {
        public ObservableCollection<TextBlockItem> TextBlockList { get; set; }

        public TranslatorWindow()
        {
            InitializeComponent();
            TextBlockList = new ObservableCollection<TextBlockItem>();
            this.DataContext = this;
            dataGridBlocks.ItemsSource = TextBlockList;
        }

        // --- 窗口效果与行为 ---
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

        // --- 数据加载 ---
        public void LoadTextBlocks(List<TextBlockItem> blocks)
        {
            TextBlockList.Clear();
            blocks.ForEach(b => TextBlockList.Add(b));
        }

        // --- 主要功能按钮 ---
        private async void BtnTranslate_Click(object sender, RoutedEventArgs e)
        {
            var translator = new BaiduTranslator("", txtApiKey.Password); // ID为空时使用内置默认ID
            foreach (var item in TextBlockList)
            {
                if (string.IsNullOrWhiteSpace(item.OriginalText) || !string.IsNullOrWhiteSpace(item.TranslatedText)) continue;
                try
                {
                    item.TranslatedText = await translator.TranslateAsync(item.OriginalText, "auto", "zh"); // 目标语言可修改为"zh"等
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"翻译第 {item.Id} 行时出错: {ex.Message}", "翻译错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
                }
            }
        }

        private void BtnApplyToCAD_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (doc.LockDocument())
            {
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    try
                    {
                        BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);

                        foreach (var item in TextBlockList)
                        {
                            if (string.IsNullOrWhiteSpace(item.TranslatedText) || item.SourceObjectIds == null || !item.SourceObjectIds.Any()) continue;

                            var firstObjectId = item.SourceObjectIds.First();
                            if (firstObjectId.IsNull || firstObjectId.IsErased) continue;

                            var baseEntity = tr.GetObject(firstObjectId, OpenMode.ForRead) as Entity;
                            if (baseEntity == null) continue;

                            foreach (var objectId in item.SourceObjectIds)
                            {
                                if (objectId.IsNull || objectId.IsErased) continue;
                                var entityToErase = tr.GetObject(objectId, OpenMode.ForWrite) as Entity;
                                entityToErase?.Erase();
                            }

                            string singleLineText = item.TranslatedText.Replace('\n', ' ').Replace('\r', ' ');

                            using (DBText newText = new DBText())
                            {
                                newText.TextString = singleLineText;
                                newText.SetPropertiesFrom(baseEntity);

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
                                    if (newText.HorizontalMode != TextHorizontalMode.TextLeft || newText.VerticalMode != TextVerticalMode.TextBase)
                                    {
                                        newText.AlignmentPoint = originalDbText.AlignmentPoint;
                                        newText.Position = originalDbText.AlignmentPoint;
                                    }
                                }
                                else if (baseEntity is MText originalMText)
                                {
                                    newText.Position = originalMText.Location;
                                    newText.Height = originalMText.TextHeight;
                                    newText.Rotation = originalMText.Rotation;
                                    newText.TextStyleId = originalMText.TextStyleId;
                                    newText.HorizontalMode = TextHorizontalMode.TextLeft;
                                    newText.VerticalMode = TextVerticalMode.TextBase;
                                }
                                if (newText.Height <= 0) newText.Height = 2.5;

                                modelSpace.AppendEntity(newText);
                                tr.AddNewlyCreatedDBObject(newText, true);
                            }
                        }
                        tr.Commit();
                        MessageBox.Show("所有翻译已成功应用到CAD图纸！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (System.Exception ex)
                    {
                        tr.Abort();
                        MessageBox.Show($"应用到CAD时发生错误：\n{ex.Message}", "失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnJigPreview_Click(object sender, RoutedEventArgs e)
        {
            if (!(dataGridBlocks.SelectedItem is TextBlockItem selectedItem) || string.IsNullOrWhiteSpace(selectedItem.TranslatedText))
            {
                MessageBox.Show("请先选择一个已有译文的行。");
                return;
            }

            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var editor = doc.Editor;

            int.TryParse(txtMaxCharsPerLine.Text, out int maxChars);
            string formattedText = TextFormatter.Format(selectedItem.TranslatedText, maxChars > 0 ? maxChars : 30, 0);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var textStyle = tr.GetObject(db.Textstyle, OpenMode.ForRead) as TextStyleTableRecord;
                double textHeight = textStyle?.TextSize ?? 2.5;
                if (textHeight <= 0)
                {
                    textHeight = 2.5;
                }
                var jig = new TextLayoutJig(formattedText, editor.CurrentUserCoordinateSystem.CoordinateSystem3d.Origin, textHeight, db.Textstyle);

                this.Hide();
                try
                {
                    var status = jig.Run();
                    if (status == PromptStatus.OK)
                    {
                        var newMText = new MText
                        {
                            Contents = formattedText,
                            Location = jig.ResultPosition,
                            TextHeight = textHeight,
                            TextStyleId = db.Textstyle
                        };
                        var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                        modelSpace.AppendEntity(newMText);
                        tr.AddNewlyCreatedDBObject(newMText, true);
                    }
                }
                finally
                {
                    this.Show();
                }
                tr.Commit();
            }
        }

        private void BtnSmartLayout_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                var db = doc.Database;
                var editor = doc.Editor;

                this.Hide();

                var selectionOptions = new PromptSelectionOptions { MessageForAdding = "\n请选择需要重新排版的文字实体..." };
                var selectionResult = editor.GetSelection(selectionOptions);
                if (selectionResult.Status != PromptStatus.OK) { this.Show(); return; }

                List<TextBlockItem> textBlocks = CadCommands.ExtractAndMergeText(doc, selectionResult.Value);
                if (!textBlocks.Any())
                {
                    editor.WriteMessage("\n未在选择中找到有效文字。");
                    this.Show(); return;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                    foreach (var block in textBlocks)
                    {
                        var basePointOptions = new PromptPointOptions($"\n请为段落“{block.OriginalText.Substring(0, Math.Min(10, block.OriginalText.Length))}...”指定左上角基点:");
                        var basePointResult = editor.GetPoint(basePointOptions);
                        if (basePointResult.Status != PromptStatus.OK) { tr.Abort(); this.Show(); return; }
                        Point3d basePoint = basePointResult.Value;

                        var widthPointOptions = new PromptPointOptions("\n请指定排版宽度（最右侧点）:") { UseBasePoint = true, BasePoint = basePoint };
                        var widthPointResult = editor.GetPoint(widthPointOptions);
                        if (widthPointResult.Status != PromptStatus.OK) { tr.Abort(); this.Show(); return; }
                        Point3d widthPoint = widthPointResult.Value;
                        double layoutWidth = Math.Abs(widthPoint.X - basePoint.X);
                        if (layoutWidth < 1e-6) layoutWidth = 1.0;

                        // ▼▼▼ 核心修正：彻底分离读写，只保存“值”，不保留“对象引用” ▼▼▼

                        // 1. 【只读操作】定义一系列局部变量，用于存储模板属性的“值”
                        string layer;
                        Autodesk.AutoCAD.Colors.Color color;
                        ObjectId linetypeId;
                        double linetypeScale;
                        LineWeight lineWeight;
                        double thickness;
                        double textHeight;
                        double widthFactor;
                        double oblique;
                        double rotation;
                        ObjectId textStyleId;

                        var firstEntityId = block.SourceObjectIds.First();
                        if (firstEntityId.IsNull || firstEntityId.IsErased) continue;

                        // 使用 using 语句块确保对象在读取完毕后立刻被彻底“关闭”和“释放”
                        using (var firstEntity = tr.GetObject(firstEntityId, OpenMode.ForRead) as Entity)
                        {
                            if (firstEntity == null) continue;

                            // 将所有需要的属性值复制到局部变量中
                            layer = firstEntity.Layer;
                            color = firstEntity.Color;
                            linetypeId = firstEntity.LinetypeId;
                            linetypeScale = firstEntity.LinetypeScale;
                            lineWeight = firstEntity.LineWeight;
                            thickness = firstEntity is DBText dbText ? dbText.Thickness : 0.0;
                            if (firstEntity is DBText dbt)
                            {
                                textHeight = dbt.Height; textStyleId = dbt.TextStyleId; widthFactor = dbt.WidthFactor;
                                oblique = dbt.Oblique; rotation = dbt.Rotation;
                            }
                            else if (firstEntity is MText mt)
                            {
                                textHeight = mt.TextHeight; textStyleId = mt.TextStyleId; rotation = mt.Rotation;
                                widthFactor = 1.0; oblique = 0.0; // MText没有这些属性，使用默认值
                            }
                            else // 如果不是文字，则使用默认值
                            {
                                textHeight = 2.5; textStyleId = db.Textstyle; widthFactor = 1.0; oblique = 0.0; rotation = 0.0;
                            }
                        } // using 块结束，firstEntity对象被彻底释放，锁解除！

                        if (textHeight <= 0) textHeight = 2.5;

                        // 2. 【写入操作】现在所有对象都未被锁定，可以安全地打开并删除了
                        foreach (var objId in block.SourceObjectIds)
                        {
                            if (objId.IsNull || objId.IsErased) continue;
                            using (var entityToErase = tr.GetObject(objId, OpenMode.ForWrite) as Entity)
                            {
                                entityToErase?.Erase();
                            }
                        }

                        // ▲▲▲ 修正结束 ▲▲▲

                        var words = block.OriginalText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        var lines = new List<string>();
                        var currentLine = new StringBuilder();
                        double indentWidth = 0;

                        using (var textStyle = tr.GetObject(textStyleId, OpenMode.ForRead) as TextStyleTableRecord)
                        {
                            if (textStyle != null)
                            {
                                using (var tempText = new DBText() { TextString = "WW", TextStyleId = textStyle.ObjectId, Height = textHeight }) { indentWidth = (tempText.GeometricExtents.MaxPoint.X - tempText.GeometricExtents.MinPoint.X); }
                            }
                        }

                        foreach (var word in words)
                        {
                            var testLine = (currentLine.Length > 0) ? currentLine.ToString() + " " + word : word;
                            using (var tempText = new DBText() { TextString = testLine, TextStyleId = textStyleId, Height = textHeight, WidthFactor = widthFactor })
                            {
                                if ((tempText.GeometricExtents.MaxPoint.X - tempText.GeometricExtents.MinPoint.X) > layoutWidth && currentLine.Length > 0)
                                {
                                    lines.Add(currentLine.ToString());
                                    currentLine.Clear().Append(word);
                                }
                                else
                                {
                                    if (currentLine.Length > 0) currentLine.Append(" ");
                                    currentLine.Append(word);
                                }
                            }
                        }
                        if (currentLine.Length > 0) lines.Add(currentLine.ToString());

                        for (int i = 0; i < lines.Count; i++)
                        {
                            using (DBText newText = new DBText())
                            {
                                // 3. 创建新实体时，使用我们之前保存的“值”来设置属性，而不是用对象引用
                                newText.TextString = lines[i];
                                newText.Layer = layer;
                                newText.Color = color;
                                newText.LinetypeId = linetypeId;
                                newText.LinetypeScale = linetypeScale;
                                newText.LineWeight = lineWeight;
                                newText.Thickness = thickness;
                                newText.Height = textHeight;
                                newText.Rotation = rotation;
                                newText.Oblique = oblique;
                                newText.WidthFactor = widthFactor;
                                newText.TextStyleId = textStyleId;

                                double verticalOffset = i * newText.Height * 1.5;
                                Point3d linePosition = (i == 0) ? basePoint : new Point3d(basePoint.X + indentWidth, basePoint.Y - verticalOffset, basePoint.Z);

                                newText.Position = linePosition;
                                newText.HorizontalMode = TextHorizontalMode.TextLeft;
                                newText.VerticalMode = TextVerticalMode.TextTop;
                                newText.AlignmentPoint = linePosition;

                                modelSpace.AppendEntity(newText);
                                tr.AddNewlyCreatedDBObject(newText, true);
                            }
                        }
                    }
                    tr.Commit();
                    editor.WriteMessage($"\n成功完成智能排版。");
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"智能排版过程中发生严重错误，操作已中断。\n\n错误类型: {ex.GetType().Name}\n错误信息: {ex.Message}\n\n详细堆栈跟踪:\n{ex.StackTrace}",
                                "CAD Translator - 运行时错误",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[CAD Translator] 错误: {ex.Message}");
            }
            finally
            {
                if (!this.IsVisible)
                {
                    this.Show();
                }
            }
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
                SourceObjectIds = selectedItems.SelectMany(i => i.SourceObjectIds).ToList()
            };
            int firstIndex = TextBlockList.IndexOf(firstItem);
            foreach (var item in selectedItems.AsEnumerable().Reverse()) { TextBlockList.Remove(item); }
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
            selectedItem.OriginalText = lines[0];
            selectedItem.TranslatedText = "";
            int insertIndex = TextBlockList.IndexOf(selectedItem) + 1;
            for (int i = 1; i < lines.Length; i++)
            {
                var newItem = new TextBlockItem { OriginalText = lines[i], SourceObjectIds = new List<ObjectId>() };
                TextBlockList.Insert(insertIndex++, newItem);
            }
            RenumberItems();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            int insertIndex = (dataGridBlocks.SelectedItem != null) ? (TextBlockList.IndexOf(dataGridBlocks.SelectedItem as TextBlockItem) + 1) : TextBlockList.Count;
            TextBlockList.Insert(insertIndex, new TextBlockItem { OriginalText = "[请双击此处以编辑原文]", TranslatedText = "" });
            RenumberItems();
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (dataGridBlocks.SelectedItems.Count == 0) return;
            if (MessageBox.Show($"确定要删除选中的 {dataGridBlocks.SelectedItems.Count} 行吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                var itemsToDelete = dataGridBlocks.SelectedItems.Cast<TextBlockItem>().ToList();
                foreach (var item in itemsToDelete) { TextBlockList.Remove(item); }
                RenumberItems();
            }
        }

        private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (dataGridBlocks.SelectedItem is TextBlockItem selectedItem)
            {
                var editWindow = new EditWindow(selectedItem.OriginalText) { Owner = this };
                if (editWindow.ShowDialog() == true)
                {
                    selectedItem.OriginalText = editWindow.EditedText;
                    selectedItem.TranslatedText = "";
                }
            }
        }

        private void RenumberItems()
        {
            for (int i = 0; i < TextBlockList.Count; i++)
            {
                TextBlockList[i].Id = i + 1;
            }
        }

        private void BtnJigLayout_Click(object sender, RoutedEventArgs e)
        {
            // 隐藏窗口，然后通过SendCommand在CAD命令行中执行新命令
            this.Hide();
            var doc = Application.DocumentManager.MdiActiveDocument;
            // 注意命令前的空格，它等同于在命令行按了一下回车
            doc.SendStringToExecute(" JLAYOUT ", true, false, false);
        }
    }
}