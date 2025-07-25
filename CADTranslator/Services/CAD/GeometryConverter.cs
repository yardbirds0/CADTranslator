using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries; // ▼▼▼【关键】引入NetTopologySuite
using System;
using System.Collections.Generic;
using System.Linq;

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
            if (entity.Bounds == null || !entity.Bounds.HasValue)
                {
                yield break;
                }

            if (entity is DBText || entity is MText)
                {
                yield return ToNtsPolygon(entity.GeometricExtents);
                yield break;
                }

            // 【核心重构】Hatch（填充）处理逻辑
            if (entity is Hatch hatch)
                {
                // 遍历填充的所有边界环（Loop）
                for (int i = 0; i < hatch.NumberOfLoops; i++)
                    {
                    var loop = hatch.GetLoopAt(i);
                    var coordinates = new List<Coordinate>();

                    // 检查边界环的每一条边
                    foreach (Curve2d curve in loop.Curves)
                        {
                        if (curve is LineSegment2d Singleline)
                            {
                            // 如果是直线段，直接添加端点
                            coordinates.Add(new Coordinate(Singleline.StartPoint.X, Singleline.StartPoint.Y));
                            }
                        else if (curve is CircularArc2d arc)
                            {
                            // 如果是圆弧段，将其离散化为一系列小线段来逼近
                            var arcPoints = TessellateArc(arc, 32); // 32段精度
                            // 添加时要跳过第一个点，因为它和上一段的终点是重合的
                            coordinates.AddRange(arcPoints.Skip(1));
                            }
                        }

                    // 确保环是闭合的
                    if (coordinates.Count > 0 && !coordinates[0].Equals(coordinates.Last()))
                        {
                        coordinates.Add(coordinates[0].Copy());
                        }

                    if (coordinates.Count >= 4) // 一个有效的环至少需要3个顶点+1个闭合点
                        {
                        // 将这个闭合的环转换为一个多边形并返回
                        // 注意：这个简化处理假定每个loop都是一个独立的、无孔的多边形。
                        // 对于最复杂的“岛中岛”填充，这个逻辑还需要进一步完善，但它已经能处理99%的常见情况。
                        yield return Factory.CreatePolygon(coordinates.ToArray());
                        }
                    }
                yield break; // Hatch 处理完毕
                }

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
                            foreach (var geom in ToNtsGeometry(explodedEntity))
                                {
                                yield return geom;
                                }
                            }
                        obj.Dispose();
                        }
                    }
                finally
                    {
                    explodedObjects.Dispose();
                    }
                yield break;
                }

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

        private static Coordinate[] TessellateArc(CircularArc2d arc, int numSegments)
            {
            var points = new List<Coordinate>();
            double startAngle = arc.StartAngle;
            double endAngle = arc.EndAngle;

            // 处理完整的圆
            if (arc.IsClosed()) endAngle = startAngle + 2 * Math.PI;

            // 确保角度是递增的
            if (endAngle < startAngle) endAngle += 2 * Math.PI;

            for (int i = 0; i <= numSegments; i++)
                {
                double angle = startAngle + (endAngle - startAngle) * i / numSegments;
                double x = arc.Center.X + arc.Radius * Math.Cos(angle);
                double y = arc.Center.Y + arc.Radius * Math.Sin(angle);
                points.Add(new Coordinate(x, y));
                }
            return points.ToArray();
            }

        }
    }