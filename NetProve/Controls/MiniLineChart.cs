using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace NetProve.Controls
{
    /// <summary>
    /// A minimal real-time line chart with smooth Bezier curves.
    /// Pure WPF, no external libraries.
    /// </summary>
    public sealed class MiniLineChart : FrameworkElement
    {
        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(ObservableCollection<double>),
                typeof(MiniLineChart),
                new FrameworkPropertyMetadata(null, OnDataChanged));

        public static readonly DependencyProperty LineColorProperty =
            DependencyProperty.Register(nameof(LineColor), typeof(Brush), typeof(MiniLineChart),
                new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6)),
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillColorProperty =
            DependencyProperty.Register(nameof(FillColor), typeof(Brush), typeof(MiniLineChart),
                new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MaxValueProperty =
            DependencyProperty.Register(nameof(MaxValue), typeof(double), typeof(MiniLineChart),
                new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public ObservableCollection<double>? Data
        {
            get => (ObservableCollection<double>?)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }
        public Brush LineColor
        {
            get => (Brush)GetValue(LineColorProperty);
            set => SetValue(LineColorProperty, value);
        }
        public Brush? FillColor
        {
            get => (Brush?)GetValue(FillColorProperty);
            set => SetValue(FillColorProperty, value);
        }
        public double MaxValue
        {
            get => (double)GetValue(MaxValueProperty);
            set => SetValue(MaxValueProperty, value);
        }

        private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var chart = (MiniLineChart)d;
            if (e.OldValue is ObservableCollection<double> oldCol)
                oldCol.CollectionChanged -= chart.OnCollectionChanged;
            if (e.NewValue is ObservableCollection<double> newCol)
                newCol.CollectionChanged += chart.OnCollectionChanged;
            chart.InvalidateVisual();
        }

        private DispatcherTimer? _debounce;

        private void OnCollectionChanged(object? s, NotifyCollectionChangedEventArgs e)
        {
            if (_debounce == null)
            {
                _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                _debounce.Tick += (_, _) => { _debounce.Stop(); InvalidateVisual(); };
            }
            _debounce.Stop();
            _debounce.Start();
        }

        // Cached pen to avoid GC pressure
        private Pen? _cachedLinePen;
        private Brush? _cachedLineBrush;

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            try
            {
                var data = Data;
                if (data == null || data.Count < 2) return;

                double w = ActualWidth, h = ActualHeight;
                if (w <= 0 || h <= 0) return;

                // Snapshot data to avoid collection-modified exceptions
                double[] snapshot;
                try { snapshot = data.ToArray(); }
                catch { return; }

                int count = snapshot.Length;
                if (count < 2) return;

                var pts = new Point[count];
                double maxV = MaxValue > 0 ? MaxValue : 1;
                double step = w / (count - 1);

                for (int i = 0; i < count; i++)
                    pts[i] = new Point(i * step, h - Math.Clamp(snapshot[i] / maxV, 0, 1) * h);

                // Fill area with smooth Bezier
                if (FillColor != null)
                {
                    var fillGeo = new StreamGeometry();
                    using (var ctx = fillGeo.Open())
                    {
                        ctx.BeginFigure(new Point(pts[0].X, h), true, true);
                        ctx.LineTo(pts[0], false, false); // up to first data point
                        DrawSmoothCurve(ctx, pts);
                        ctx.LineTo(new Point(pts[count - 1].X, h), false, false);
                    }
                    fillGeo.Freeze();
                    dc.DrawGeometry(FillColor, null, fillGeo);
                }

                // Line with smooth Bezier
                var lineGeo = new StreamGeometry();
                using (var ctx = lineGeo.Open())
                {
                    ctx.BeginFigure(pts[0], false, false);
                    DrawSmoothCurve(ctx, pts);
                }
                lineGeo.Freeze();

                // Cache pen
                if (_cachedLinePen == null || _cachedLineBrush != LineColor)
                {
                    _cachedLineBrush = LineColor;
                    _cachedLinePen = new Pen(LineColor, 2) { LineJoin = PenLineJoin.Round };
                    if (_cachedLinePen.CanFreeze) _cachedLinePen.Freeze();
                }
                dc.DrawGeometry(null, _cachedLinePen, lineGeo);
            }
            catch { /* prevent render crash from killing the app */ }
        }

        /// <summary>
        /// Draws a smooth Catmull-Rom spline through the given points
        /// by converting to cubic Bezier segments.
        /// </summary>
        private static void DrawSmoothCurve(StreamGeometryContext ctx, Point[] pts)
        {
            if (pts.Length < 2) return;
            if (pts.Length == 2)
            {
                ctx.LineTo(pts[1], true, false);
                return;
            }

            const double tension = 0.35; // 0 = sharp corners, 0.5 = very smooth

            for (int i = 0; i < pts.Length - 1; i++)
            {
                var p0 = pts[Math.Max(i - 1, 0)];
                var p1 = pts[i];
                var p2 = pts[Math.Min(i + 1, pts.Length - 1)];
                var p3 = pts[Math.Min(i + 2, pts.Length - 1)];

                // Control points derived from Catmull-Rom tangents
                var cp1 = new Point(
                    p1.X + (p2.X - p0.X) * tension,
                    p1.Y + (p2.Y - p0.Y) * tension);
                var cp2 = new Point(
                    p2.X - (p3.X - p1.X) * tension,
                    p2.Y - (p3.Y - p1.Y) * tension);

                ctx.BezierTo(cp1, cp2, p2, true, false);
            }
        }
    }
}
