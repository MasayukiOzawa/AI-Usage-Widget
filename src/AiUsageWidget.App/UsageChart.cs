using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
namespace AiUsageWidget.App;
public sealed record ChartPoint(DateTimeOffset Time, double Value, DateTimeOffset? Reset);
public sealed record ChartSeries(string Name, Color Color, IReadOnlyList<ChartPoint> Points);
public sealed class UsageChart : FrameworkElement
{
    public IReadOnlyList<ChartSeries> Series { get; set; } = [];
    public DateTimeOffset Start { get; set; } = DateTimeOffset.UtcNow.AddDays(-30);
    public DateTimeOffset End { get; set; } = DateTimeOffset.UtcNow;
    public bool Bars { get; set; }
    public bool AutoScale { get; set; }
    public bool BreakOnDecrease { get; set; }
    public string? ValueSuffix { get; set; }
    public UsageChart() { Height = 140; MouseMove += Hover; }
    private double Max => Bars || AutoScale ? Math.Max(1, Series.SelectMany(s => s.Points).Where(p => p.Time >= Start && p.Time <= End).Select(p => p.Value).DefaultIfEmpty(1).Max()) : 100;
    private double X(DateTimeOffset time) => 30 + (ActualWidth - 38) * (time - Start).TotalSeconds / Math.Max(1, (End - Start).TotalSeconds);
    private double Y(double value) => 10 + (ActualHeight - 36) * (1 - value / Max);
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); if (ActualWidth < 40) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(45, 55, 70)), 1);
        foreach (var fraction in new[] { 0d, .5, 1 })
        { var y = Y(Max * fraction); dc.DrawLine(grid, new(30, y), new(ActualWidth - 8, y)); Text(dc, Bars || AutoScale ? Compact(Max * fraction) : $"{fraction * 100:0}", 0, y - 6); }
        Text(dc, Start.ToLocalTime().ToString("M/d"), 30, ActualHeight - 18); Text(dc, End.ToLocalTime().ToString("M/d"), ActualWidth - 40, ActualHeight - 18);
        foreach (var series in Series)
        {
            var brush = new SolidColorBrush(series.Color); var pen = new Pen(brush, 1.8); ChartPoint? previous = null;
            foreach (var p in series.Points.Where(p => p.Time >= Start && p.Time <= End))
            {
                var point = new Point(X(p.Time), Y(p.Value));
                if (Bars) { var width = Math.Max(2, (ActualWidth - 38) / Math.Max(1, (End - Start).TotalDays) * .65); dc.DrawRoundedRectangle(brush, null, new(point.X, point.Y, Math.Min(width, ActualWidth - point.X), Math.Max(1, Y(0) - point.Y)), 2, 2); }
                else { if (previous is { } last && p.Time - last.Time <= TimeSpan.FromMinutes(5) && last.Reset == p.Reset && (!BreakOnDecrease || p.Value >= last.Value)) dc.DrawLine(pen, new(X(last.Time), Y(last.Value)), point); dc.DrawEllipse(brush, null, point, 1.5, 1.5); }
                previous = p;
            }
        }
        if (!Series.SelectMany(s => s.Points).Any()) Text(dc, "履歴は取得後に表示されます", 42, 55);
    }
    private void Hover(object sender, MouseEventArgs e)
    {
        var x = e.GetPosition(this).X;
        var nearest = Series.SelectMany(s => s.Points.Where(p => p.Time >= Start && p.Time <= End).Select(p => (Series: s, Point: p))).OrderBy(p => Math.Abs(X(p.Point.Time) - x)).Take(1).ToArray();
        ToolTip = nearest.Length == 0 ? null : $"{nearest[0].Series.Name}\n{nearest[0].Point.Time.ToLocalTime():yyyy/M/d HH:mm}\n{nearest[0].Point.Value:#,0.##}{(ValueSuffix ?? (Bars ? " トークン" : "% 残り"))}";
    }
    private void Text(DrawingContext dc, string text, double x, double y) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, new SolidColorBrush(Color.FromRgb(139, 155, 176)), VisualTreeHelper.GetDpi(this).PixelsPerDip), new(x, y));
    private static string Compact(double n) => n >= 1000000 ? $"{n / 1000000:0.#}M" : n >= 1000 ? $"{n / 1000:0.#}K" : $"{n:0}";
}
