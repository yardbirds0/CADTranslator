// 文件路径: CADTranslator/Tools/Behaviors/EllipseProgressBehavior.cs
// 【请用此代码完整替换】

using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation; // ◄◄◄ 【新增】引入动画命名空间
using System.Windows.Shapes;
using Microsoft.Xaml.Behaviors;

namespace CADTranslator.Tools.Behaviors
    {
    public class EllipseProgressBehavior : Behavior<Ellipse>
        {
        #region --- 依赖属性 (Dependency Properties) ---

        // 这是我们暴露给外部的“目标进度”属性
        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register("Progress", typeof(double), typeof(EllipseProgressBehavior),
                new PropertyMetadata(0d, OnProgressChanged));

        // 这是在内部实际驱动动画的属性
        private static readonly DependencyProperty CurrentProgressProperty =
            DependencyProperty.Register("CurrentProgress", typeof(double), typeof(EllipseProgressBehavior),
                new PropertyMetadata(0d, OnCurrentProgressChanged));

        // 其他属性保持不变
        public static readonly DependencyProperty EndAngleProperty =
            DependencyProperty.Register(nameof(EndAngle), typeof(double), typeof(EllipseProgressBehavior), new PropertyMetadata(default(double), OnAngleChanged));
        public static readonly DependencyProperty StartAngleProperty =
            DependencyProperty.Register(nameof(StartAngle), typeof(double), typeof(EllipseProgressBehavior), new PropertyMetadata(default(double), OnAngleChanged));

        // 【新增】允许自定义动画时长的属性
        public static readonly DependencyProperty AnimationDurationProperty =
            DependencyProperty.Register("AnimationDuration", typeof(Duration), typeof(EllipseProgressBehavior), new PropertyMetadata(new Duration(TimeSpan.FromMilliseconds(300))));

        #endregion

        #region --- .NET 属性包装器 ---

        public double Progress
            {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
            }

        private double CurrentProgress
            {
            get => (double)GetValue(CurrentProgressProperty);
            set => SetValue(CurrentProgressProperty, value);
            }

        public Duration AnimationDuration
            {
            get { return (Duration)GetValue(AnimationDurationProperty); }
            set { SetValue(AnimationDurationProperty, value); }
            }

        public double EndAngle
            {
            get => (double)GetValue(EndAngleProperty);
            set => SetValue(EndAngleProperty, value);
            }

        public double StartAngle
            {
            get => (double)GetValue(StartAngleProperty);
            set => SetValue(StartAngleProperty, value);
            }

        #endregion

        #region --- 私有字段 ---

        private double _normalizedMaxAngle;
        private double _normalizedMinAngle;

        #endregion

        #region --- 核心方法 ---

        protected override void OnAttached()
            {
            base.OnAttached();
            UpdateAngle();
            UpdateStrokeDashArray();
            AssociatedObject.SizeChanged += (s, e) => { UpdateStrokeDashArray(); };
            }

        // 当“目标进度”(Progress)改变时，我们不再直接更新UI，而是触发一个动画
        private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            {
            var behavior = (EllipseProgressBehavior)d;
            var newProgress = (double)e.NewValue;

            // 创建一个从“当前进度”到“目标进度”的双精度浮点数动画
            var animation = new DoubleAnimation
                {
                To = newProgress,
                Duration = behavior.AnimationDuration,
                // 添加一个缓动函数，让动画看起来更自然
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

            // 开始动画，目标是我们的内部属性 CurrentProgress
            behavior.BeginAnimation(CurrentProgressProperty, animation);
            }

        // 只有当内部的“当前进度”(CurrentProgress)在动画过程中被改变时，我们才真正更新UI
        private static void OnCurrentProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            {
            ((EllipseProgressBehavior)d).UpdateStrokeDashArray();
            }

        private void UpdateStrokeDashArray()
            {
            if (AssociatedObject == null || AssociatedObject.StrokeThickness == 0) return;

            var totalLength = GetTotalLength();
            if (totalLength == 0) return;

            totalLength /= AssociatedObject.StrokeThickness;

            // 使用 CurrentProgress 来计算长度
            var progressLength = CurrentProgress * totalLength / 100;

            AssociatedObject.StrokeDashArray = new DoubleCollection { progressLength, double.MaxValue };
            }

        #endregion

        #region --- 角度计算与辅助方法 (保持不变) ---

        private static void OnAngleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            {
            var behavior = (EllipseProgressBehavior)d;
            behavior.UpdateAngle();
            behavior.UpdateStrokeDashArray();
            }

        protected virtual double GetTotalLength()
            {
            if (AssociatedObject == null || AssociatedObject.ActualHeight == 0) return 0;
            return (AssociatedObject.ActualHeight - AssociatedObject.StrokeThickness) * Math.PI * (_normalizedMaxAngle - _normalizedMinAngle) / 360;
            }

        private static double Mod(double number, double divider)
            {
            var result = number % divider;
            result = result < 0 ? result + divider : result;
            return result;
            }

        private void UpdateAngle()
            {
            UpdateNormalizedAngles();
            if (AssociatedObject == null) return;

            AssociatedObject.RenderTransformOrigin = new Point(0.5, 0.5);
            if (AssociatedObject.RenderTransform is RotateTransform transform)
                {
                transform.Angle = _normalizedMinAngle - 90;
                }
            else
                {
                AssociatedObject.RenderTransform = new RotateTransform { Angle = _normalizedMinAngle - 90 };
                }
            }

        private void UpdateNormalizedAngles()
            {
            var result = Mod(StartAngle, 360);
            if (result >= 180) result -= 360;
            _normalizedMinAngle = result;

            result = Mod(EndAngle, 360);
            if (result < 180) result += 360;
            if (result > _normalizedMinAngle + 360) result -= 360;
            _normalizedMaxAngle = result;
            }

        #endregion
        }
    }