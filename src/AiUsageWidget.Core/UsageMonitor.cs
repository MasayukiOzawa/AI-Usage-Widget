namespace AiUsageWidget.Core;
public sealed class UsageMonitor : IAsyncDisposable
{
    private sealed class Slot(IUsageProvider provider) { public IUsageProvider Provider = provider; public SemaphoreSlim Gate = new(1); public long NextTicks; public int Failures; public UsageSnapshot? Last; public DateTimeOffset RecordedAt; public string? RecordedWindows; }
    private readonly Slot[] slots;
    private readonly HistoryStore history;
    private readonly WidgetSettings settings;
    private readonly CancellationTokenSource lifetime = new();
    private Task? loop;
    public event Action<ProviderDescriptor, UsageSnapshot>? Updated;
    public event Action<ProviderDescriptor, QuotaNotice>? Notice;
    public UsageMonitor(IEnumerable<IUsageProvider> providers, HistoryStore history, WidgetSettings settings)
    { slots = providers.Select(p => new Slot(p)).ToArray(); this.history = history; this.settings = settings; }
    public void Start()
    {
        loop = Task.Run(async () =>
        {
            while (!lifetime.IsCancellationRequested)
            {
                foreach (var slot in slots) if (DateTimeOffset.UtcNow.UtcTicks >= Interlocked.Read(ref slot.NextTicks)) _ = RefreshAsync(slot);
                try { await Task.Delay(1000, lifetime.Token); } catch (OperationCanceledException) { break; }
            }
        });
    }
    public void Refresh() { foreach (var slot in slots) Interlocked.Exchange(ref slot.NextTicks, 0); }
    public static int RetrySeconds(int failures) => failures switch { <= 1 => 60, 2 => 120, _ => 300 };
    public static int RefreshSeconds(ProviderDescriptor descriptor, int configured) =>
        Math.Max(Math.Clamp(configured, 15, 3600), descriptor.MinimumRefreshSeconds);
    private async Task RefreshAsync(Slot slot)
    {
        if (!await slot.Gate.WaitAsync(0)) return;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(40));
            var snapshot = await slot.Provider.GetSnapshotAsync(timeout.Token);
            slot.Failures = 0; slot.Last = snapshot;
            var signature = System.Text.Json.JsonSerializer.Serialize(new { snapshot.AccountKey, snapshot.Windows });
            if (snapshot.Status == UsageStatus.Ready && (signature != slot.RecordedWindows || snapshot.ReceivedAt - slot.RecordedAt >= TimeSpan.FromMinutes(1)))
            { history.Record(snapshot); slot.RecordedAt = snapshot.ReceivedAt; slot.RecordedWindows = signature; }
            if (settings.Notifications) foreach (var notice in history.CheckNotifications(snapshot)) Notice?.Invoke(slot.Provider.Descriptor, notice);
            Updated?.Invoke(slot.Provider.Descriptor, snapshot);
            Interlocked.Exchange(ref slot.NextTicks, DateTimeOffset.UtcNow.AddSeconds(slot.Provider.Descriptor.UpdateMode == UpdateMode.Activity ? 2 : RefreshSeconds(slot.Provider.Descriptor, settings.GetRefreshSeconds(slot.Provider.Descriptor.Id))).UtcTicks);
        }
        catch (Exception error)
        {
            if (lifetime.IsCancellationRequested) return;
            var rateLimited = error is HttpRequestException { StatusCode: System.Net.HttpStatusCode.TooManyRequests };
            Interlocked.Exchange(ref slot.NextTicks, DateTimeOffset.UtcNow.AddSeconds(rateLimited ? 300 : RetrySeconds(++slot.Failures)).UtcTicks);
            var auth = error.Message.Contains("auth", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("login", StringComparison.OrdinalIgnoreCase) || error.Message.Contains("401");
            var message = auth ? "ログインが必要です。設定の接続案内をご確認ください。" : error is FileNotFoundException ? "実行ファイルが見つかりません。設定をご確認ください。" : "更新失敗 · 自動で再試行します。接続と設定をご確認ください。";
            var previous = slot.Last ?? history.Read(slot.Provider.Descriptor.Id, 31).LastOrDefault();
            var status = auth ? UsageStatus.AuthenticationRequired : rateLimited && previous != null ? UsageStatus.Stale : UsageStatus.Error;
            Updated?.Invoke(slot.Provider.Descriptor, (previous ?? new(slot.Provider.Descriptor.Id, "default", DateTimeOffset.MinValue, slot.Provider.Descriptor.Name, UsageStatus.Error, [])) with { Status = status, Message = rateLimited && previous != null ? null : message });
        }
        finally { slot.Gate.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); if (loop != null) await loop;
        await Task.WhenAll(slots.Select(async slot => { await slot.Gate.WaitAsync(); try { await slot.Provider.DisposeAsync(); } finally { slot.Gate.Release(); } }));
        lifetime.Dispose();
    }
}
