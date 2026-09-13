using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AiUsageWidget.Core;
namespace AiUsageWidget.App;
public partial class MainWindow : Window
{
    private readonly WidgetApp app;
    private int days = 30;
    private DateTimeOffset lastChartRender;
    public ObservableCollection<ProviderViewModel> Providers { get; }
    public MainWindow(WidgetApp app)
    {
        this.app = app; Providers = new(app.Descriptors.Select(d => new ProviderViewModel(d)));
        InitializeComponent(); DataContext = Providers; Topmost = app.Settings.AlwaysOnTop;
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "AiUsageWidget.ico");
        if (System.IO.File.Exists(iconPath)) Icon = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));
        var area = SystemParameters.WorkArea; Height = Math.Min(700, area.Height - 40);
        Left = app.Settings.Left ?? area.Right - Width - 24; Top = app.Settings.Top ?? area.Top + 30;
        KeepOnScreen(); Closing += (_, e) => { if (!app.IsExiting) { e.Cancel = true; Hide(); SavePosition(); } };
        LocationChanged += (_, _) => { if (IsLoaded) SavePosition(); };
        SourceInitialized += (_, _) =>
        {
            var dark = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
        };
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    public void KeepOnScreen()
    {
        if (WindowState != WindowState.Normal) return;
        // Virtual desktop bounds preserve placement on secondary monitors; require a real monitor intersection.
        var dpi = VisualTreeHelper.GetDpi(this); var x = (int)(Left * dpi.DpiScaleX); var y = (int)(Top * dpi.DpiScaleY);
        var rect = new System.Drawing.Rectangle(x, y, (int)(Width * dpi.DpiScaleX), (int)(Height * dpi.DpiScaleY));
        var screen = System.Windows.Forms.Screen.FromRectangle(rect).WorkingArea;
        Left = Math.Clamp(Left, screen.Left / dpi.DpiScaleX, Math.Max(screen.Left / dpi.DpiScaleX, screen.Right / dpi.DpiScaleX - Width));
        Top = Math.Clamp(Top, screen.Top / dpi.DpiScaleY, Math.Max(screen.Top / dpi.DpiScaleY, screen.Bottom / dpi.DpiScaleY - Height));
        if (double.IsNaN(Left) || double.IsInfinity(Left)) Left = 24;
        if (double.IsNaN(Top) || double.IsInfinity(Top)) Top = 24;
    }
    private void SavePosition() { if (WindowState != WindowState.Normal) return; app.Settings.Left = Left; app.Settings.Top = Top; try { app.Settings.Save(app.Root); } catch (System.IO.IOException) { } }
    public void Apply(ProviderDescriptor descriptor, UsageSnapshot snapshot)
    {
        Providers.First(p => p.Descriptor.Id == descriptor.Id).Apply(snapshot);
        if (HistorySection.IsExpanded && DateTimeOffset.UtcNow - lastChartRender > TimeSpan.FromMinutes(1)) RefreshCharts();
    }
    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed) return;
        e.Handled = true;
        try { DragMove(); SavePosition(); }
        catch (InvalidOperationException) { }
    }
    private void RefreshClick(object sender, RoutedEventArgs e) => app.Monitor.Refresh();
    private void HideClick(object sender, RoutedEventArgs e) { SavePosition(); Hide(); }
    private void SettingsClick(object sender, RoutedEventArgs e) => new SettingsWindow(app) { Owner = this }.ShowDialog();
    private void HistoryExpanded(object sender, RoutedEventArgs e) => RefreshCharts();
    private void SevenDays(object sender, RoutedEventArgs e) { days = 7; RefreshCharts(); }
    private void ThirtyDays(object sender, RoutedEventArgs e) { days = 30; RefreshCharts(); }
    public void RefreshCharts()
    {
        if (Charts == null) return;
        lastChartRender = DateTimeOffset.UtcNow;
        Charts.Children.Clear();
        foreach (var p in Providers)
        {
            Charts.Children.Add(new TextBlock { Text = p.Name, Foreground = p.Accent, Margin = new(0,10,0,3), FontWeight = FontWeights.SemiBold });
            var snapshots = app.History.Read(p.Descriptor.Id, days, p.Snapshot?.AccountKey);
            var palette = new[] { p.Descriptor.Color, "#D8CF82", "#85BFFF", "#E9A1D4" };
            var groups = snapshots.SelectMany(s => s.Windows.Where(q => !q.IsSupplemental && q.RemainingPercent.HasValue && !q.Unlimited
                && !(p.Descriptor.Id == "claude" && (q.Id is "nimbus_quill" or "nimbus_quil")))
                .Select(q => (Snapshot:s, Quota:q))).GroupBy(x => x.Quota.Id).ToArray();
            var series = groups.Select((g, i) => new ChartSeries(g.Last().Quota.Label, (Color)ColorConverter.ConvertFromString(palette[i % palette.Length]), g.Select(x => new ChartPoint(x.Snapshot.ReceivedAt, x.Quota.RemainingPercent!.Value, x.Quota.ResetsAt)).ToArray())).ToArray();
            Charts.Children.Add(new UsageChart { Series = series, Start = DateTimeOffset.UtcNow.AddDays(-days) });
            foreach (var s in series) Charts.Children.Add(new TextBlock { Text = "● " + s.Name, FontSize = 10, Foreground = new SolidColorBrush(s.Color), TextWrapping = TextWrapping.Wrap });
            if (p.Descriptor.Id == "copilot")
            {
                var creditSeries = snapshots.SelectMany(s => s.Windows
                    .Where(q => !q.IsSupplemental && q.Unit == "クレジット" && !q.Unlimited && q.UsedAmount is >= 0)
                    .Select(q => (Snapshot: s, Quota: q)))
                    .GroupBy(x => x.Quota.Id)
                    .Select((g, i) => new ChartSeries(g.Last().Quota.Label,
                        (Color)ColorConverter.ConvertFromString(palette[i % palette.Length]),
                        g.OrderBy(x => x.Snapshot.ReceivedAt).Select(x => new ChartPoint(x.Snapshot.ReceivedAt,
                            x.Quota.UsedAmount!.Value, x.Quota.ResetsAt)).ToArray())).ToArray();
                if (creditSeries.Length > 0)
                {
                    Charts.Children.Add(new TextBlock { Text = "使用済みクレジット", Margin = new(0,12,0,0) });
                    Charts.Children.Add(new UsageChart { AutoScale = true, BreakOnDecrease = true,
                        ValueSuffix = " クレジット", Series = creditSeries, Start = DateTimeOffset.UtcNow.AddDays(-days) });
                }
            }
            if (p.Descriptor.Capabilities.HasFlag(UsageCapabilities.TokenHistory))
            {
                Charts.Children.Add(new TextBlock { Text = "日別トークン数", Margin = new(0,12,0,0) });
                var tokens = p.Snapshot?.TokenHistory ?? app.History.ReadTokens(p.Descriptor.Id, p.Snapshot?.AccountKey ?? "default");
                var end = new DateTimeOffset(DateTime.Today.AddDays(1));
                Charts.Children.Add(new UsageChart { Bars = true, End = end, Start = end.AddDays(-days), Series = [new("トークン数", (Color)ColorConverter.ConvertFromString(p.Descriptor.Color), tokens.Select(t => new ChartPoint(new DateTimeOffset(t.Date.ToDateTime(TimeOnly.MinValue)), t.Tokens, null)).ToArray())] });
            }
        }
        Charts.Children.Add(new TextBlock { Text = "未取得期間は補間しません。", TextWrapping = TextWrapping.Wrap, FontSize = 10, Margin = new(0,8,0,4), Foreground = Brushes.LightSlateGray });
    }
}
