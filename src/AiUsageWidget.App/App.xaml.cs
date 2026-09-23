using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using AiUsageWidget.Core;
using Forms = System.Windows.Forms;
namespace AiUsageWidget.App;
public partial class WidgetApp : Application
{
    public string Root { get; } = AppPaths.Root;
    public WidgetSettings Settings { get; private set; } = new();
    public HistoryStore History { get; private set; } = null!;
    public UsageMonitor Monitor { get; private set; } = null!;
    public IReadOnlyList<ProviderDescriptor> Descriptors { get; private set; } = [];
    public bool IsExiting { get; private set; }
    private Mutex? mutex;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private MainWindow? widget;
    private IReadOnlyList<IUsageProvider> CreateProviders() => [new CodexProvider(() => Settings.CodexPath), new CopilotProvider(() => Settings.CopilotPath), new ClaudeProvider(Root)];
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            Settings = WidgetSettings.Load(Root);
            if (e.Args.Contains("--install-claude")) { ClaudeIntegration.Install(Root, AppPaths.ClaudeHome, BridgePath); Shutdown(); return; }
            if (e.Args.Contains("--uninstall-claude")) { ClaudeIntegration.Uninstall(Root, AppPaths.ClaudeHome); Shutdown(); return; }
            if (Value(e.Args, "--probe") is { } probePath) { await ProbeAsync(probePath); Shutdown(); return; }
            mutex = new Mutex(true, "Local\\AiUsageWidget-" + Environment.UserName, out var created);
            if (!created) { MessageBox.Show("AI Usage Widget は起動済みです。通知領域のアイコンから表示できます。", "AI Usage Widget"); Shutdown(); return; }
            History = new HistoryStore(Root); var providers = CreateProviders(); Descriptors = providers.Select(p => p.Descriptor).ToArray();
            Monitor = new UsageMonitor(providers, History, Settings);
            widget = new MainWindow(this); MainWindow = widget;
            Monitor.Updated += (d, s) => Dispatcher.BeginInvoke(() => { if (!IsExiting) widget.Apply(d, s); });
            Monitor.Notice += (d, n) => Dispatcher.BeginInvoke(() => { if (!IsExiting && tray != null) tray.ShowBalloonTip(5000, d.Name, $"{n.Label} の残りは {n.RemainingPercent:0.#}% です。", Forms.ToolTipIcon.Warning); });
            CreateTray(); SystemEvents.PowerModeChanged += PowerChanged; SystemEvents.DisplaySettingsChanged += DisplaysChanged;
            widget.Show();
            if (e.Args.Contains("--demo")) ApplyDemo(); else Monitor.Start();
            if (Value(e.Args, "--capture") is { } capture)
            {
                await Task.Delay(e.Args.Contains("--demo") ? 700 : 10000);
                widget.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)widget.ActualWidth, (int)widget.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(widget);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(capture))!);
                using (var stream = File.Create(capture)) { var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); png.Save(stream); }
                await ExitAsync();
            }
        }
        catch (Exception error) { MessageBox.Show(error.Message, "AI Usage Widget · 起動できません", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1); }
    }
    public string BridgePath => Path.Combine(AppContext.BaseDirectory, "bridge", "AiUsageWidget.ClaudeBridge.exe");
    private async Task ProbeAsync(string destination)
    {
        var results = new List<object>();
        foreach (var provider in CreateProviders())
        {
            try { using var timeout = new CancellationTokenSource(40000); var s = await provider.GetSnapshotAsync(timeout.Token); results.Add(new { service = provider.Descriptor.Id, s.Status, s.Plan, s.Windows, tokenDays = s.TokenHistory?.Count, s.Message, s.Models, s.ModelsMessage, s.Capabilities }); }
            catch (Exception error) { results.Add(new { service = provider.Descriptor.Id, error = error.Message }); }
            finally { await provider.DisposeAsync(); }
        }
        AppPaths.WriteAtomic(Path.GetFullPath(destination), JsonSerializer.Serialize(results, Json.Options));
    }
    private void ApplyDemo()
    {
        var now = DateTimeOffset.UtcNow;
        widget!.Apply(Descriptors[0], new("codex", "demo", now, "DEMO", UsageStatus.Ready, [new("codex/primary", "通常枠 · 週間", 76, now.AddDays(3)), new("spark", "Spark · 5時間", 94, now.AddHours(2)) { IsSupplemental = true }]));
        widget.Apply(Descriptors[1], new("copilot", "demo", now, "DEMO", UsageStatus.Ready, [new("premium", "プレミアム使用枠", 18, now.AddDays(10)), new("completion", "コード補完", null, null, true) { IsSupplemental = true }]));
        widget.Apply(Descriptors[2], new("claude", "demo", now.AddMinutes(-8), "DEMO", UsageStatus.Stale, [new("five_hour", "5時間", 42, now.AddHours(1)), new("seven_day", "週間", 8, now.AddDays(2))], "前回の値 · Claude Code 利用時に更新"));
    }
    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("表示", null, (_, _) => ShowWidget()); menu.Items.Add("更新", null, (_, _) => Monitor.Refresh());
        menu.Items.Add("設定", null, (_, _) => { ShowWidget(); new SettingsWindow(this) { Owner = widget }.ShowDialog(); });
        menu.Items.Add("終了", null, async (_, _) => await ExitAsync());
        var iconPath = Path.Combine(AppContext.BaseDirectory, "AiUsageWidget.ico");
        trayIcon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : null;
        tray = new Forms.NotifyIcon { Icon = trayIcon ?? System.Drawing.SystemIcons.Information, Text = "AI Usage Widget", ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowWidget(); };
    }
    private void ShowWidget() { widget!.KeepOnScreen(); widget.Show(); widget.Activate(); }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) Monitor.Refresh(); }
    private void DisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => widget?.KeepOnScreen());
    public void ApplySettings()
    {
        Settings.Save(Root); if (widget != null) widget.SetPinned(Settings.AlwaysOnTop, false);
        using var run = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
        if (Settings.AutoStart) run.SetValue("AiUsageWidget", $"\"{Environment.ProcessPath}\""); else run.DeleteValue("AiUsageWidget", false);
        Monitor.Refresh();
    }
    public async Task ExitAsync()
    {
        if (IsExiting) return; IsExiting = true;
        SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
        tray?.Dispose(); trayIcon?.Dispose(); trayIcon = null; if (Monitor != null) await Monitor.DisposeAsync(); History?.Dispose();
        widget?.Close(); Shutdown();
    }
    protected override void OnExit(ExitEventArgs e) { tray?.Dispose(); trayIcon?.Dispose(); mutex?.Dispose(); base.OnExit(e); }
    private static string? Value(string[] args, string key) { var i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
