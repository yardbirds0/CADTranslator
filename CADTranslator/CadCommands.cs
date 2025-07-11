using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System; // 需要此引用来使用 Exception

namespace CADTranslator
{
    public class CadCommands
    {
        private static TranslatorWindow translatorWindow;

        public static readonly Regex PrimaryListRegex = new Regex(@"^\s*(\d+|[A-Za-z])[\.\)]\s*");
        public static readonly Regex SubListRegex = new Regex(@"^\s*\(\s*(\d+|[A-Za-z])\s*\)\s*");



        [CommandMethod("qqq")]
        public void LaunchRealTimeTranslator()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            var selRes = ed.GetSelection();
            if (selRes.Status != PromptStatus.OK) return;

            // 调用已经增加了安全性的方法
            List<TextBlockItem> textBlocks = ExtractAndMergeText(doc, selRes.Value);

            if (textBlocks.Count == 0)
            {
                ed.WriteMessage("\n选择的对象中未找到有效文字。");
                return;
            }

            if (translatorWindow == null || !translatorWindow.IsLoaded)
            {
                translatorWindow = new TranslatorWindow();
            }

            translatorWindow.Show();
            translatorWindow.Activate();
            translatorWindow.LoadTextBlocks(textBlocks);
        }

        public static List<TextBlockItem> ExtractAndMergeText(Document doc, SelectionSet selSet)
        {
            var extractedEntities = new List<TextEntityInfo>();
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                // ▼▼▼ 核心修正：为实体处理循环增加 try-catch 安全网 ▼▼▼
                foreach (SelectedObject selObj in selSet)
                {
                    // 使用try-catch包裹，防止单个问题实体导致整个程序崩溃
                    try
                    {
                        if (selObj == null) continue;

                        // 尝试以只读方式打开对象
                        var ent = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        string text = "";
                        Point3d position = new Point3d();
                        double height = 1.0;

                        // 尝试读取实体属性
                        if (ent is DBText dbText)
                        {
                            text = dbText.TextString;
                            position = dbText.Position;
                            height = dbText.Height;
                        }
                        else if (ent is MText mText)
                        {
                            text = mText.Contents;
                            position = mText.Location;
                            height = mText.TextHeight;
                        }

                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            extractedEntities.Add(new TextEntityInfo
                            {
                                ObjectId = ent.ObjectId,
                                Text = text.Trim(),
                                Position = position,
                                Height = height
                            });
                        }
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex)
                    {
                        // 如果处理某个实体时发生错误，就在CAD命令行提示一下，然后跳过这个实体继续执行
                        doc.Editor.WriteMessage($"\n警告：跳过一个无法处理的实体。错误信息: {ex.Message}");
                        continue;
                    }
                }
                // ▲▲▲ 修正结束 ▲▲▲
                tr.Commit();
            }

            // 后续的排序和合并逻辑保持不变
            var sortedEntities = extractedEntities.OrderBy(e => -e.Position.Y).ThenBy(e => e.Position.X).ToList();
            var textBlocks = new List<TextBlockItem>();
            if (sortedEntities.Count == 0) return textBlocks;

            var currentBlock = new TextBlockItem
            {
                Id = 1,
                OriginalText = sortedEntities[0].Text,
                SourceObjectIds = new List<ObjectId> { sortedEntities[0].ObjectId }
            };
            textBlocks.Add(currentBlock);

            for (int i = 1; i < sortedEntities.Count; i++)
            {
                var previousEntity = sortedEntities[i - 1];
                var currentEntity = sortedEntities[i];

                bool isPrimaryListItem = PrimaryListRegex.IsMatch(currentEntity.Text);
                bool isSubListItem = SubListRegex.IsMatch(currentEntity.Text);
                double verticalDist = previousEntity.Position.Y - currentEntity.Position.Y;
                bool isTooFar = verticalDist > previousEntity.Height * 3.5;

                if (isPrimaryListItem || isSubListItem || isTooFar)
                {
                    currentBlock = new TextBlockItem
                    {
                        Id = textBlocks.Count + 1,
                        OriginalText = currentEntity.Text,
                        SourceObjectIds = new List<ObjectId> { currentEntity.ObjectId }
                    };
                    textBlocks.Add(currentBlock);
                }
                else
                {
                    currentBlock.OriginalText += "\n" + currentEntity.Text;
                    currentBlock.SourceObjectIds.Add(currentEntity.ObjectId);
                }
            }
            return textBlocks;
        }

        public class TextEntityInfo
        {
            public ObjectId ObjectId { get; set; }
            public string Text { get; set; }
            public Point3d Position { get; set; }
            public double Height { get; set; }
        }


        [CommandMethod("JDX")]
        public void DrawBreakLineCommand()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var editor = doc.Editor;

            var ppr1 = editor.GetPoint("\n请指定截断线起点:");
            if (ppr1.Status != PromptStatus.OK) return;
            Point3d startPoint = ppr1.Value;

            var jig = new BreakLineJig(startPoint);
            var result = editor.Drag(jig);

            if (result.Status == PromptStatus.OK)
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                    // ▼▼▼ 修正2：使用我们新增的公开属性 jig.Polyline 来获取最终实体 ▼▼▼
                    var finalPolyline = jig.Polyline;
                    if (finalPolyline != null)
                    {
                        modelSpace.AppendEntity(finalPolyline);
                        tr.AddNewlyCreatedDBObject(finalPolyline, true);
                    }
                    // ▲▲▲ 修正结束 ▲▲▲

                    tr.Commit();
                }
            }
        }

        [CommandMethod("JLAYOUT")]
        public void SmartLayoutCommand()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var editor = doc.Editor;

            try
            {
                // 1. 选择文字
                var selRes = editor.GetSelection();
                if (selRes.Status != PromptStatus.OK) return;

                // 2. 智能合并
                List<TextBlockItem> textBlocks = ExtractAndMergeText(doc, selRes.Value);
                if (!textBlocks.Any())
                {
                    editor.WriteMessage("\n未在选择中找到有效文字。");
                    return;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                    foreach (var block in textBlocks)
                    {
                        // 3. 提示指定左上角基点
                        var ppr = editor.GetPoint($"\n请为段落“{block.OriginalText.Substring(0, Math.Min(10, block.OriginalText.Length))}...”指定左上角基点:");
                        if (ppr.Status != PromptStatus.OK) { tr.Abort(); return; }
                        Point3d basePoint = ppr.Value;

                        // 4. 从原始实体中提取属性值
                        double textHeight = 2.5, widthFactor = 1.0;
                        ObjectId textStyleId = db.Textstyle;
                        Entity templateEntity = null;

                        using (var firstEntity = tr.GetObject(block.SourceObjectIds.First(), OpenMode.ForRead) as Entity)
                        {
                            if (firstEntity == null) continue;
                            templateEntity = firstEntity.Clone() as Entity;
                            templateEntity.SetPropertiesFrom( firstEntity); // 仅用于最后的属性复制
                            if (firstEntity is DBText dbt) { textHeight = dbt.Height; widthFactor = dbt.WidthFactor; textStyleId = dbt.TextStyleId; }
                            else if (firstEntity is MText mt) { textHeight = mt.TextHeight; textStyleId = mt.TextStyleId; }
                        }
                        if (textHeight <= 0) textHeight = 2.5;

                        // 5. 创建并运行Jig
                        var jig = new SmartLayoutJig(block.OriginalText, basePoint, textHeight, widthFactor, textStyleId);
                        var dragResult = editor.Drag(jig);

                        if (dragResult.Status == PromptStatus.OK)
                        {
                            // 6. Jig成功后，删除旧实体
                            foreach (var objId in block.SourceObjectIds)
                            {
                                using (var entToErase = tr.GetObject(objId, OpenMode.ForWrite)) { entToErase.Erase(); }
                            }

                            // 7. 创建最终的、独立的DBText实体
                            // 我们需要一个临时的MText来帮助我们获取换行后的真实行文本
                            using (var helperMText = new MText())
                            {
                                helperMText.Contents = jig.FinalFormattedText;
                                helperMText.Width = jig.FinalWidth;
                                helperMText.Location = basePoint;
                                helperMText.TextHeight = textHeight;
                                helperMText.TextStyleId = textStyleId;

                                // Explode会将MText炸开成多个DBText
                                var explodedEntities = new DBObjectCollection();
                                helperMText.Explode(explodedEntities);

                                foreach (DBObject obj in explodedEntities)
                                {
                                    if (obj is DBText newText)
                                    {
                                        // 完美克隆属性
                                        newText.SetPropertiesFrom(templateEntity);
                                        newText.Height = textHeight;
                                        newText.WidthFactor = widthFactor;
                                        newText.TextStyleId = textStyleId;
                                        // MText炸开后位置可能不准，重新设置
                                        // (此处为简化逻辑，实际应用可能需要更复杂的坐标转换)

                                        modelSpace.AppendEntity(newText);
                                        tr.AddNewlyCreatedDBObject(newText, true);
                                    }
                                }
                            }
                        }
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage($"\n[JLAYOUT] 命令出错: {ex.Message}");
            }
        }

    }
}