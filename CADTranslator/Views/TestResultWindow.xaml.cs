using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models.CAD;
using CADTranslator.Services.CAD;
using CADTranslator.Services.Settings; // 【新增】引入设置服务
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks; // 【新增】引入Task
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives; // 【新增】引入Primitives以使用Thumb
using System.Windows.Media;
using System.Windows.Shapes;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using TextBlock = System.Windows.Controls.TextBlock;

namespace CADTranslator.Views
    {
    public partial class TestResultWindow : Window, INotifyPropertyChanged
        {
        #region --- 字段与属性 ---

        // 用于存储原始数据
        private readonly List<LayoutTask> _originalTargets;
        private readonly List<Entity> _rawObstacles;
        private readonly List<Tuple<Extents3d, string>> _obstaclesForReport;
        private readonly List<NtsGeometry> _preciseObstacles;
        private readonly string _preciseReport;
        private readonly Dictionary<ObjectId, NtsGeometry> _obstacleIdMap;

        // 【新增】用于设置持久化
        private readonly SettingsService _settingsService = new SettingsService();
        private AppSettings _settings;

        // 用于UI绑定和状态控制
        private Matrix _transformMatrix;
        private bool _isDrawing = false;
        private bool _isRecalculating = false;
        private int _numberOfRounds;

        public ObservableCollection<int> RoundOptions { get; set; }

        public int NumberOfRounds
            {
            get => _numberOfRounds;
            set
                {
                // 只更新值，不在此处触发重算，以避免滑动时卡顿
                if (SetField(ref _numberOfRounds, value))
                    {
                    // 值变化后，保存设置
                    SaveSettings();
                    }
                }
            }

        #endregion

        #region --- 构造函数与加载事件 ---

        public TestResultWindow(List<LayoutTask> targets, List<Entity> rawObstacles, List<Tuple<Extents3d, string>> obstaclesForReport, List<NtsGeometry> preciseObstacles, string preciseReport, Dictionary<ObjectId, NtsGeometry> obstacleIdMap, (int rounds, double bestScore, double worstScore) summary)
            {
            InitializeComponent();
            DataContext = this;

            // 初始化数据
            _originalTargets = targets.Select(t => new LayoutTask(t)).ToList();
            _rawObstacles = rawObstacles;
            _obstaclesForReport = obstaclesForReport;
            _preciseObstacles = preciseObstacles;
            _preciseReport = preciseReport;
            _obstacleIdMap = obstacleIdMap;
            _settings = _settingsService.LoadSettings();

            // 初始化UI选项
            RoundOptions = new ObservableCollection<int> { 10, 50, 100, 200, 500, 1000 };
            NumberOfRounds = _settings.TestNumberOfRounds; // 从设置加载轮次

            this.Loaded += (s, e) => TestResultWindow_Loaded(s, e, summary);
            this.SizeChanged += (s, e) => DrawLayout();
            }

        private void TestResultWindow_Loaded(object sender, RoutedEventArgs e, (int rounds, double bestScore, double worstScore) summary)
            {
            // 【核心修改】为滑块添加拖动完成事件
            RoundsSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(Slider_DragCompleted));

            UpdateSummary(summary);

            var obstaclesReport = new StringBuilder();
            obstaclesReport.AppendLine($"共分析 {_obstaclesForReport.Count} 个初始障碍物 (基于边界框)：");
            obstaclesReport.AppendLine("========================================");
            _obstaclesForReport.ForEach(obs => obstaclesReport.AppendLine($"--- [类型: {obs.Item2}] Min: {obs.Item1.MinPoint}, Max: {obs.Item1.MaxPoint}"));
            ObstaclesTextBox.Text = obstaclesReport.ToString();
            PreciseObstaclesTextBox.Text = _preciseReport;

            ReportListView.ItemsSource = _originalTargets;

            DrawLayout();
            if (ReportListView.Items.Count > 0) { ReportListView.SelectedIndex = 0; }
            }

        #endregion

        #region --- 核心重算与重绘逻辑 ---

        private async void RecalculateAndRedraw()
            {
            if (!this.IsLoaded || _isRecalculating) return;

            _isRecalculating = true;
            SummaryTextBlock.Text = $"正在使用 {NumberOfRounds} 轮次进行新一轮推演，请稍候...";

            // 使用 Task.Run 在后台线程执行计算，避免UI卡死
            var newSummary = await Task.Run(() =>
            {
                foreach (var task in _originalTargets)
                    {
                    task.BestPosition = null;
                    task.FailureReason = null;
                    task.CollisionDetails.Clear();
                    }

                var calculator = new LayoutCalculator();
                return calculator.CalculateLayouts(_originalTargets, _rawObstacles, NumberOfRounds);
            });

            // 回到UI线程更新界面
            UpdateSummary(newSummary);
            ReportListView.ItemsSource = null;
            ReportListView.ItemsSource = _originalTargets;
            DrawLayout();

            _isRecalculating = false;
            }

        // 【新增】滑块拖动完成的事件处理器
        private void Slider_DragCompleted(object sender, DragCompletedEventArgs e)
            {
            RecalculateAndRedraw();
            }

        private void UpdateSummary((int rounds, double bestScore, double worstScore) summary)
            {
            var summaryText = new StringBuilder();
            summaryText.AppendLine($"总推演轮次: {summary.rounds} 轮");
            summaryText.AppendLine($"最佳布局评分: {summary.bestScore:F2}");
            summaryText.AppendLine($"最差布局评分: {summary.worstScore:F2}");
            SummaryTextBlock.Text = summaryText.ToString();
            }

        private void DrawLayout()
            {
            // ... (此方法内容与您之前的最终版本完全相同，无需修改)
            if (_isDrawing) return;
            _isDrawing = true;

            try
                {
                if (PreviewCanvas.ActualWidth == 0 || PreviewCanvas.ActualHeight == 0) return;

                var worldEnvelope = new NetTopologySuite.Geometries.Envelope();
                _preciseObstacles.ForEach(g => worldEnvelope.ExpandToInclude(g.EnvelopeInternal));
                _originalTargets.ForEach(t => {
                    var b = t.Bounds;
                    if (b.MinPoint.X <= b.MaxPoint.X && b.MinPoint.Y <= b.MaxPoint.Y)
                        {
                        worldEnvelope.ExpandToInclude(new NetTopologySuite.Geometries.Coordinate(b.MinPoint.X, b.MinPoint.Y));
                        worldEnvelope.ExpandToInclude(new NetTopologySuite.Geometries.Coordinate(b.MaxPoint.X, b.MaxPoint.Y));
                        }
                });

                CalculateTransform(worldEnvelope);
                PreviewCanvas.Children.Clear();

                foreach (var geometry in _preciseObstacles)
                    {
                    var path = CreateWpfPath(geometry, new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)), Brushes.DarkGray, 0.5);
                    PreviewCanvas.Children.Add(path);
                    }

                foreach (var task in _originalTargets)
                    {
                    string displayText = task.OriginalText;
                    if (task.SemanticType != "独立文本")
                        {
                        displayText += $" ({task.SemanticType})";
                        }
                    var textBlock = CreateTextBlock(displayText, task.Bounds, Brushes.Blue, 12);
                    PreviewCanvas.Children.Add(textBlock);
                    }

                foreach (var task in _originalTargets.Where(t => t.BestPosition.HasValue))
                    {
                    var newBounds = task.GetTranslatedBounds();
                    var rect = CreateRectangle(newBounds, new SolidColorBrush(Color.FromArgb(100, 144, 238, 144)), Brushes.Green, 1.5);
                    PreviewCanvas.Children.Add(rect);
                    }
                DrawDiagnosticsAndHighlight();
                }
            finally
                {
                _isDrawing = false;
                }
            }

        #endregion

        #region --- 绘图与辅助方法 (无需修改) ---
        // ... (从这里开始的所有绘图辅助方法，都与您之前的最终版本完全相同，无需修改)
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
                // 【核心修正】使用与计算核心完全一致的逻辑来定义搜索框范围
                var originalBounds = selectedTask.Bounds;
                var width = originalBounds.MaxPoint.X - originalBounds.MinPoint.X;
                var height = originalBounds.MaxPoint.Y - originalBounds.MinPoint.Y;
                var center = originalBounds.GetCenter();

                double searchHalfWidth = (width / 2.0) * 5;
                double searchHalfHeight = (height / 2.0) * 8;

                var searchAreaMin = new Point3d(center.X - searchHalfWidth, center.Y - searchHalfHeight, 0);
                var searchAreaMax = new Point3d(center.X + searchHalfWidth, center.Y + searchHalfHeight, 0);

                var searchBounds = new Extents3d(searchAreaMin, searchAreaMax);
                // --- 修正结束 ---

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

                // --- 绘制高亮信息 (这部分不变) ---
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
                };
            var p1 = _transformMatrix.Transform(new System.Windows.Point(bounds.MinPoint.X, bounds.MinPoint.Y));
            var p2 = _transformMatrix.Transform(new System.Windows.Point(bounds.MaxPoint.X, bounds.MaxPoint.Y));
            double left = Math.Min(p1.X, p2.X);
            double top = Math.Min(p1.Y, p2.Y);
            double height = Math.Abs(p2.Y - p1.Y);
            Canvas.SetLeft(textBlock, left);
            Canvas.SetTop(textBlock, top + (height - fontSize) / 2);
            Panel.SetZIndex(textBlock, 50);
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

        #endregion

        #region --- INotifyPropertyChanged & Settings ---

        public event PropertyChangedEventHandler PropertyChanged;
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
            {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
            }
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }

        private void SaveSettings()
            {
            _settings.TestNumberOfRounds = this.NumberOfRounds;
            _settingsService.SaveSettings(_settings);
            }

        #endregion
        }
    }