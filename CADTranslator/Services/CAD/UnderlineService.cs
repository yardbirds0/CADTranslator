// 文件路径: CADTranslator/Services/UnderlineService.cs
// 【请用此代码完整替换】

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models.CAD;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CADTranslator.Services.CAD
    {
    /// <summary>
    /// 提供为文字添加下划线功能的服务
    /// </summary>
    public class UnderlineService
        {
        private readonly Document _doc;
        private readonly Database _db;
        private readonly Editor _editor;

        public UnderlineService(Document doc)
            {
            _doc = doc;
            _db = doc.Database;
            _editor = doc.Editor;
            }

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
        /// 【新增方法】这是给其他命令自动化调用的新入口。
        /// 它直接接收一个ID列表，不再需要用户交互。
        /// </summary>
        public void AddUnderlinesToObjectIds(List<ObjectId> objectIds, UnderlineOptions options)
            {
            if (objectIds == null || !objectIds.Any()) return;

            // 将ID列表转换成CAD能够理解的选择集
            var selSet = SelectionSet.FromObjectIds(objectIds.ToArray());

            // 调用核心的执行逻辑
            ExecuteUnderlining(selSet, options);
            }

        #endregion

        #region --- 3. 核心执行逻辑 ---

        /// <summary>
        /// 【新增方法】这是真正干活的核心方法，它被上面两个入口共享。
        /// </summary>
        private void ExecuteUnderlining(SelectionSet selSet, UnderlineOptions options)
            {
            using (var tr = _db.TransactionManager.StartTransaction())
                {
                try
                    {
                    EnsureLayerAndLinetype(tr, options.Layer, options.Linetype);

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
                        // 只有在交互模式下才需要提示，自动化调用时静默处理
                        if (_editor.IsQuiescent)
                            {
                            _editor.WriteMessage("\n选择的对象中未找到任何有效文字。");
                            }
                        return;
                        }

                    var overallBounds = new Extents3d();
                    double maxWidth = 0;
                    foreach (var ent in textEntities)
                        {
                        if (ent.Bounds.HasValue)
                            {
                            overallBounds.AddExtents(ent.Bounds.Value);
                            maxWidth = Math.Max(maxWidth, ent.Bounds.Value.MaxPoint.X - ent.Bounds.Value.MinPoint.X);
                            }
                        }

                    var lines = textEntities.GroupBy(e => e.Bounds.Value.MinPoint.Y.ToString("F4"))
                                            .OrderByDescending(g => g.Key)
                                            .ToList();

                    var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);

                    foreach (var lineGroup in lines)
                        {
                        double lineY = lineGroup.First().Bounds.Value.MinPoint.Y;

                        Point3d startPoint = new Point3d(overallBounds.MinPoint.X, lineY + options.VerticalOffset, 0);
                        Point3d endPoint = new Point3d(overallBounds.MinPoint.X + maxWidth, lineY + options.VerticalOffset, 0);

                        using (var underline = new Line(startPoint, endPoint))
                            {
                            underline.SetDatabaseDefaults();
                            underline.Layer = options.Layer;
                            underline.ColorIndex = options.ColorIndex;
                            underline.Linetype = options.Linetype;
                            underline.LinetypeScale = options.LinetypeScale;

                            modelSpace.AppendEntity(underline);
                            tr.AddNewlyCreatedDBObject(underline, true);
                            }
                        }

                    tr.Commit();

                    if (_editor.IsQuiescent)
                        {
                        _editor.WriteMessage($"\n成功为 {lines.Count} 行文字添加了下划线。");
                        }
                    }
                catch (Exception ex)
                    {
                    _editor.WriteMessage($"\n添加下划线时出错: {ex.Message}");
                    tr.Abort();
                    }
                }
            }

        #endregion

        #region --- 4. 辅助方法 ---
        private void EnsureLayerAndLinetype(Transaction tr, string layerName, string linetypeName)
            {
            // 确保图层存在
            var layerTable = (LayerTable)tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
            if (!layerTable.Has(layerName))
                {
                tr.GetObject(_db.LayerTableId, OpenMode.ForWrite); // 切换为写模式
                using (var newLayer = new LayerTableRecord())
                    {
                    newLayer.Name = layerName;
                    layerTable.Add(newLayer);
                    tr.AddNewlyCreatedDBObject(newLayer, true);
                    }
                }

            // 确保线型存在
            var linetypeTable = (LinetypeTable)tr.GetObject(_db.LinetypeTableId, OpenMode.ForRead);
            // 如果线型不是 "ByLayer" 或 "ByBlock"，并且在表中不存在，则尝试加载
            if (!string.Equals(linetypeName, "ByLayer", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(linetypeName, "ByBlock", StringComparison.OrdinalIgnoreCase) &&
                !linetypeTable.Has(linetypeName))
                {
                try
                    {
                    _db.LoadLineTypeFile(linetypeName, "acad.lin");
                    }
                catch
                    {
                    _editor.WriteMessage($"\n警告: 无法从 acad.lin 加载线型 '{linetypeName}'。将使用默认连续线型。");
                    }
                }
            }
        #endregion
        }
    }