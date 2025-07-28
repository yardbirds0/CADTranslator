using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace CADTranslator.Tools.Controls
    {
    [TemplatePart(Name = RootTemplateName, Type = typeof(FrameworkElement))]
    public class WaveProgressBar : Control
        {
        private const string RootTemplateName = "Root";

        // 【新增】用于驱动“显示进度”的依赖属性
        public static readonly DependencyProperty DisplayedProgressProperty = DependencyProperty.Register(
            nameof(DisplayedProgress), typeof(double), typeof(WaveProgressBar), new PropertyMetadata(0d));

        public double DisplayedProgress
            {
            get { return (double)GetValue(DisplayedProgressProperty); }
            set { SetValue(DisplayedProgressProperty, value); }
            }

        public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
            nameof(Progress), typeof(double), typeof(WaveProgressBar), new PropertyMetadata(0d, OnProgressChanged));

        public static readonly DependencyProperty WavePhaseProperty = DependencyProperty.Register(
            "WavePhase", typeof(double), typeof(WaveProgressBar), new PropertyMetadata(0d, OnWaveVisualsChanged));

        public static readonly DependencyProperty WaveBaselineYProperty = DependencyProperty.Register(
            "WaveBaselineY", typeof(double), typeof(WaveProgressBar), new PropertyMetadata(100d, OnWaveVisualsChanged));

        private FrameworkElement _root;
        private LineSegment _lineSegment;
        private BezierSegment _bezierSegment;
        private bool _isLoaded;
        private Storyboard _phaseStoryboard;

        public WaveProgressBar() => DefaultStyleKey = typeof(WaveProgressBar);

        public double Progress
            {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
            }

        public override void OnApplyTemplate()
            {
            base.OnApplyTemplate();
            _root = (FrameworkElement)GetTemplateChild(RootTemplateName);

            if (_root != null)
                {
                _lineSegment = _root.FindName("PART_LineSegment") as LineSegment;
                _bezierSegment = _root.FindName("PART_BezierSegment") as BezierSegment;

                _root.Loaded += (s, e) =>
                {
                    _isLoaded = true;
                    InitializeAnimations();
                    UpdateAnimationState(true);
                };
                }
            }

        private void InitializeAnimations()
            {
            _phaseStoryboard = new Storyboard
                {
                RepeatBehavior = RepeatBehavior.Forever
                };
            var phaseAnimation = new DoubleAnimation
                {
                From = 0,
                To = 360,
                Duration = new Duration(TimeSpan.FromSeconds(2.5))
                };
            Storyboard.SetTarget(phaseAnimation, this);
            Storyboard.SetTargetProperty(phaseAnimation, new PropertyPath(WavePhaseProperty));
            _phaseStoryboard.Children.Add(phaseAnimation);
            _phaseStoryboard.Begin();
            }

        private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            {
            var obj = (WaveProgressBar)d;
            if (obj._isLoaded)
                {
                obj.UpdateAnimationState();
                }
            }

        private static void OnWaveVisualsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            {
            var control = (WaveProgressBar)d;
            if (control._lineSegment == null || control._bezierSegment == null) return;

            double phase = (double)control.GetValue(WavePhaseProperty);
            double baseline = (double)control.GetValue(WaveBaselineYProperty);
            double amplitude = 5;

            double rad = phase * Math.PI / 180;

            control._lineSegment.Point = new Point(0, baseline - Math.Sin(rad) * amplitude);
            control._bezierSegment.Point1 = new Point(35, baseline - Math.Sin(rad + Math.PI / 2) * amplitude);
            control._bezierSegment.Point2 = new Point(65, baseline - Math.Sin(rad + Math.PI) * amplitude);
            control._bezierSegment.Point3 = new Point(100, baseline - Math.Sin(rad + 3 * Math.PI / 2) * amplitude);
            }

        private void UpdateAnimationState(bool isInitialSetup = false)
            {
            if (!_isLoaded) return;

            double targetY;
            double targetProgress = Progress * 100;

            if (Progress >= 1)
                {
                targetY = 7;
                targetProgress = 100;
                }
            else if (Progress <= 0)
                {
                targetY = 93;
                targetProgress = 0;
                }
            else
                {
                targetY = 100 * (1 - Progress);
                }

            double fromY = isInitialSetup ? 100 : (double)GetValue(WaveBaselineYProperty);
            double fromProgress = isInitialSetup ? 0 : DisplayedProgress;

            // 【核心修改】创建平滑的、1秒钟的动画
            var baselineAnimation = new DoubleAnimation
                {
                From = fromY,
                To = targetY,
                Duration = new Duration(TimeSpan.FromSeconds(1)),
                // 【核心修改】使用更平缓的CubicEase代替BackEase
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
            this.BeginAnimation(WaveBaselineYProperty, baselineAnimation);

            // 【新增】为显示的进度数字创建平滑动画
            var textAnimation = new DoubleAnimation
                {
                From = fromProgress,
                To = targetProgress,
                Duration = new Duration(TimeSpan.FromSeconds(1)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
            this.BeginAnimation(DisplayedProgressProperty, textAnimation);
            }
        }
    }