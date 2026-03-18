using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace NetProve.Controls
{
    /// <summary>
    /// A circular arc gauge control that displays a percentage value 0–100.
    /// Lightweight pure-WPF implementation with smooth animation.
    /// </summary>
    public sealed class CircularGauge : Control
    {
        static CircularGauge()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(CircularGauge),
                new FrameworkPropertyMetadata(typeof(CircularGauge)));

            // Default Foreground to white so text is visible on dark backgrounds
            ForegroundProperty.OverrideMetadata(typeof(CircularGauge),
                new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
        }

        // ── Dependency Properties ─────────────────────────────────────────────
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(CircularGauge),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.None,
                    OnValueChanged));

        public static readonly DependencyProperty TrackColorProperty =
            DependencyProperty.Register(nameof(TrackColor), typeof(Brush), typeof(CircularGauge),
                new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x2D, 0x30, 0x48)),
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ArcColorProperty =
            DependencyProperty.Register(nameof(ArcColor), typeof(Brush), typeof(CircularGauge),
                new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeWidthProperty =
            DependencyProperty.Register(nameof(StrokeWidth), typeof(double), typeof(CircularGauge),
                new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.Register(nameof(MaxValue), typeof(double), typeof(CircularGauge),
                new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(CircularGauge),
                new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty UnitProperty =
            DependencyProperty.Register(nameof(Unit), typeof(string), typeof(CircularGauge),
                new FrameworkPropertyMetadata("%", FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LabelBrushProperty =
            DependencyProperty.Register(nameof(LabelBrush), typeof(Brush), typeof(CircularGauge),
                new FrameworkPropertyMetadata(
                    new SolidColorBrush(Color.FromRgb(0xBD, 0xC8, 0xD6)),
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
        public Brush TrackColor
        {
            get => (Brush)GetValue(TrackColorProperty);
            set => SetValue(TrackColorProperty, value);
        }
        public Brush ArcColor
        {
            get => (Brush)GetValue(ArcColorProperty);
            set => SetValue(ArcColorProperty, value);
        }
        public double StrokeWidth
        {
            get => (double)GetValue(StrokeWidthProperty);
            set => SetValue(StrokeWidthProperty, value);
        }
        public string Label
        {
            get => (string)GetValue(LabelProperty);
            set => SetValue(LabelProperty, value);
        }
        public double MaxValue
        {
            get => (double)GetValue(MaxValueProperty);
            set => SetValue(MaxValueProperty, value);
        }

        public string Unit
        {
            get => (string)GetValue(UnitProperty);
            set => SetValue(UnitProperty, value);
        }

        public Brush LabelBrush
        {
            get => (Brush)GetValue(LabelBrushProperty);
            set => SetValue(LabelBrushProperty, value);
        }

        // ── Smooth animation state ──────────────────────────────────────────
        private double _displayValue;      // The value currently being rendered
        private double _animStartValue;    // Value at animation start
        private double _animTargetValue;   // Value we're animating toward
        private DateTime _animStartTime;
        private DispatcherTimer? _animTimer;
        private const double AnimDurationMs = 350; // animation duration in ms

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var gauge = (CircularGauge)d;
            var newVal = (double)e.NewValue;

            gauge._animStartValue = gauge._displayValue;
            gauge._animTargetValue = newVal;
            gauge._animStartTime = DateTime.UtcNow;

            if (gauge._animTimer == null)
            {
                gauge._animTimer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(16) // ~60fps
                };
                gauge._animTimer.Tick += gauge.OnAnimTick;
            }
            gauge._animTimer.Start();
        }

        private void OnAnimTick(object? sender, EventArgs e)
        {
            var elapsed = (DateTime.UtcNow - _animStartTime).TotalMilliseconds;
            var t = Math.Min(elapsed / AnimDurationMs, 1.0);

            // Ease-out cubic: decelerates smoothly
            t = 1.0 - Math.Pow(1.0 - t, 3);

            _displayValue = _animStartValue + (_animTargetValue - _animStartValue) * t;

            InvalidateVisual();

            if (t >= 1.0)
            {
                _displayValue = _animTargetValue;
                _animTimer?.Stop();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            try
            {
                double w = ActualWidth, h = ActualHeight;
                if (w <= 0 || h <= 0) return;

                double sw = StrokeWidth;
                double cx = w / 2, cy = h / 2;
                double r = Math.Min(cx, cy) - sw / 2 - 2;
                if (r <= 0) return;

                double maxV = MaxValue > 0 ? MaxValue : 100.0;
                double pct = Math.Clamp(_displayValue, 0, maxV) / maxV;

                // ── Track arc (full circle) ───────────────────────────────────
                var trackPen = GetOrCreatePen(ref _cachedTrackPen, ref _cachedTrackBrush, TrackColor, sw);
                DrawArc(dc, cx, cy, r, 0, 360, trackPen);

                // ── Value arc ─────────────────────────────────────────────────
                if (pct > 0.001)
                {
                    var valuePen = GetOrCreatePen(ref _cachedArcPen, ref _cachedArcBrush, ArcColor, sw);
                    DrawArc(dc, cx, cy, r, -90, pct * 360 - 90, valuePen);
                }

                // ── Center text ───────────────────────────────────────────────
                var displayVal = Math.Round(_displayValue);
                double dpi = GetDpi();
                var valueFt = new FormattedText(
                    $"{displayVal:F0}{Unit}",
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Semibold"),
                    r * 0.45,
                    Foreground ?? Brushes.White,
                    dpi);

                dc.DrawText(valueFt,
                    new Point(cx - valueFt.Width / 2, cy - valueFt.Height / 2 - (Label.Length > 0 ? 8 : 0)));

                // ── Label beneath value ────────────────────────────────────────
                if (!string.IsNullOrEmpty(Label))
                {
                    var lbBrush = LabelBrush ?? new SolidColorBrush(Color.FromRgb(0xBD, 0xC8, 0xD6));
                    var labelFt = new FormattedText(
                        Label,
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"),
                        r * 0.22,
                        lbBrush,
                        dpi);

                    dc.DrawText(labelFt,
                        new Point(cx - labelFt.Width / 2, cy + valueFt.Height / 2 - 6));
                }
            }
            catch { /* prevent render crash from killing the app */ }
        }

        private double _dpi;
        private double GetDpi()
        {
            if (_dpi > 0) return _dpi;
            try { _dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip; }
            catch { _dpi = 1.0; }
            return _dpi;
        }

        private Pen? _cachedTrackPen;
        private Brush? _cachedTrackBrush;
        private Pen? _cachedArcPen;
        private Brush? _cachedArcBrush;

        private static Pen GetOrCreatePen(ref Pen? cached, ref Brush? cachedBrush, Brush brush, double width)
        {
            if (cached != null && cachedBrush == brush && cached.Thickness == width)
                return cached;
            cachedBrush = brush;
            cached = new Pen(brush, width) { LineJoin = PenLineJoin.Round };
            if (cached.CanFreeze) cached.Freeze();
            return cached;
        }

        private static void DrawArc(DrawingContext dc, double cx, double cy, double r,
            double startDeg, double endDeg, Pen pen)
        {
            double startRad = startDeg * Math.PI / 180.0;
            double endRad = endDeg * Math.PI / 180.0;

            var startPt = new Point(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
            var endPt = new Point(cx + r * Math.Cos(endRad), cy + r * Math.Sin(endRad));

            double sweepDeg = endDeg - startDeg;
            bool isLargeArc = sweepDeg > 180;

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(startPt, false, false);
                ctx.ArcTo(endPt, new Size(r, r), 0, isLargeArc, SweepDirection.Clockwise, true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }
    }
}
