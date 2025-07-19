using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models;
using CADTranslator.Models.CAD;
using CADTranslator.ViewModels;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CADTranslator.Services.CAD
    {
    public class CadTextService
        {
        // 同样设为私有字段
        private readonly Document _doc;

        public CadTextService(Document doc)
            {
            _doc = doc;
            }

        public List<TextBlockViewModel> ExtractAndMergeText(SelectionSet selSet)
            {
            var extractedEntities = new List<TextEntityInfo>();
            using (Transaction tr = _doc.Database.TransactionManager.StartTransaction())
                {
                var textIds = new List<ObjectId>();
                foreach (SelectedObject selObj in selSet)
                    {
                    if (selObj.ObjectId.ObjectClass.DxfName.EndsWith("TEXT"))
                        {
                        textIds.Add(selObj.ObjectId);
                        }
                    }

                foreach (ObjectId id in textIds)
                    {
                    try
                        {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        string text = "";
                        Point3d position = new Point3d();
                        double height = 1.0;

                        if (ent is DBText dbText)
                            {
                            text = dbText.TextString;
                            position = dbText.Position;
                            height = dbText.Height;
                            }
                        else if (ent is MText mText)
                            {
                            text = mText.Text;
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
                        _doc.Editor.WriteMessage($"\n警告：跳过一个无法处理的文字实体。错误信息: {ex.Message}");
                        }
                    }
                tr.Commit();
                }

            var sortedEntities = extractedEntities.OrderBy(e => -e.Position.Y).ThenBy(e => e.Position.X).ToList();
            var textBlocks = new List<TextBlockViewModel>();
            if (sortedEntities.Count == 0) return textBlocks;

            var currentBlock = new TextBlockViewModel { Id = 1, OriginalText = sortedEntities[0].Text, SourceObjectIds = new List<ObjectId> { sortedEntities[0].ObjectId } };
            textBlocks.Add(currentBlock);

            var paragraphMarkers = new Regex(@"^\s*(?:\d+[、\.]|\(\d+\)\.?)");
            // 【新增】正则：匹配段落结尾的标点
            var endOfParagraphMarkers = new Regex(@"[：:。]\s*$");

            for (int i = 1; i < sortedEntities.Count; i++)
                {
                var previousEntity = sortedEntities[i - 1];
                var currentEntity = sortedEntities[i];

                double verticalDist = previousEntity.Position.Y - currentEntity.Position.Y;
                bool isTooFar = verticalDist > previousEntity.Height * 3.5;
                bool startsNewParagraph = paragraphMarkers.IsMatch(currentEntity.Text);
                // 【新增】判断上一段是否以结束符结尾
                bool prevBlockEnds = endOfParagraphMarkers.IsMatch(currentBlock.OriginalText);

                // 【核心修改】在判断条件中加入新规则
                if (startsNewParagraph || isTooFar || prevBlockEnds)
                    {
                    currentBlock = new TextBlockViewModel { Id = textBlocks.Count + 1, OriginalText = currentEntity.Text, SourceObjectIds = new List<ObjectId> { currentEntity.ObjectId } };
                    textBlocks.Add(currentBlock);
                    }
                else
                    {
                    currentBlock.OriginalText += " " + currentEntity.Text;
                    currentBlock.SourceObjectIds.Add(currentEntity.ObjectId);
                    }
                }
            return textBlocks;
            }
        }
    }
