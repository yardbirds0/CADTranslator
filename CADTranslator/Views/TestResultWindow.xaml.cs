using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models.CAD;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using TextBlock = System.Windows.Controls.TextBlock;

namespace CADTranslator.Views
    {
    public partial class TestResultWindow : Window
        {
        private readonly List<LayoutTask> _targets;
        private readonly List<Tuple<Extents3d, string>> _obstaclesForReport;
        private readonly List<NtsGeometry> _preciseObstacles;
        private readonly string _preciseReport;
        private readonly Dictionary<ObjectId, NtsGeometry> _obstacleIdMap;

        private Matrix _transformMatrix;
        private TextBox _obstaclesTextBox;
        private TextBox _preciseObstaclesTextBox;
        private bool _isDrawing = false;

        public TestResultWindow(List<LayoutTask> targets, List<Tuple<Extents3d, string>> obstaclesForReport, List<NtsGeometry> preciseObstacles, string preciseReport, Dictionary<ObjectId, NtsGeometry> obstacleIdMap, (int rounds, double bestScore, double worstScore) summary)
            {
            InitializeComponent();
            _targets = targets;
            _obstaclesForReport = obstaclesForReport;
            _preciseObstacles = preciseObstacles;
            _preciseReport = preciseReport;
            _obstacleIdMap = obstacleIdMap;

            this.Loaded += (s, e) => TestResultWindow_Loaded(s, e, summary); // 将summary传递给Loaded事件
            this.SizeChanged += (s, e) => DrawLayout();
            }

        private void TestResultWindow_Loaded(object sender, RoutedEventArgs e, (int rounds, double bestScore, double worstScore) summary)
            {
            // 填充“战报陈列室”
            var summaryText = new StringBuilder();
            summaryText.AppendLine($"总推演轮次: {summary.rounds} 轮");
            summaryText.AppendLine($"最佳布局评分: {summary.bestScore:F2}");
            summaryText.AppendLine($"最差布局评分: {summary.worstScore:F2}");
            SummaryTextBlock.Text = summaryText.ToString();

            // ... (后续的查找控件和填充其他报告的逻辑，与上次的最终修正版完全相同)
            if (this.FindName("ObstaclesTextBox") is TextBox foundObstaclesTextBox) { _obstaclesTextBox = foundObstaclesTextBox; }
            if (this.FindName("PreciseObstaclesTextBox") is TextBox foundPreciseTextBox) { _preciseObstaclesTextBox = foundPreciseTextBox; }
            ReportListView.ItemsSource = _targets;
            var obstaclesReport = new StringBuilder();
            obstaclesReport.AppendLine($"共分析 {_obstaclesForReport.Count} 个初始障碍物 (基于边界框)：");
            obstaclesReport.AppendLine("========================================");
            _obstaclesForReport.ForEach(obs => obstaclesReport.AppendLine($"--- [类型: {obs.Item2}] Min: {obs.Item1.MinPoint}, Max: {obs.Item1.MaxPoint}"));
            if (_obstaclesTextBox != null) { _obstaclesTextBox.Text = obstaclesReport.ToString(); }
            if (_preciseObstaclesTextBox != null) { _preciseObstaclesTextBox.Text = _preciseReport; }

            DrawLayout();
            if (ReportListView.Items.Count > 0) { ReportListView.SelectedIndex = 0; }
            }

        private void DrawLayout()
            {
            if (_isDrawing) return;
            _isDrawing = true;

            try
                {
                if (PreviewCanvas.ActualWidth == 0 || PreviewCanvas.ActualHeight == 0) return;

                var worldEnvelope = new NetTopologySuite.Geometries.Envelope();
                _preciseObstacles.ForEach(g => worldEnvelope.ExpandToInclude(g.EnvelopeInternal));
                _targets.ForEach(t => {
                    var b = t.Bounds;
                    if (b.MinPoint.X <= b.MaxPoint.X && b.MinPoint.Y <= b.MaxPoint.Y)
                        {
                        worldEnvelope.ExpandToInclude(new NetTopologySuite.Geometries.Coordinate(b.MinPoint.X, b.MinPoint.Y));
                        worldEnvelope.ExpandToInclude(new NetTopologySuite.Geometries.Coordinate(b.MaxPoint.X, b.MaxPoint.Y));
                        }
                });

                CalculateTransform(worldEnvelope);
                PreviewCanvas.Children.Clear();

                // 绘制基础障碍物 (不变)
                foreach (var geometry in _preciseObstacles)
                    {
                    var path = CreateWpfPath(geometry, new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)), Brushes.DarkGray, 0.5);
                    PreviewCanvas.Children.Add(path);
                    }

                // ▼▼▼ 【核心升级】在这里，我们整合原文和标签的显示 ▼▼▼
                foreach (var task in _targets)
                    {
                    // 1. 准备要显示的最终文本
                    string displayText = task.OriginalText;
                    if (task.SemanticType != "独立文本")
                        {
                        displayText += $" ({task.SemanticType})";
                        }

                    // 2. 调用简化的“画笔”，将整合后的文本绘制出来
                    var textBlock = CreateTextBlock(displayText, task.Bounds, Brushes.Blue, 12);
                    PreviewCanvas.Children.Add(textBlock);
                    }
                // ▲▲▲ 【修改结束】 ▲▲▲

                // 绘制计算出的最佳位置 (不变)
                foreach (var task in _targets.Where(t => t.BestPosition.HasValue))
                    {
                    var newBounds = task.GetTranslatedBounds();
                    var rect = CreateRectangle(newBounds, new SolidColorBrush(Color.FromArgb(100, 144, 238, 144)), Brushes.Green, 1.5);
                    PreviewCanvas.Children.Add(rect);
                    }
                // 最后绘制高亮和诊断信息 (不变)
                DrawDiagnosticsAndHighlight();
                }
            finally
                {
                _isDrawing = false;
                }
            }

        private void ReportListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
            {
            if (this.IsLoaded)
                {
                DrawLayout();
                }
            }

        private void DrawDiagnosticsAndHighlight()
            {
            if (ReportListView.SelectedItem is LayoutTask selectedTask)
                {
                // ... (绘制搜索区域和碰撞元凶的逻辑保持不变)
                double searchMargin = selectedTask.Height * 2;
                var searchBounds = new Extents3d(new Point3d(selectedTask.Bounds.MinPoint.X - searchMargin - (selectedTask.Bounds.MaxPoint.X - selectedTask.Bounds.MinPoint.X), selectedTask.Bounds.MinPoint.Y - searchMargin - (selectedTask.Bounds.MaxPoint.Y - selectedTask.Bounds.MinPoint.Y), 0), new Point3d(selectedTask.Bounds.MaxPoint.X + searchMargin, selectedTask.Bounds.MaxPoint.Y + searchMargin, 0));
                var searchRect = CreateRectangle(searchBounds, new SolidColorBrush(Color.FromArgb(20, 100, 100, 100)), Brushes.Transparent, 0);
                Panel.SetZIndex(searchRect, -10);
                PreviewCanvas.Children.Add(searchRect);
                if (selectedTask.CollisionDetails.Any())
                    {
                    var culpritIds = new HashSet<ObjectId>(selectedTask.CollisionDetails.Values);
                    foreach (var culpritId in culpritIds)
                        {
                        if (_obstacleIdMap.TryGetValue(culpritId, out var culpritGeom))
                            {
                            var culpritPath = CreateWpfPath(culpritGeom, new SolidColorBrush(Color.FromArgb(100, 255, 0, 0)), Brushes.Red, 1);
                            Panel.SetZIndex(culpritPath, 20);
                            PreviewCanvas.Children.Add(culpritPath);
                            }
                        }
                    }

                // --- 绘制高亮信息 ---
                // ▼▼▼ 【核心升级 2/3】高亮时，我们用一个更醒目的背景框来代替高亮文字 ▼▼▼
                var originalRect = CreateRectangle(selectedTask.Bounds, new SolidColorBrush(Color.FromArgb(100, 30, 144, 255)), Brushes.DodgerBlue, 2.5);
                Panel.SetZIndex(originalRect, 30);
                PreviewCanvas.Children.Add(originalRect);

                if (selectedTask.BestPosition.HasValue)
                    {
                    var translatedRect = CreateRectangle(selectedTask.GetTranslatedBounds(), new SolidColorBrush(Color.FromArgb(150, 60, 179, 113)), Brushes.SeaGreen, 2.5);
                    Panel.SetZIndex(translatedRect, 30);
                    PreviewCanvas.Children.Add(translatedRect);
                    }
                }
            }

        private TextBlock CreateTextBlock(string text, Extents3d bounds, Brush color, double fontSize)
            {
            var textBlock = new TextBlock
                {
                Text = text,
                Foreground = color,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold
                // 【核心修正】我们移除了 TextTrimming = TextTrimming.CharacterEllipsis 这一行
                };

            // 应用坐标转换
            var p1 = _transformMatrix.Transform(new System.Windows.Point(bounds.MinPoint.X, bounds.MinPoint.Y));
            var p2 = _transformMatrix.Transform(new System.Windows.Point(bounds.MaxPoint.X, bounds.MaxPoint.Y));

            // 将TextBlock放置在边界框的中心
            double left = Math.Min(p1.X, p2.X);
            double top = Math.Min(p1.Y, p2.Y);
            double height = Math.Abs(p2.Y - p1.Y);

            // 【核心修正】我们移除了 textBlock.MaxWidth = width 这一行
            Canvas.SetLeft(textBlock, left);
            Canvas.SetTop(textBlock, top + (height - fontSize) / 2); // 垂直居中

            Panel.SetZIndex(textBlock, 50); // 确保文字总在最顶层
            return textBlock;
            }
        private Path CreateWpfPath(NtsGeometry geometry, Brush fill, Brush stroke, double strokeThickness)
            {
            var path = new Path { Stroke = stroke, StrokeThickness = strokeThickness };
            if (geometry.IsSimple && geometry.IsValid)
                {
                if (geometry is NetTopologySuite.Geometries.Polygon polygon)
                    {
                    path.Fill = fill;
                    var figure = new PathFigure { StartPoint = ConvertPoint(polygon.ExteriorRing.Coordinates[0]), IsClosed = true };
                    for (int i = 1; i < polygon.ExteriorRing.Coordinates.Length; i++) { figure.Segments.Add(new System.Windows.Media.LineSegment(ConvertPoint(polygon.ExteriorRing.Coordinates[i]), true)); }
                    path.Data = new PathGeometry(new[] { figure });
                    }
                else if (geometry is NetTopologySuite.Geometries.LineString lineString)
                    {
                    if (lineString.IsClosed) path.Fill = fill;
                    var figure = new PathFigure { StartPoint = ConvertPoint(lineString.Coordinates[0]), IsClosed = lineString.IsClosed };
                    for (int i = 1; i < lineString.Coordinates.Length; i++) { figure.Segments.Add(new System.Windows.Media.LineSegment(ConvertPoint(lineString.Coordinates[i]), true)); }
                    path.Data = new PathGeometry(new[] { figure });
                    }
                }
            return path;
            }
        private System.Windows.Point ConvertPoint(NetTopologySuite.Geometries.Coordinate coord)
            {
            return _transformMatrix.Transform(new System.Windows.Point(coord.X, coord.Y));
            }
        private void CalculateTransform(NetTopologySuite.Geometries.Envelope worldEnvelope)
            {
            if (worldEnvelope.IsNull || worldEnvelope.Width < 1e-6 || worldEnvelope.Height < 1e-6)
                { _transformMatrix = Matrix.Identity; return; }
            double canvasWidth = PreviewCanvas.ActualWidth * 0.9;
            double canvasHeight = PreviewCanvas.ActualHeight * 0.9;
            var scale = Math.Min(canvasWidth / worldEnvelope.Width, canvasHeight / worldEnvelope.Height);
            _transformMatrix = Matrix.Identity;
            _transformMatrix.Translate(-worldEnvelope.Centre.X, -worldEnvelope.Centre.Y);
            _transformMatrix.Scale(scale, -scale);
            _transformMatrix.Translate(PreviewCanvas.ActualWidth / 2, PreviewCanvas.ActualHeight / 2);
            }
        private Rectangle CreateRectangle(Extents3d bounds, Brush fill, Brush stroke, double strokeThickness)
            {
            var p1 = _transformMatrix.Transform(new System.Windows.Point(bounds.MinPoint.X, bounds.MinPoint.Y));
            var p2 = _transformMatrix.Transform(new System.Windows.Point(bounds.MaxPoint.X, bounds.MaxPoint.Y));
            var rect = new Rectangle { Width = Math.Abs(p2.X - p1.X), Height = Math.Abs(p2.Y - p1.Y), Fill = fill, Stroke = stroke, StrokeThickness = strokeThickness };
            Canvas.SetLeft(rect, Math.Min(p1.X, p2.X));
            Canvas.SetTop(rect, Math.Min(p1.Y, p2.Y));
            return rect;
            }
        }
    }