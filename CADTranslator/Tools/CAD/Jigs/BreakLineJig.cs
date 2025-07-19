using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Tools;

namespace CADTranslator.Tools.CAD.Jigs
    {
    public class BreakLineJig : EntityJig
    {
        private Point3d _startPoint;
        private Point3d _currentEndPoint;

        // ▼▼▼ 修正2：添加一个公开的属性，以安全地从外部获取Jig中的Polyline实体 ▼▼▼
        public Polyline Polyline => this.Entity as Polyline;
        // ▲▲▲ 修正结束 ▲▲▲

        public BreakLineJig(Point3d startPoint) : base(new Polyline())
        {
            _startPoint = startPoint;
            _currentEndPoint = startPoint;

            // 为多段线设置一些默认属性，确保它不是一个空的、无效的对象
            var pline = this.Entity as Polyline;
            pline.SetDatabaseDefaults();
            pline.AddVertexAt(0, new Point2d(startPoint.X, startPoint.Y), 0, 0, 0);
            pline.AddVertexAt(1, new Point2d(startPoint.X, startPoint.Y), 0, 0, 0);
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var options = new JigPromptPointOptions("\n请指定截断线终点:")
            {
                BasePoint = _startPoint,
                UseBasePoint = true,
                Cursor = CursorType.Crosshair
            };

            var result = prompts.AcquirePoint(options);

            if (result.Status == PromptStatus.OK)
            {
                if (_currentEndPoint.IsEqualTo(result.Value, new Tolerance(1e-6, 1e-6)))
                {
                    return SamplerStatus.NoChange;
                }
                _currentEndPoint = result.Value;
                return SamplerStatus.OK;
            }

            return SamplerStatus.Cancel;
        }

        protected override bool Update()
        {
            var pline = this.Polyline; // 使用我们公开的属性

            using (var tempPline = new Polyline())
            {
                Point3dCollection newVertices = GeometryHelper.CreateVertices(_startPoint, _currentEndPoint);
                for (int i = 0; i < newVertices.Count; i++)
                {
                    tempPline.AddVertexAt(i, new Point2d(newVertices[i].X, newVertices[i].Y), 0, 0, 0);
                }

                // ▼▼▼ 修正1：使用正确的 CopyFrom 方法，而不是不存在的 SetFrom ▼▼▼
                pline.CopyFrom(tempPline);
                // ▲▲▲ 修正结束 ▲▲▲
            }

            return true;
        }
    }
}