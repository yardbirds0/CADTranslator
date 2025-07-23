using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using System.Collections.Generic;
using NetTopologySuite.Geometries; // ▼▼▼【关键】引入NetTopologySuite

namespace CADTranslator.Services.CAD
    {
    /// <summary>
    /// 一个辅助类，负责将AutoCAD的几何对象转换为NetTopologySuite的几何对象。
    /// </summary>
    public static class GeometryConverter
        {
        // 创建一个全局的 GeometryFactory，用于生成所有NetTopologySuite对象
        private static readonly GeometryFactory Factory = new GeometryFactory();

        /// <summary>
        /// 主转换方法：接收一个CAD实体，返回一个或多个NetTopologySuite几何对象。
        /// </summary>
        public static IEnumerable<Geometry> ToNtsGeometry(Entity entity)
            {
            // 【核心修正】首先处理文字对象，将它们的边界框转换为多边形
            if (entity is DBText || entity is MText)
                {
                // 使用我们之前创建的辅助方法，将文字的精确边界框转换为一个NTS多边形
                yield return ToNtsPolygon(entity.GeometricExtents);
                yield break; // 文字对象处理完毕，直接返回
                }

            // 【关键】几何分解：对于复杂对象，我们先在内存中将其炸开
            if (entity is Polyline || entity is Polyline2d || entity is Polyline3d || entity is Autodesk.AutoCAD.DatabaseServices.Dimension || entity is BlockReference)
                {
                var explodedObjects = new DBObjectCollection();
                try
                    {
                    entity.Explode(explodedObjects);
                    foreach (DBObject obj in explodedObjects)
                        {
                        if (obj is Entity explodedEntity)
                            {
                            // 递归调用，处理炸开后的每一个部分
                            foreach (var geom in ToNtsGeometry(explodedEntity))
                                {
                                yield return geom;
                                }
                            }
                        obj.Dispose(); // 释放内存中的临时对象
                        }
                    }
                finally
                    {
                    explodedObjects.Dispose();
                    }
                yield break; // 结束当前实体的处理
                }

            // --- 处理基础几何图元 (这部分保持不变) ---
            Geometry geometry = null;
            if (entity is Line line)
                {
                geometry = Factory.CreateLineString(new[] { ToCoordinate(line.StartPoint), ToCoordinate(line.EndPoint) });
                }
            else if (entity is Arc arc)
                {
                geometry = Factory.CreateLineString(TessellateArc(arc));
                }
            else if (entity is Circle circle)
                {
                geometry = Factory.CreatePolygon(TessellateArc(circle, true));
                }
            else if (entity is Hatch hatch)
                {
                if (hatch.NumberOfLoops > 0)
                    {
                    var coordinates = new List<Coordinate>();
                    var loop = hatch.GetLoopAt(0);
                    if (loop.IsPolyline)
                        {
                        foreach (BulgeVertex vertex in loop.Polyline)
                            {
                            coordinates.Add(new Coordinate(vertex.Vertex.X, vertex.Vertex.Y));
                            }
                        if (!coordinates[0].Equals(coordinates[coordinates.Count - 1]))
                            {
                            coordinates.Add(coordinates[0].Copy());
                            }
                        geometry = Factory.CreatePolygon(coordinates.ToArray());
                        }
                    }
                }

            if (geometry != null && geometry.IsValid)
                {
                yield return geometry;
                }
            }

        // --- 辅助方法 ---
        private static Coordinate ToCoordinate(Point3d point)
            {
            return new Coordinate(point.X, point.Y);
            }

        private static Coordinate[] TessellateArc(Curve curve, bool close = false)
            {
            int numSegments = 32; // 将圆或圆弧分割成32段
            var points = new List<Coordinate>();
            double startParam = curve.StartParam;
            double endParam = curve.EndParam;

            for (int i = 0; i <= numSegments; i++)
                {
                double param = startParam + (endParam - startParam) * i / numSegments;
                points.Add(ToCoordinate(curve.GetPointAtParameter(param)));
                }

            if (close && !points[0].Equals(points[points.Count - 1]))
                {
                points.Add(points[0].Copy());
                }
            return points.ToArray();
            }

        /// <summary>
        /// 【新增】一个辅助方法，将AutoCAD的边界框直接转换为一个NTS多边形。
        /// </summary>
        public static NetTopologySuite.Geometries.Polygon ToNtsPolygon(Extents3d bounds)
            {
            return Factory.CreatePolygon(new[] {
                new Coordinate(bounds.MinPoint.X, bounds.MinPoint.Y),
                new Coordinate(bounds.MaxPoint.X, bounds.MinPoint.Y),
                new Coordinate(bounds.MaxPoint.X, bounds.MaxPoint.Y),
                new Coordinate(bounds.MinPoint.X, bounds.MaxPoint.Y),
                new Coordinate(bounds.MinPoint.X, bounds.MinPoint.Y) // 闭合多边形
            });
            }

        }
    }