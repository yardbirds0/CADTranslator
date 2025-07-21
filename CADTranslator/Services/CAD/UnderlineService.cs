// 文件路径: CADTranslator/Services/CAD/UnderlineService.cs
// 【请用此代码完整替换】

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models.CAD;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions; // ◄◄◄ 【新增】引入正则表达式库

namespace CADTranslator.Services.CAD
    {
    /// <summary>
    /// 提供为文字添加下划线功能的服务。
    /// 【已升级】现在能够为标题和普通文本应用不同的下划线样式。
    /// </summary>
    public class UnderlineService
        {
        #region --- 字段与构造函数 ---

        private readonly Document _doc;
        private readonly Database _db;
        private readonly Editor _editor;

        // 【新增】用于识别标题的正则表达式，与AdvancedTextService保持一致
        private readonly Regex _titleMarkers = new Regex(@"(说明|注意|技术要求|参数|示例|NOTES|SPECIFICATION|LEGEND|DESCRIPTION)[\s:：]*$|:$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public UnderlineService(Document doc)
            {
            _doc = doc;
            _db = doc.Database;
            _editor = doc.Editor;
            }

        #endregion

        #region --- 1. 交互式入口 (供 WZXHX 命令调用) ---

        /// <summary>
        /// 【保持不变】这是给用户直接使用的 WZXHX 命令的入口。
        /// 它会提示用户去选择对象。
        /// </summary>
        public void AddUnderlinesToSelectedText(UnderlineOptions options)
            {
            var selRes = _editor.GetSelection();
            if (selRes.Status != PromptStatus.OK) return;

            // 调用核心的执行逻辑
            ExecuteUnderlining(selRes.Value, options);
            }

        #endregion

        #region --- 2. 自动化入口 (供 WZPB 命令调用) ---

        /// <summary>
        /// 【保持不变】这是给其他命令自动化调用的入口。
        /// </summary>
        public void AddUnderlinesToObjectIds(List<ObjectId> objectIds, UnderlineOptions options)
            {
            if (objectIds == null || !objectIds.Any()) return;

            var selSet = SelectionSet.FromObjectIds(objectIds.ToArray());

            // 调用核心的执行逻辑
            ExecuteUnderlining(selSet, options);
            }

        #endregion

        // 文件路径: CADTranslator/Services/CAD/UnderlineService.cs

        #region --- 3. 核心执行逻辑 (ExecuteUnderlining) ---

        /// <summary>
        /// 【核心修改】这是真正干活的核心方法，已全面升级。
        /// </summary>
        private void ExecuteUnderlining(SelectionSet selSet, UnderlineOptions options)
            {
            using (var tr = _db.TransactionManager.StartTransaction())
                {
                try
                    {
                    // 一次性确保所有可能用到的图层和线型都存在
                    EnsureLayerAndLinetype(tr, options.TitleStyle);
                    EnsureLayerAndLinetype(tr, options.DefaultStyle);

                    var textEntities = new List<Entity>();
                    foreach (SelectedObject selObj in selSet)
                        {
                        var ent = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent is DBText || ent is MText)
                            {
                            textEntities.Add(ent);
                            }
                        }

                    if (textEntities.Count == 0)
                        {
                        if (_editor.IsQuiescent) _editor.WriteMessage("\n选择的对象中未找到任何有效文字。");
                        return;
                        }

                    // 计算所有选中文字的整体范围和最大宽度
                    var overallBounds = new Extents3d();
                    foreach (var ent in textEntities)
                        {
                        // 【安全检查】只有在Bounds有值时才进行计算
                        if (ent.Bounds.HasValue) overallBounds.AddExtents(ent.Bounds.Value);
                        }
                    double overallWidth = overallBounds.MaxPoint.X - overallBounds.MinPoint.X;

                    // ▼▼▼【最终修正】在这里添加了 .Where(e => e.Bounds.HasValue) 来过滤掉无效对象 ▼▼▼
                    var lines = textEntities
                        .Where(e => e.Bounds.HasValue) // 只处理有有效边界的文字
                        .GroupBy(e => e.Bounds.Value.MinPoint.Y.ToString("F4"))
                        .OrderByDescending(g => g.Key)
                        .ToList();

                    var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);
                    int linesUnderlined = 0;

                    foreach (var lineGroup in lines)
                        {
                        string combinedText = string.Join(" ", lineGroup.Select(GetTextFromEntity));

                        bool isTitle = _titleMarkers.IsMatch(combinedText.Trim());
                        UnderlineStyle styleToUse = isTitle ? options.TitleStyle : options.DefaultStyle;

                        if (isTitle)
                            {
                            // --- 情况A: 是标题行 ---
                            var titleBounds = new Extents3d();
                            double maxTitleHeight = 0;

                            foreach (var ent in lineGroup)
                                {
                                if (ent.Bounds.HasValue) titleBounds.AddExtents(ent.Bounds.Value);

                                double currentHeight = (ent is DBText dbText) ? dbText.Height : (ent as MText)?.TextHeight ?? 0;
                                if (currentHeight > maxTitleHeight) maxTitleHeight = currentHeight;
                                }

                            double titleVerticalOffset = -(maxTitleHeight / 4.0);
                            double lineY = titleBounds.MinPoint.Y + titleVerticalOffset;
                            Point2d startPoint = new Point2d(titleBounds.MinPoint.X, lineY);
                            Point2d endPoint = new Point2d(titleBounds.MaxPoint.X, lineY);

                            using (var underline = new Polyline())
                                {
                                underline.AddVertexAt(0, startPoint, 0, 0, 0);
                                underline.AddVertexAt(1, endPoint, 0, 0, 0);

                                underline.SetDatabaseDefaults();
                                underline.Layer = styleToUse.Layer;
                                underline.ColorIndex = styleToUse.ColorIndex;
                                underline.Linetype = styleToUse.Linetype;
                                underline.LinetypeScale = styleToUse.LinetypeScale;
                                underline.ConstantWidth = styleToUse.GlobalWidth;

                                modelSpace.AppendEntity(underline);
                                tr.AddNewlyCreatedDBObject(underline, true);
                                }
                            }
                        else
                            {
                            // --- 情况B: 是普通行 ---
                            double lineY = lineGroup.First().Bounds.Value.MinPoint.Y + options.VerticalOffset;
                            Point3d startPoint = new Point3d(overallBounds.MinPoint.X, lineY, 0);
                            Point3d endPoint = new Point3d(overallBounds.MinPoint.X + overallWidth, lineY, 0);

                            using (var underline = new Line(startPoint, endPoint))
                                {
                                underline.SetDatabaseDefaults();
                                underline.Layer = styleToUse.Layer;
                                underline.ColorIndex = styleToUse.ColorIndex;
                                underline.Linetype = styleToUse.Linetype;
                                underline.LinetypeScale = styleToUse.LinetypeScale;

                                modelSpace.AppendEntity(underline);
                                tr.AddNewlyCreatedDBObject(underline, true);
                                }
                            }
                        linesUnderlined++;
                        }

                    tr.Commit();
                    if (_editor.IsQuiescent) _editor.WriteMessage($"\n成功为 {linesUnderlined} 行文字添加了下划线。");
                    }
                catch (Exception ex)
                    {
                    _editor.WriteMessage($"\n添加下划线时出错: {ex.Message}\n{ex.StackTrace}");
                    tr.Abort();
                    }
                }
            }

        #endregion

        #region --- 4. 辅助方法 ---

        /// <summary>
        /// 【已修改】确保下划线样式中定义的图层和线型都存在。
        /// </summary>
        private void EnsureLayerAndLinetype(Transaction tr, UnderlineStyle style)
            {
            // 确保图层存在
            var layerTable = (LayerTable)tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
            if (!layerTable.Has(style.Layer))
                {
                // 如果需要写入，则必须用写模式打开对象
                layerTable.UpgradeOpen();
                using (var newLayer = new LayerTableRecord { Name = style.Layer })
                    {
                    layerTable.Add(newLayer);
                    tr.AddNewlyCreatedDBObject(newLayer, true);
                    }
                layerTable.DowngradeOpen(); // 操作完成，降回读模式
                }

            // 确保线型存在
            var linetypeTable = (LinetypeTable)tr.GetObject(_db.LinetypeTableId, OpenMode.ForRead);
            if (!string.Equals(style.Linetype, "ByLayer", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(style.Linetype, "ByBlock", StringComparison.OrdinalIgnoreCase) &&
                !linetypeTable.Has(style.Linetype))
                {
                try { _db.LoadLineTypeFile(style.Linetype, "acad.lin"); }
                catch { _editor.WriteMessage($"\n警告: 无法从 acad.lin 加载线型 '{style.Linetype}'。将使用默认连续线型。"); }
                }
            }

        /// <summary>
        /// 【新增】一个安全的、统一的从DBText或MText获取文本的方法。
        /// </summary>
        private string GetTextFromEntity(Entity ent)
            {
            if (ent is DBText dbText) return dbText.TextString;
            if (ent is MText mText) return mText.Text;
            return string.Empty;
            }

        #endregion
        }
    }