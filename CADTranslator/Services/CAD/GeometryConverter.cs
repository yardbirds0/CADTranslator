using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CADTranslator.Services.CAD
    {
    public static class GeometryConverter
        {
        private static readonly GeometryFactory Factory = new GeometryFactory();

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

            if (entity is Hatch hatch)
                {
                for (int i = 0; i < hatch.NumberOfLoops; i++)
                    {
                    var loop = hatch.GetLoopAt(i);
                    var coordinates = new List<Coordinate>();

                    if (loop.Curves == null) continue;

                    foreach (Curve2d curve in loop.Curves)
                        {
                        if (curve is LineSegment2d Singleline)
                            {
                            coordinates.Add(new Coordinate(Singleline.StartPoint.X, Singleline.StartPoint.Y));
                            }
                        else if (curve is CircularArc2d arc)
                            {
                            var arcPoints = TessellateArc(arc, 32);
                            coordinates.AddRange(arcPoints.Skip(1));
                            }
                        }

                    if (coordinates.Count > 0 && !coordinates[0].Equals(coordinates.Last()))
                        {
                        coordinates.Add(coordinates[0].Copy());
                        }

                    if (coordinates.Count >= 4)
                        {
                        yield return Factory.CreatePolygon(coordinates.ToArray());
                        }
                    }
                yield break;
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

        private static Coordinate ToCoordinate(Point3d point)
            {
            return new Coordinate(point.X, point.Y);
            }

        private static Coordinate[] TessellateArc(Curve curve, bool close = false)
            {
            int numSegments = 32;
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

        public static NetTopologySuite.Geometries.Polygon ToNtsPolygon(Extents3d bounds)
            {
            return Factory.CreatePolygon(new[] {
                new Coordinate(bounds.MinPoint.X, bounds.MinPoint.Y),
                new Coordinate(bounds.MaxPoint.X, bounds.MinPoint.Y),
                new Coordinate(bounds.MaxPoint.X, bounds.MaxPoint.Y),
                new Coordinate(bounds.MinPoint.X, bounds.MaxPoint.Y),
                new Coordinate(bounds.MinPoint.X, bounds.MinPoint.Y)
            });
            }

        private static Coordinate[] TessellateArc(CircularArc2d arc, int numSegments)
            {
            var points = new List<Coordinate>();
            double startAngle = arc.StartAngle;
            double endAngle = arc.EndAngle;

            if (arc.IsClosed()) endAngle = startAngle + 2 * Math.PI;

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