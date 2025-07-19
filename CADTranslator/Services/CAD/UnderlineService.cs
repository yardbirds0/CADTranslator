// 文件路径: CADTranslator/Services/UnderlineService.cs

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models;
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

        /// <summary>
        /// 执行添加下划线的主逻辑
        /// </summary>
        public void AddUnderlinesToSelectedText(UnderlineOptions options)
            {
            var selRes = _editor.GetSelection();
            if (selRes.Status != PromptStatus.OK) return;

            using (var tr = _db.TransactionManager.StartTransaction())
                {
                try
                    {
                    EnsureLayerAndLinetype(tr, options.Layer, options.Linetype);

                    var textEntities = new List<Entity>();
                    foreach (SelectedObject selObj in selRes.Value)
                        {
                        var ent = tr.GetObject(selObj.ObjectId, OpenMode.ForRead) as Entity;
                        if (ent is DBText || ent is MText)
                            {
                            textEntities.Add(ent);
                            }
                        }

                    if (textEntities.Count == 0)
                        {
                        _editor.WriteMessage("\n选择的对象中未找到任何有效文字。");
                        return;
                        }

                    // 计算整个文本块的边界和最大宽度
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

                    // 按Y坐标对文本进行分组，识别出每一行
                    var lines = textEntities.GroupBy(e => e.Bounds.Value.MinPoint.Y.ToString("F4"))
                                            .OrderByDescending(g => g.Key)
                                            .ToList();

                    var modelSpace = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(_db), OpenMode.ForWrite);

                    foreach (var lineGroup in lines)
                        {
                        double lineY = lineGroup.First().Bounds.Value.MinPoint.Y;

                        // 根据文本块的整体左边界和计算出的最大宽度来确定下划线的位置
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
                    _editor.WriteMessage($"\n成功为 {lines.Count} 行文字添加了下划线。");
                    }
                catch (Exception ex)
                    {
                    _editor.WriteMessage($"\n添加下划线时出错: {ex.Message}");
                    tr.Abort();
                    }
                }
            }

        /// <summary>
        /// 确保目标图层和线型存在，如果不存在则创建。
        /// </summary>
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
        }
    }
