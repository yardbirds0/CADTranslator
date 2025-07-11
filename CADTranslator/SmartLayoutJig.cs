using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using System;
using System.Collections.Generic;
using System.Text;

namespace CADTranslator
{
    public class SmartLayoutJig : DrawJig
    {
        // 输入参数
        private readonly string _originalText;
        private readonly Point3d _basePoint; // 左上角基点
        private readonly double _textHeight;
        private readonly double _widthFactor;
        private readonly ObjectId _textStyleId;

        // 内部状态
        private Point3d _currentMousePoint; // 跟随鼠标移动的点

        // 输出结果
        public double FinalWidth { get; private set; }
        public string FinalFormattedText { get; private set; }

        public SmartLayoutJig(string text, Point3d basePoint, double height, double widthFactor, ObjectId textStyleId)
        {
            _originalText = text;
            _basePoint = basePoint;
            _currentMousePoint = basePoint; // 初始鼠标点等于基点
            _textHeight = height;
            _widthFactor = widthFactor;
            _textStyleId = textStyleId;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var options = new JigPromptPointOptions("\n请拖动鼠标确定排版宽度，然后点击确认:")
            {
                BasePoint = _basePoint,
                UseBasePoint = true,
                Cursor = CursorType.Crosshair
            };

            var result = prompts.AcquirePoint(options);

            if (result.Status == PromptStatus.OK)
            {
                // 仅当鼠标位置变化时才更新
                if (_currentMousePoint.IsEqualTo(result.Value, new Tolerance(1e-4, 1e-4)))
                {
                    return SamplerStatus.NoChange;
                }
                _currentMousePoint = result.Value;
                return SamplerStatus.OK;
            }
            return SamplerStatus.Cancel;
        }

        // WorldDraw负责绘制实时预览
        protected override bool WorldDraw(WorldDraw draw)
        {
            // 1. 计算当前排版宽度
            double currentWidth = Math.Abs(_currentMousePoint.X - _basePoint.X);
            if (currentWidth < 1e-4) currentWidth = 1.0; // 防止宽度为0

            // 2. 为了预览性能，我们使用一个MText来显示排版效果
            using (var previewMText = new MText())
            {
                previewMText.Location = _basePoint;
                previewMText.TextHeight = _textHeight;
                previewMText.TextStyleId = _textStyleId;
                previewMText.Width = currentWidth; // 设置MText的宽度，让它自动换行

                // 3. 计算悬挂缩进值 (2个字符宽度)
                double indentValue = 0;
                using (var tempDbText = new DBText { TextString = "WW", Height = _textHeight, TextStyleId = _textStyleId, WidthFactor = _widthFactor })
                {
                    indentValue = tempDbText.GeometricExtents.MaxPoint.X - tempDbText.GeometricExtents.MinPoint.X;
                }

                // 4. 应用MText的段落悬挂缩进格式
                string formattingCode = $"\\pi0,{indentValue};";
                previewMText.Contents = formattingCode + _originalText.Replace('\n', ' ');

                // 5. 绘制预览图形
                draw.Geometry.Draw(previewMText);

                // 保存最终结果，供Jig结束后使用
                FinalWidth = currentWidth;
                FinalFormattedText = previewMText.Contents; // 保存带格式代码的文本
            }
            return true;
        }
    }
}