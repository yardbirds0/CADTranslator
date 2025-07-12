using Autodesk.AutoCAD.Geometry;
using System;

namespace CADTranslator.AutoCAD.Helpers
{
    public static class GeometryHelper
    {
        /// <summary>
        /// 根据起点和终点，创建截断线的顶点集合
        /// </summary>
        /// <param name="startPoint">起点</param>
        /// <param name="endPoint">终点</param>
        /// <param name="gap">中间Z字形折线的尺寸，可以按需调整</param>
        /// <returns>包含所有顶点的集合</returns>
        public static Point3dCollection CreateVertices(Point3d startPoint, Point3d endPoint, double gap = 2.5)
        {
            // 计算起点和终点之间的向量、距离和角度
            Vector3d vector = endPoint - startPoint;
            double distance = vector.Length;
            double angle = vector.GetAngleTo(Vector3d.XAxis, Vector3d.ZAxis);

            // 如果距离太小，不足以绘制Z字形，则只画一条直线
            if (distance < gap * 2)
            {
                return new Point3dCollection(new[] { startPoint, endPoint });
            }

            // 计算Z字形四个点的中心点
            Point3d midPoint = startPoint + vector / 2;

            // 计算Z字形的四个顶点相对于中心点的位置
            Point3d p1 = midPoint + new Vector3d(-gap, gap, 0);
            Point3d p2 = midPoint + new Vector3d(-gap, -gap, 0);
            Point3d p3 = midPoint + new Vector3d(gap, gap, 0);
            Point3d p4 = midPoint + new Vector3d(gap, -gap, 0);

            // 创建变换矩阵，将计算出的Z字形顶点旋转到正确的角度
            Matrix3d matrix = Matrix3d.Rotation(angle, Vector3d.ZAxis, midPoint);
            p1 = p1.TransformBy(matrix);
            p2 = p2.TransformBy(matrix);
            p3 = p3.TransformBy(matrix);
            p4 = p4.TransformBy(matrix);

            // 创建并返回包含所有顶点的集合
            var points = new Point3dCollection
            {
                startPoint,
                p1,
                p2,
                p3,
                p4,
                endPoint
            };

            return points;
        }
    }
}