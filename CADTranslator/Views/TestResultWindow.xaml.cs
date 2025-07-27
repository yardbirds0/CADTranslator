using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CADTranslator.Models.CAD;
using CADTranslator.Services.CAD;
using CADTranslator.Services.Settings;
using NetTopologySuite.Index.Strtree;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using TextBlock = System.Windows.Controls.TextBlock;
using WinPoint = System.Windows.Point;

namespace CADTranslator.Views
    {
    public partial class TestResultWindow : Window, INotifyPropertyChanged
        {
        #region --- 字段与属性 ---

        // 原始数据
        private readonly Document _doc;
        private readonly List<LayoutTask> _originalTargets;
        private readonly List<Entity> _rawObstacles;
        private readonly List<Tuple<Extents3d, string>> _obstaclesForReport;
        private readonly List<NtsGeometry> _preciseObstacles;
        private readonly string _preciseReport;
        private readonly Dictionary<ObjectId, NtsGeometry> _obstacleIdMap;
        private readonly STRtree<NtsGeometry> _obstacleIndex = new STRtree<NtsGeometry>();
        private readonly Dictionary<ObjectId, Size> _textSizeCache = new Dictionary<ObjectId, Size>();
        private readonly Dictionary<ObjectId, Size> _translatedSizeCache = new Dictionary<ObjectId, Size>();

        // 设置服务
        private readonly SettingsService _settingsService = new SettingsService();
        private AppSettings _settings;

        // UI绑定与状态控制
        private Matrix _transformMatrix;
        private bool _isDrawing = false;
        private bool _isRecalculating = false;
        private LayoutTask _draggedTask = null;
        private Path _highlightedObstaclePath = null;
        private int _numberOfRounds;
        private double _currentSearchRangeFactor;
        private System.Windows.Point _lastMousePosition; // ◄◄◄ 明确使用 System.Windows.Point
        private Matrix _canvasMatrix;
        private bool _isLoaded = false;
        private bool _canvasInitialized = false;

        public ObservableCollection<int> RoundOptions { get; set; }
        public ObservableCollection<double> SearchRangeOptions { get; set; }

        public int NumberOfRounds
            {
            get => _numberOfRounds;
            set
                {
                if (SetField(ref _numberOfRounds, value))
                    {
                    // 只有在窗口完全加载后，用户的修改才触发保存
                    if (_isLoaded)
                        {
                        SaveSettings();
                        }
                    }
                }
            }
        public double CurrentSearchRangeFactor
            {
            get => _currentSearchRangeFactor;
            set
                {
                if (SetField(ref _currentSearchRangeFactor, value))
                    {
                    SaveSettings();
                    // 值变化后，立刻更新所有任务的因子，为下一次重算做准备
                    foreach (var task in _originalTargets)
                        {
                        task.SearchRangeFactor = _currentSearchRangeFactor;
                        }
                    }
                }
            }
        #endregion

        #region --- 构造函数与加载事件 ---

        public TestResultWindow(List<LayoutTask> targets, List<Entity> rawObstacles, List<Tuple<Extents3d, string>> obstaclesForReport, List<NtsGeometry> preciseObstacles, string preciseReport, Dictionary<ObjectId, NtsGeometry> obstacleIdMap, (int rounds, double bestScore, double worstScore) summary)
            {
            InitializeComponent();
            DataContext = this;
            _doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;

            // 步骤 1: 立刻从构造函数参数中初始化 _originalTargets 列表，确保它不再为NULL
            _originalTargets = targets.Select(t => new LayoutTask(t)).ToList();

            // 步骤 2: 现在加载设置，并为WPF属性(NumberOfRounds)赋上正确的值
            _settings = _settingsService.LoadSettings();
            NumberOfRounds = _settings.TestNumberOfRounds;
            CurrentSearchRangeFactor = _settings.TestSearchRangeFactor;

            // 步骤 3: 初始化其余的数据字段
            _rawObstacles = rawObstacles;
            _obstaclesForReport = obstaclesForReport;
            _preciseReport = preciseReport;
            _preciseObstacles = preciseObstacles;
            _obstacleIdMap = obstacleIdMap;

            // 步骤 4: 现在可以安全地对 _originalTargets 进行操作了
            SearchRangeOptions = new ObservableCollection<double> { 5.0, 8.0, 10.0, 15.0, 20.0 };
            if (!SearchRangeOptions.Contains(CurrentSearchRangeFactor))
                {
                SearchRangeOptions.Add(CurrentSearchRangeFactor);
                }
            foreach (var task in _originalTargets) // 这个循环现在是安全的
                {
                task.SearchRangeFactor = CurrentSearchRangeFactor;
                }
            foreach (var obstacle in _preciseObstacles)
                {
                _obstacleIndex.Insert(obstacle.EnvelopeInternal, obstacle);
                }

            // 步骤 5: 剩余的UI初始化
            RoundOptions = new ObservableCollection<int> { 10, 50, 100, 200, 500, 1000 };
            if (NumberOfRounds < RoundsSlider.Minimum)
                {
                NumberOfRounds = (int)RoundsSlider.Minimum;
                }

            this.Loaded += (s, e) => TestResultWindow_Loaded(s, e, summary);
            this.SizeChanged += (s, e) => DrawLayout();
            }

        private void TestResultWindow_Loaded(object sender, RoutedEventArgs e, (int rounds, double bestScore, double worstScore) summary)
            {
            _canvasMatrix = CanvasTransform.Matrix;
            RoundsSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(Slider_DragCompleted));
            RoundsComboBox.LostFocus += RoundsComboBox_LostFocus;
            SearchRangeComboBox.LostFocus += SearchRangeComboBox_LostFocus;
            UpdateSummary(summary);
            RoundsComboBox.Text = this.NumberOfRounds.ToString();

            var obstaclesReport = new StringBuilder();
            obstaclesReport.AppendLine($"共分析 {_obstaclesForReport.Count} 个初始障碍物 (基于边界框)：");
            obstaclesReport.AppendLine("========================================");
            _obstaclesForReport.ForEach(obs => obstaclesReport.AppendLine($"--- [类型: {obs.Item2}] Min: {obs.Item1.MinPoint}, Max: {obs.Item1.MaxPoint}"));
            ObstaclesTextBox.Text = obstaclesReport.ToString();
            PreciseObstaclesTextBox.Text = _preciseReport;

            ReportListView.ItemsSource = _originalTargets;
            RoundsSlider.Value = this.NumberOfRounds;
            RoundsComboBox.Text = this.NumberOfRounds.ToString();
            // 【核心修复】在所有初始化完成后，才允许保存操作
            _isLoaded = true;
            PreCacheAllTextSizes();
            _canvasInitialized = false; // 强制重绘
            DrawLayout();
            //if (ReportListView.Items.Count > 0) { ReportListView.SelectedIndex = 0; }
            }
        #endregion

        #region --- 核心重算逻辑 ---

        private async void RecalculateAndRedraw()
            {
            if (!this.IsLoaded || _isRecalculating) return;

            _canvasInitialized = false; // 强制重绘
            DrawLayout();
            _isRecalculating = false;
            SummaryTextBlock.Text = $"正在使用 {NumberOfRounds} 轮次进行新一轮推演，请稍候...";

            var newSummary = await Task.Run(() =>
            {
                foreach (var task in _originalTargets)
                    {
                    task.BestPosition = null;
                    task.AlgorithmPosition = null;
                    task.CurrentUserPosition = null;
                    task.IsManuallyMoved = false;
                    task.FailureReason = null;
                    task.CollisionDetails.Clear();
                    }

                var calculator = new LayoutCalculator();
                return calculator.CalculateLayouts(_originalTargets, _rawObstacles, NumberOfRounds);
            });

            UpdateSummary(newSummary);
            ReportListView.ItemsSource = null;
            ReportListView.ItemsSource = _originalTargets;
            DrawLayout();

            _isRecalculating = false;
            }

        private void Slider_DragCompleted(object sender, DragCompletedEventArgs e)
            {
            RecalculateAndRedraw();
            }

        private void UpdateSummary((int rounds, double bestScore, double worstScore) summary)
            {
            var summaryText = new StringBuilder();
            summaryText.AppendLine($"总推演轮次: {summary.rounds} 轮");
            summaryText.AppendLine($"最佳布局评分: {summary.bestScore:F2}");
            // 【恢复】显示最差得分
            summaryText.AppendLine($"最差布局评分: {summary.worstScore:F2}");
            SummaryTextBlock.Text = summaryText.ToString();
            }

        #endregion

        #region --- 核心绘图逻辑 ---

        private void DrawLayout()
            {
            if (_isDrawing || PreviewCanvas.ActualWidth == 0) return;
            _isDrawing = true;

            try
                {
                // 只有在画布未初始化时，才执行代价高昂的UI元素创建过程
                if (!_canvasInitialized)
                    {
                    var worldEnvelope = new NetTopologySuite.Geometries.Envelope();
                    _preciseObstacles.ForEach(g => worldEnvelope.ExpandToInclude(g.EnvelopeInternal));
                    _originalTargets.ForEach(t =>
                    {
                        var b = t.Bounds;
                        worldEnvelope.ExpandToInclude(new NetTopologySuite.Geometries.Coordinate(b.MinPoint.X, b.MinPoint.Y));
                        worldEnvelope.ExpandToInclude(new NetTopologySuite.Geometries.Coordinate(b.MaxPoint.X, b.MaxPoint.Y));
                    });

                    CalculateTransform(worldEnvelope);
                    PreviewCanvas.Children.Clear();

                    // 创建所有静态的几何障碍物
                    foreach (var geometry in _preciseObstacles)
                        {
                        var path = CreateWpfPath(geometry, Brushes.LightGray, Brushes.DarkGray, 0.5);
                        path.Opacity = 0.5;
                        PreviewCanvas.Children.Add(path);
                        }

                    // 创建所有原文框
                    foreach (var task in _originalTargets)
                        {
                        var textBlockWrapper = CreateTextBlockWrapper(task, isTranslated: false);
                        PreviewCanvas.Children.Add(textBlockWrapper);
                        }

                    // 创建所有译文框
                    foreach (var task in _originalTargets.Where(t => t.CurrentUserPosition.HasValue))
                        {
                        var thumb = CreateDraggableThumb(task);
                        PreviewCanvas.Children.Add(thumb);
                        }
                    _canvasInitialized = true;
                    }

                // 无论是首次创建还是后续更新，都调用样式方法
                UpdateVisualStyles();
                }
            finally
                {
                _isDrawing = false;
                }
            }
        #endregion

        #region --- 拖动事件处理 ---

        private void Thumb_DragStarted(object sender, DragStartedEventArgs e)
            {
            if (sender is Thumb thumb && thumb.DataContext is LayoutTask task)
                {
                _draggedTask = task;
                ReportListView.SelectedItem = task;
                thumb.CaptureMouse();
                e.Handled = true;
                Panel.SetZIndex(thumb, 100);
                }
            }

        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
            {
            if (_draggedTask == null || sender is not Thumb thumb) return;

            // 【关键】现在拖动逻辑是绝对的，不会与画布平移冲突
            var newLeft = Canvas.GetLeft(thumb) + e.HorizontalChange;
            var newTop = Canvas.GetTop(thumb) + e.VerticalChange;
            Canvas.SetLeft(thumb, newLeft);
            Canvas.SetTop(thumb, newTop);

            var inverseTransform = _transformMatrix;
            inverseTransform.Invert();
            var newWorldTopLeft = inverseTransform.Transform(new Point(newLeft, newTop));

            if (!_translatedSizeCache.TryGetValue(_draggedTask.ObjectId, out var size))
                {
                size = new Size(_draggedTask.Bounds.Width(), _draggedTask.Bounds.Height());
                }

            // Y轴反转，从WPF的左上角坐标计算回CAD的左下角坐标
            _draggedTask.CurrentUserPosition = new Point3d(newWorldTopLeft.X, newWorldTopLeft.Y - size.Height, 0);
            _draggedTask.IsManuallyMoved = true;

            var currentBounds = _draggedTask.GetTranslatedBounds(useUserPosition: true, accurateSize: size);
            var ntsPolygon = Services.CAD.GeometryConverter.ToNtsPolygon(currentBounds);

            // 1. 先检查与静态障碍物的碰撞
            var candidates = _obstacleIndex.Query(ntsPolygon.EnvelopeInternal);
            NtsGeometry collidingObstacle = candidates.FirstOrDefault(obs => obs.Intersects(ntsPolygon));

            // 2. 如果没有撞上静态障碍，再检查是否撞上了其他译文框
            if (collidingObstacle == null)
                {
                foreach (var otherTask in _originalTargets)
                    {
                    // 跳过自己和没有位置的框
                    if (otherTask == _draggedTask || !otherTask.CurrentUserPosition.HasValue) continue;

                    var otherBounds = otherTask.GetTranslatedBounds(useUserPosition: true); // 确保使用用户位置
                    var otherPolygon = Services.CAD.GeometryConverter.ToNtsPolygon(otherBounds);

                    if (ntsPolygon.Intersects(otherPolygon))
                        {
                        // 如果撞上了，就找到代表那个障碍物的几何体（这里用它的原文位置几何体来高亮）
                        _obstacleIdMap.TryGetValue(otherTask.ObjectId, out collidingObstacle);
                        break;
                        }
                    }
                }

            if (_highlightedObstaclePath != null)
                {
                _highlightedObstaclePath.Fill = Brushes.LightGray;
                _highlightedObstaclePath.Stroke = Brushes.DarkGray;
                _highlightedObstaclePath.Opacity = 0.5;
                _highlightedObstaclePath = null;
                }

            var border = (thumb.Template.FindName("ThumbBorder", thumb) as Border);
            if (collidingObstacle != null)
                {
                border.Background = new SolidColorBrush(Color.FromArgb(180, 255, 99, 71));
                border.BorderBrush = Brushes.Red;

                var collidingPath = FindPathByGeometry(collidingObstacle);
                if (collidingPath != null)
                    {
                    collidingPath.Fill = new SolidColorBrush(Color.FromArgb(180, 255, 99, 71));
                    collidingPath.Stroke = Brushes.Red;
                    collidingPath.Opacity = 1.0;
                    _highlightedObstaclePath = collidingPath;
                    }
                }
            else
                {
                border.Background = new SolidColorBrush(Color.FromArgb(180, 147, 112, 219));
                border.BorderBrush = Brushes.DarkViolet;
                }
            }

        private void Thumb_DragCompleted(object sender, DragCompletedEventArgs e)
            {
            if (sender is Thumb thumb)
                {
                Panel.SetZIndex(thumb, 50);
                }
            _draggedTask = null;
            }

        #endregion

        #region --- UI交互与辅助方法 ---

        private void ReportListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
            {
            if (this.IsLoaded)
                {
                UpdateVisualStyles();
                }
            }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
            {
            if (ReportListView.SelectedItem is LayoutTask selectedTask && selectedTask.IsManuallyMoved)
                {
                selectedTask.CurrentUserPosition = selectedTask.AlgorithmPosition;
                selectedTask.IsManuallyMoved = false;
                DrawLayout();
                }
            }

        private void DrawSelectionHighlight()
            {
            if (ReportListView.SelectedItem is not LayoutTask selectedTask) return;

            // 【恢复】绘制搜索框和碰撞物
            var originalBounds = selectedTask.Bounds;
            var center = originalBounds.GetCenter();
            double searchHalfWidth = (originalBounds.Width() / 2.0) * 5;
            double searchHalfHeight = (originalBounds.Height() / 2.0) * selectedTask.SearchRangeFactor;
            var searchBounds = new Extents3d(
                new Point3d(center.X - searchHalfWidth, center.Y - searchHalfHeight, 0),
                new Point3d(center.X + searchHalfWidth, center.Y + searchHalfHeight, 0)
            );
            var searchRect = CreateRectangle(searchBounds, new SolidColorBrush(Color.FromArgb(20, 100, 100, 100)), Brushes.Gray, 1);
            searchRect.StrokeDashArray = new System.Windows.Media.DoubleCollection { 4, 4 };
            Panel.SetZIndex(searchRect, -10);
            PreviewCanvas.Children.Add(searchRect);

            var culpritIds = new HashSet<ObjectId>(selectedTask.CollisionDetails.Values);
            foreach (var culpritId in culpritIds)
                {
                if (_obstacleIdMap.TryGetValue(culpritId, out var culpritGeom))
                    {
                    var culpritPath = FindPathByGeometry(culpritGeom);
                    if (culpritPath != null)
                        {
                        culpritPath.Fill = new SolidColorBrush(Color.FromArgb(100, 255, 0, 0));
                        culpritPath.Stroke = Brushes.Red;
                        culpritPath.Opacity = 1.0;
                        Panel.SetZIndex(culpritPath, 20);
                        }
                    }
                }

            var originalTextBlockWrapper = FindUIElementByTask(selectedTask, isThumb: false);
            if (originalTextBlockWrapper is Viewbox viewbox && viewbox.Child is Border border)
                {
                // 将原文的背景填充为半透明黄色
                border.Background = new SolidColorBrush(Color.FromArgb(128, 255, 255, 0));
                }

            var thumb = FindUIElementByTask(selectedTask, isThumb: true);
            if (thumb is Thumb t && t.Template.FindName("ThumbBorder", t) is Border thumbBorder)
                {
                thumbBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Gold, BlurRadius = 15, ShadowDepth = 0, Opacity = 1 };
                }
            }



        private Path FindPathByGeometry(NtsGeometry geometry)
            {
            return PreviewCanvas.Children.OfType<Path>().FirstOrDefault(p =>
            {
                // 先检查DataContext是不是一个几何体
                if (p.DataContext is NtsGeometry pathGeometry)
                    {
                    // 使用.Equals()进行值比较
                    return pathGeometry.Equals(geometry);
                    }
                return false;
            });
            }

        private FrameworkElement FindUIElementByTask(LayoutTask task, bool isThumb)
            {
            return PreviewCanvas.Children.OfType<FrameworkElement>()
                .FirstOrDefault(fe => fe.DataContext == task && (fe is Thumb) == isThumb);
            }

        private FrameworkElement CreateTextBlockWrapper(LayoutTask task, bool isTranslated)
            {
            var textBlock = new TextBlock
                {
                Text = isTranslated ? task.TranslatedText : task.OriginalText,
                Foreground = Brushes.DimGray,
                Opacity = 0.8
                };

            // 【新增】用一个Border包裹TextBlock，以便我们可以改变背景色
            var border = new Border { Child = textBlock, Background = Brushes.Transparent };

            var viewbox = new Viewbox { Child = border, DataContext = task, IsHitTestVisible = false };

            if (!_textSizeCache.TryGetValue(task.ObjectId, out var size))
                {
                size = new Size(task.Bounds.Width(), task.Bounds.Height());
                }

            var worldBottomLeft = new WinPoint(task.Bounds.MinPoint.X, task.Bounds.MinPoint.Y);
            var worldTopRight = new WinPoint(task.Bounds.MinPoint.X + size.Width, task.Bounds.MinPoint.Y + size.Height);

            var screenP1 = _transformMatrix.Transform(worldBottomLeft);
            var screenP2 = _transformMatrix.Transform(worldTopRight);

            viewbox.Width = Math.Abs(screenP2.X - screenP1.X);
            viewbox.Height = Math.Abs(screenP2.Y - screenP1.Y);

            Canvas.SetLeft(viewbox, Math.Min(screenP1.X, screenP2.X));
            Canvas.SetTop(viewbox, Math.Min(screenP1.Y, screenP2.Y));
            Panel.SetZIndex(viewbox, 20);

            return viewbox;
            }

        #endregion

        #region --- 绘图元素创建 ---

        private Thumb CreateDraggableThumb(LayoutTask task)
            {
            var thumb = new Thumb
                {
                DataContext = task,
                Cursor = Cursors.SizeAll
                };
            var template = new ControlTemplate(typeof(Thumb));
            var border = new FrameworkElementFactory(typeof(Border), "ThumbBorder");
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));

            // --- ▼▼▼ 核心修改：升级动态高亮逻辑 ▼▼▼ ---

            var selectedTask = ReportListView.SelectedItem as LayoutTask;

            // 1. 定义所有颜色状态
            var defaultFill = new SolidColorBrush(Color.FromArgb(180, 144, 238, 144));
            var defaultStroke = Brushes.Green;

            var fadedFill = new SolidColorBrush(Color.FromArgb(50, 200, 200, 200));
            var fadedStroke = Brushes.LightGray;

            var movedFill = new SolidColorBrush(Color.FromArgb(180, 147, 112, 219));
            var movedStroke = Brushes.DarkViolet;

            // 【新增】定义“褪色”的紫色
            var fadedMovedFill = new SolidColorBrush(Color.FromArgb(80, 147, 112, 219));
            var fadedMovedStroke = new SolidColorBrush(Color.FromArgb(255, 190, 170, 220));


            // 2. 根据更复杂的逻辑设置颜色
            if (selectedTask != null) // 如果列表有选中项
                {
                if (task == selectedTask) // 如果当前任务就是被选中的任务
                    {
                    // 无论它是否被移动过，都高亮显示
                    border.SetValue(Border.BackgroundProperty, task.IsManuallyMoved ? movedFill : defaultFill);
                    border.SetValue(Border.BorderBrushProperty, task.IsManuallyMoved ? movedStroke : defaultStroke);
                    }
                else // 如果当前任务不是被选中的任务
                    {
                    // 让它“褪色”
                    border.SetValue(Border.BackgroundProperty, task.IsManuallyMoved ? fadedMovedFill : fadedFill);
                    border.SetValue(Border.BorderBrushProperty, task.IsManuallyMoved ? fadedMovedStroke : fadedStroke);
                    }
                }
            else // 如果列表没有任何选中项
                {
                // 所有任务都恢复到其本身的默认颜色（绿色或紫色）
                border.SetValue(Border.BackgroundProperty, task.IsManuallyMoved ? movedFill : defaultFill);
                border.SetValue(Border.BorderBrushProperty, task.IsManuallyMoved ? movedStroke : defaultStroke);
                }

            // --- ▲▲▲ 核心修改结束 ▲▲▲ ---

            var viewbox = new FrameworkElementFactory(typeof(Viewbox));
            var textBlock = new FrameworkElementFactory(typeof(TextBlock));
            textBlock.SetValue(TextBlock.TextProperty, task.TranslatedText);
            textBlock.SetValue(TextBlock.ForegroundProperty, Brushes.Black);
            textBlock.SetValue(TextBlock.MarginProperty, new Thickness(2));
            viewbox.AppendChild(textBlock);
            border.AppendChild(viewbox);

            template.VisualTree = border;
            thumb.Template = template;

            thumb.DragStarted += Thumb_DragStarted;
            thumb.DragDelta += Thumb_DragDelta;
            thumb.DragCompleted += Thumb_DragCompleted;

            if (!_translatedSizeCache.TryGetValue(task.ObjectId, out var size))
                {
                size = new Size(task.Bounds.Width(), task.Bounds.Height());
                }

            var bounds = task.GetTranslatedBounds(useUserPosition: true, accurateSize: size);
            var p1 = _transformMatrix.Transform(new WinPoint(bounds.MinPoint.X, bounds.MinPoint.Y));
            var p2 = _transformMatrix.Transform(new WinPoint(bounds.MaxPoint.X, bounds.MaxPoint.Y));
            thumb.Width = Math.Abs(p2.X - p1.X);
            thumb.Height = Math.Abs(p2.Y - p1.Y);

            var positionInCanvas = _transformMatrix.Transform(new WinPoint(bounds.MinPoint.X, bounds.MaxPoint.Y));
            Canvas.SetLeft(thumb, positionInCanvas.X);
            Canvas.SetTop(thumb, positionInCanvas.Y);
            Panel.SetZIndex(thumb, 50);

            return thumb;
            }

        private Path CreateWpfPath(NtsGeometry geometry, Brush fill, Brush stroke, double strokeThickness)
            {
            var path = new Path
                {
                Stroke = stroke,
                StrokeThickness = strokeThickness,
                Fill = fill,
                DataContext = geometry
                };
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

        private Rectangle CreateRectangle(Extents3d bounds, Brush fill, Brush stroke, double strokeThickness)
            {
            var p1 = _transformMatrix.Transform(new Point(bounds.MinPoint.X, bounds.MinPoint.Y));
            var p2 = _transformMatrix.Transform(new Point(bounds.MaxPoint.X, bounds.MaxPoint.Y));
            var rect = new Rectangle { Width = Math.Abs(p2.X - p1.X), Height = Math.Abs(p2.Y - p1.Y), Fill = fill, Stroke = stroke, StrokeThickness = strokeThickness };
            Canvas.SetLeft(rect, Math.Min(p1.X, p2.X));
            Canvas.SetTop(rect, Math.Min(p1.Y, p2.Y));
            return rect;
            }

        private Point ConvertPoint(NetTopologySuite.Geometries.Coordinate coord)
            {
            return _transformMatrix.Transform(new Point(coord.X, coord.Y));
            }

        private void CalculateTransform(NetTopologySuite.Geometries.Envelope worldEnvelope)
            {
            if (worldEnvelope.IsNull || worldEnvelope.Width < 1e-6 || worldEnvelope.Height < 1e-6)
                { _transformMatrix = Matrix.Identity; return; }
            double canvasWidth = PreviewCanvas.ActualWidth * 0.9;
            double canvasHeight = PreviewCanvas.ActualHeight * 0.9;
            var scale = Math.Min(canvasWidth / worldEnvelope.Width, canvasHeight / worldEnvelope.Height);

            var baseTransform = Matrix.Identity;
            baseTransform.Translate(-worldEnvelope.Centre.X, -worldEnvelope.Centre.Y);
            baseTransform.Scale(scale, -scale);
            baseTransform.Translate(PreviewCanvas.ActualWidth / 2, PreviewCanvas.ActualHeight / 2);

            _transformMatrix = baseTransform;
            _transformMatrix.Append(_canvasMatrix);
            }
        #endregion

        #region --- 画布平移与缩放事件 ---

        private void PreviewCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
            {
            var scaleFactor = e.Delta > 0 ? 1.1 : 1 / 1.1;
            var mousePosition = e.GetPosition(sender as IInputElement);

            _canvasMatrix.ScaleAt(scaleFactor, scaleFactor, mousePosition.X, mousePosition.Y);
            CanvasTransform.Matrix = _canvasMatrix;

            DrawLayout();
            }

        private void CanvasBorder_MouseDown(object sender, MouseButtonEventArgs e)
            {
            // 判断是哪个鼠标按键触发了事件
            if (e.ChangedButton == MouseButton.Middle && e.ButtonState == MouseButtonState.Pressed)
                {
                // 如果是中键按下，执行画布平移的准备工作
                _lastMousePosition = e.GetPosition(sender as IInputElement);
                (sender as UIElement)?.CaptureMouse();
                PreviewCanvas.Cursor = Cursors.ScrollAll;
                }
            else if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
                {
                // 如果是左键按下，执行取消选择的逻辑
                if (ReportListView.SelectedItem != null)
                    {
                    ReportListView.SelectedItem = null;
                    }
                }
            }

        private void CanvasBorder_MouseUp(object sender, MouseButtonEventArgs e)
            {
            // 判断是哪个鼠标按键被松开
            if (e.ChangedButton == MouseButton.Middle)
                {
                // 如果是中键松开，则停止平移
                (sender as UIElement)?.ReleaseMouseCapture();
                PreviewCanvas.Cursor = Cursors.Arrow;
                }
            }

        private void PreviewCanvas_MouseMove(object sender, MouseEventArgs e)
            {
            // 检查中键是否处于按住状态
            if (e.MiddleButton == MouseButtonState.Pressed && (sender as UIElement).IsMouseCaptured)
                {
                var currentMousePosition = e.GetPosition(sender as IInputElement);
                var delta = Point.Subtract(currentMousePosition, _lastMousePosition);

                _canvasMatrix.Translate(delta.X, delta.Y);
                CanvasTransform.Matrix = _canvasMatrix;

                _lastMousePosition = currentMousePosition;

                DrawLayout();
                }
            }

        private void PreviewCanvas_MouseMiddleButtonDown(object sender, MouseButtonEventArgs e)
            {
            if (e.ButtonState == MouseButtonState.Pressed)
                {
                _lastMousePosition = e.GetPosition(sender as IInputElement);
                (sender as UIElement)?.CaptureMouse();
                PreviewCanvas.Cursor = Cursors.ScrollAll;
                }
            }

        private void PreviewCanvas_MouseMiddleButtonUp(object sender, MouseButtonEventArgs e)
            {
            (sender as UIElement)?.ReleaseMouseCapture();
            PreviewCanvas.Cursor = Cursors.Arrow;
            }

        #endregion

        #region --- "内存CAD渲染" 核心技术 ---
        private void PreCacheAllTextSizes()
            {
            _textSizeCache.Clear();
            _translatedSizeCache.Clear();
            using (var tr = _doc.TransactionManager.StartTransaction())
                {
                foreach (var task in _originalTargets)
                    {
                    _textSizeCache[task.ObjectId] = GetAccurateTextSize(task.OriginalText, task, tr);

                    // ▼▼▼ 请修改下面这一行，移除 "[译] " 前缀 ▼▼▼
                    string translatedTextForSizing = task.OriginalText; // 使用原文作为译文尺寸的精确代理
                                                                        // ▲▲▲ 修改结束 ▲▲▲

                    _translatedSizeCache[task.ObjectId] = GetAccurateTextSize(translatedTextForSizing, task, tr);
                    }
                tr.Commit();
                }
            }

        private Size GetAccurateTextSize(string text, LayoutTask templateTask, Transaction tr)
            {
            if (string.IsNullOrEmpty(text))
                {
                return new Size(0, 0);
                }

            try
                {
                using (var tempText = new DBText())
                    {
                    tempText.TextString = text;
                    tempText.Height = templateTask.Height;
                    tempText.WidthFactor = templateTask.WidthFactor;
                    tempText.TextStyleId = templateTask.TextStyleId;
                    tempText.Oblique = templateTask.Oblique;

                    tempText.HorizontalMode = TextHorizontalMode.TextLeft;
                    tempText.VerticalMode = TextVerticalMode.TextBase;
                    tempText.Position = Point3d.Origin;

                    var extents = tempText.GeometricExtents;
                    if (extents != null)
                        {
                        return new Size(extents.MaxPoint.X - extents.MinPoint.X, extents.MaxPoint.Y - extents.MinPoint.Y);
                        }
                    }
                }
            catch (Exception)
                {
                return new Size(templateTask.Bounds.Width(), templateTask.Bounds.Height());
                }

            return new Size(templateTask.Bounds.Width(), templateTask.Bounds.Height());
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
            _settings.TestSearchRangeFactor = this.CurrentSearchRangeFactor; // ◄◄◄ 【新增】保存搜索范围
            _settingsService.SaveSettings(_settings);
            }

        private void RoundsComboBox_LostFocus(object sender, RoutedEventArgs e)
            {
            if (sender is ComboBox comboBox)
                {
                if (int.TryParse(comboBox.Text, out int value))
                    {
                    // 确保输入值在有效范围内
                    int clampedValue = (int)Math.Max(RoundsSlider.Minimum, Math.Min(RoundsSlider.Maximum, value));

                    // 只有当修正后的值与当前值不同时，才进行更新并保存
                    if (this.NumberOfRounds != clampedValue)
                        {
                        this.NumberOfRounds = clampedValue;
                        }

                    // 【核心修正】无论值是否变化，都强制UI显示与后台数据一致
                    comboBox.Text = this.NumberOfRounds.ToString();
                    }
                else
                    {
                    // 如果输入了无效内容（如字母），则将文本重置为当前有效值
                    comboBox.Text = this.NumberOfRounds.ToString();
                    }
                }
            }
        private void SearchRangeComboBox_LostFocus(object sender, RoutedEventArgs e)
            {
            if (sender is ComboBox comboBox)
                {
                if (double.TryParse(comboBox.Text, out double value) && value > 0)
                    {
                    if (this.CurrentSearchRangeFactor != value)
                        {
                        this.CurrentSearchRangeFactor = value;
                        }
                    }
                comboBox.Text = this.CurrentSearchRangeFactor.ToString("F1");
                }
            }
        private void UpdateVisualStyles()
            {
            var selectedTask = ReportListView.SelectedItem as LayoutTask;

            // 遍历画布上的所有子元素
            foreach (var element in PreviewCanvas.Children.OfType<FrameworkElement>())
                {
                var task = element.DataContext as LayoutTask;
                if (task == null) continue;

                if (element is Viewbox viewbox && viewbox.Child is Border originalTextBorder) // 处理原文框
                    {
                    // 如果当前任务是选中任务，则背景变黄，否则变透明
                    originalTextBorder.Background = (task == selectedTask)
                        ? new SolidColorBrush(Color.FromArgb(128, 255, 255, 0))
                        : Brushes.Transparent;
                    }
                else if (element is Thumb thumb && thumb.Template.FindName("ThumbBorder", thumb) is Border thumbBorder) // 处理译文框
                    {
                    // --- 这里是您之前版本中已经非常完善的颜色逻辑 ---
                    var defaultFill = new SolidColorBrush(Color.FromArgb(180, 144, 238, 144));
                    var defaultStroke = Brushes.Green;
                    var fadedFill = new SolidColorBrush(Color.FromArgb(50, 200, 200, 200));
                    var fadedStroke = Brushes.LightGray;
                    var movedFill = new SolidColorBrush(Color.FromArgb(180, 147, 112, 219));
                    var movedStroke = Brushes.DarkViolet;
                    var fadedMovedFill = new SolidColorBrush(Color.FromArgb(80, 147, 112, 219));
                    var fadedMovedStroke = new SolidColorBrush(Color.FromArgb(255, 190, 170, 220));

                    if (selectedTask != null)
                        {
                        if (task == selectedTask)
                            {
                            thumbBorder.Background = task.IsManuallyMoved ? movedFill : defaultFill;
                            thumbBorder.BorderBrush = task.IsManuallyMoved ? movedStroke : defaultStroke;
                            thumbBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Gold, BlurRadius = 15, ShadowDepth = 0, Opacity = 1 };
                            }
                        else
                            {
                            thumbBorder.Background = task.IsManuallyMoved ? fadedMovedFill : fadedFill;
                            thumbBorder.BorderBrush = task.IsManuallyMoved ? fadedMovedStroke : fadedStroke;
                            thumbBorder.Effect = null;
                            }
                        }
                    else
                        {
                        thumbBorder.Background = task.IsManuallyMoved ? movedFill : defaultFill;
                        thumbBorder.BorderBrush = task.IsManuallyMoved ? movedStroke : defaultStroke;
                        thumbBorder.Effect = null;
                        }
                    }
                }
            }
        #endregion

        private void RecalculateButton_Click(object sender, RoutedEventArgs e)
            {
            RecalculateAndRedraw();
            }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
            {
            // 1. 准备要写入CAD的数据
            var tasksToApply = _originalTargets;

            // 2. 将数据传递给我们的静态桥接服务
            CadBridgeService.LayoutTasksToApply = tasksToApply;

            // 3. 向AutoCAD发送执行命令
            CadBridgeService.SendCommandToAutoCAD("TEST_APPLY\n");

            // 4. 关闭预览窗口
            this.Close();
            }
        }

    public static class LayoutTaskExtensionsForView
        {
        public static Extents3d GetTranslatedBounds(this LayoutTask task, bool useUserPosition = false, Size? accurateSize = null)
            {
            Point3d? positionToUse = useUserPosition ? task.CurrentUserPosition : task.AlgorithmPosition;

            if (!positionToUse.HasValue) return task.Bounds;

            double width = accurateSize?.Width ?? task.Bounds.Width();
            double height = accurateSize?.Height ?? task.Bounds.Height();

            // 【修正】Y轴反转，从左下角坐标计算右上角
            return new Extents3d(
                positionToUse.Value,
                new Point3d(positionToUse.Value.X + width, positionToUse.Value.Y + height, 0)
            );
            }
        }
    }