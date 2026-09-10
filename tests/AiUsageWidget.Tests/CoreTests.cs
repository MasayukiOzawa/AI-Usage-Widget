using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net;
using AiUsageWidget.Core;
using Xunit;
namespace AiUsageWidget.Tests;
public sealed class CoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "AiUsageWidgetTests", Guid.NewGuid().ToString("N"));
    private static JsonElement Parse(string text) => JsonDocument.Parse(text).RootElement;
    [Fact] public void CodexPrefersMultiBucketAndAcceptsWeeklyOnly()
    {
        var q = ProviderParsers.Codex(Parse("""{"rateLimits":{"primary":{"usedPercent":99}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":1,"windowDurationMins":10080,"resetsAt":1800000000},"secondary":null},"extra":{"limitName":"Extra","primary":{"usedPercent":42}}}}"""));
        Assert.Equal(2, q.Count); Assert.Equal(99, q[0].RemainingPercent); Assert.Contains("週間", q[0].Label); Assert.Equal("Extra · primary", q[1].Label);
    }
    [Fact] public void CodexLegacyAndMissingValuesRemainUnknown()
    {
        var q = ProviderParsers.Codex(Parse("""{"rateLimits":{"primary":{"usedPercent":null,"resetsAt":"bad"},"secondary":{"usedPercent":140}}}"""));
        Assert.Null(q[0].RemainingPercent); Assert.Null(q[0].ResetsAt); Assert.Equal(0, q[1].RemainingPercent);
    }
    [Fact] public void TokenHistoryIs64BitSortedAndNotZeroFilled()
    {
        var rows = ProviderParsers.Tokens(Parse("""{"dailyUsageBuckets":[{"startDate":"2026-09-07","tokens":3416518819},{"startDate":"invalid","tokens":2},{"startDate":"2026-09-02","tokens":9}]}"""));
        Assert.Equal(2, rows.Count); Assert.Equal(3416518819L, rows[1].Tokens); Assert.Equal(new DateOnly(2026,9,2), rows[0].Date);
    }
    [Fact] public void CopilotUnlimitedDoesNotDivideByZeroOrInventRequestUnits()
    {
        var q = ProviderParsers.Copilot(Parse("""{"quotaSnapshots":{"chat":{"isUnlimitedEntitlement":true,"entitlementRequests":0,"remainingPercentage":100},"premium_interactions":{"remainingPercentage":99.1,"tokenBasedBilling":true,"resetDate":"2020-01-01T00:00:00Z"},"future":{"remainingPercentage":null}}}"""), DateTimeOffset.UtcNow);
        Assert.Equal(99.1, q[0].RemainingPercent); Assert.Null(q[0].ResetsAt); Assert.Equal("単位不明", q[0].Unit); Assert.True(q[1].Unlimited); Assert.Null(q[1].RemainingPercent); Assert.Null(q[2].RemainingPercent);
    }
    [Fact] public void ClaudeWindowsAreIndependentAndExpiredValuesNotReset()
    {
        var q = ProviderParsers.Claude(Parse("""{"rate_limits":{"five_hour":{"used_percentage":92,"resets_at":1},"seven_day":null,"future":{"used_percentage":3}}}"""));
        Assert.Equal(2, q.Count); Assert.Equal(8, q[0].RemainingPercent); Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1), q[0].ResetsAt);
    }
    [Fact] public void NotificationsPersistCrossingRecoveryAndAccountBoundaries()
    {
        var now = DateTimeOffset.UtcNow; var reset = now.AddDays(1);
        UsageSnapshot S(double remaining, string account = "a") => new("codex", account, now, "test", UsageStatus.Ready, [new("q", "Quota", remaining, reset)]);
        using (var db = new HistoryStore(root)) { Assert.Single(db.CheckNotifications(S(19))); Assert.Empty(db.CheckNotifications(S(18))); Assert.Single(db.CheckNotifications(S(9))); }
        using var reopened = new HistoryStore(root); Assert.Empty(reopened.CheckNotifications(S(9))); Assert.Single(reopened.CheckNotifications(S(9, "b")));
        Assert.Empty(reopened.CheckNotifications(S(95))); Assert.Single(reopened.CheckNotifications(S(19)));
        Assert.Empty(reopened.CheckNotifications(S(9) with { Status = UsageStatus.Stale }));
        Assert.Single(reopened.CheckNotifications(S(9) with { Windows = [new("q", "Quota", 9, reset.AddDays(1))] }));
    }
    [Fact] public void HistoryDeduplicatesAndExcludesErrorsAndStaleSamples()
    {
        using var db = new HistoryStore(root); var snapshot = new UsageSnapshot("claude", "a", DateTimeOffset.UtcNow, "test", UsageStatus.Ready, [new("q", "Q", 10, null)]);
        db.Record(snapshot); db.Record(snapshot); db.Record(snapshot with { ReceivedAt = DateTimeOffset.UtcNow.AddMinutes(1), Status = UsageStatus.Stale });
        Assert.Single(db.Read("claude", 7)); Assert.Empty(db.Read("codex", 7)); Assert.Empty(db.Read("claude", 7, "other"));
    }
    [Fact] public void TokensPersistSeparatelyWithoutDuplicatingEverySample()
    {
        using var db = new HistoryStore(root); var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var s = new UsageSnapshot("codex", "a", DateTimeOffset.UtcNow, "test", UsageStatus.Ready, [], TokenHistory: [new(today, 3416518819L)]);
        db.Record(s); Assert.Null(Assert.Single(db.Read("codex", 7)).TokenHistory); Assert.Equal(3416518819L, Assert.Single(db.ReadTokens("codex","a")).Tokens);
    }
    [Fact] public async Task ClaudeCollectorIsConcurrentAndDoesNotPersistPrompts()
    {
        await Task.WhenAll(Enumerable.Range(0, 12).Select(i => Task.Run(() => ClaudeIntegration.Collect(root, JsonSerializer.Serialize(new { session_id = "../../" + i, prompt = "SECRET PROMPT", rate_limits = new { five_hour = new { used_percentage = i } } }), DateTimeOffset.UtcNow))));
        var files = Directory.GetFiles(Path.Combine(root, "claude")); Assert.Equal(12, files.Length);
        foreach (var file in files) Assert.DoesNotContain("SECRET", File.ReadAllText(file));
    }
    [Fact] public void ClaudeDirectUsageParsesLegacyWindows()
    {
        var q = ProviderParsers.ClaudeUsage(Parse("""{"five_hour":{"utilization":70,"resets_at":"2026-09-09T00:00:00Z"},"seven_day":{"utilization":20},"nimbus_quill":{"utilization":0}}"""));
        Assert.Equal(2, q.Count); Assert.Equal(30, q[0].RemainingPercent); Assert.Equal("5時間", q[0].Label);
        Assert.DoesNotContain(q, window => window.Id == "nimbus_quill");
    }
    [Fact] public void ClaudeDirectUsageParsesMeterSchema()
    {
        var q = ProviderParsers.ClaudeUsage(Parse("""[{"kind":"session","percent":5},{"kind":"weekly_scoped","percent":16,"scope":{"model":{"display_name":"Fable"}}}]"""));
        Assert.Equal(95, q[0].RemainingPercent); Assert.Equal("Fable · 週間", q[1].Label);
    }
    [Fact] public async Task ClaudeDirectProviderReturnsDisconnectedForEmptyQuota()
    {
        var credentials = Path.Combine(root, "credentials.json"); Directory.CreateDirectory(root);
        File.WriteAllText(credentials, JsonSerializer.Serialize(new { claudeAiOauth = new { accessToken = "test", expiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(), subscriptionType = "free" } }));
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, "{}"));
        await using var provider = new ClaudeProvider(root, http, credentials);
        var snapshot = await provider.GetSnapshotAsync(default);
        Assert.Equal(UsageStatus.AuthenticationRequired, snapshot.Status); Assert.Empty(snapshot.Windows);
    }
    [Fact] public async Task ClaudeProviderRefreshesCliAuthenticationWhenCredentialFileIsMissing()
    {
        var credentials = Path.Combine(root, "refreshed-credentials.json"); Directory.CreateDirectory(root); var refreshed = false;
        Task Refresh(CancellationToken _)
        {
            refreshed = true;
            File.WriteAllText(credentials, JsonSerializer.Serialize(new { claudeAiOauth = new { accessToken = "refreshed", expiresAt = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(), subscriptionType = "pro" } }));
            return Task.CompletedTask;
        }
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, "{}"));
        await using var provider = new ClaudeProvider(root, http, credentials, Refresh);
        var snapshot = await provider.GetSnapshotAsync(default);
        Assert.True(refreshed); Assert.Equal("pro", snapshot.Plan);
    }
    [Fact] public async Task ClaudeProviderRefreshesBeforeAccessTokenExpires()
    {
        var credentials = Path.Combine(root, "expiring-credentials.json"); Directory.CreateDirectory(root); var refreshed = false;
        File.WriteAllText(credentials, JsonSerializer.Serialize(new { claudeAiOauth = new { accessToken = "expiring", refreshToken = "refresh", scopes = new[] { "user:inference" }, expiresAt = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() } }));
        Task Refresh(CancellationToken _)
        {
            refreshed = true;
            File.WriteAllText(credentials, JsonSerializer.Serialize(new { claudeAiOauth = new { accessToken = "fresh", refreshToken = "rotated", scopes = new[] { "user:inference" }, expiresAt = DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeMilliseconds() } }));
            return Task.CompletedTask;
        }
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, "{}"));
        await using var provider = new ClaudeProvider(root, http, credentials, Refresh);
        await provider.GetSnapshotAsync(default);
        Assert.True(refreshed);
    }
    [Fact] public async Task ClaudeProviderKeepsUnexpiredAccessTokenWhenProactiveRefreshFails()
    {
        var credentials = Path.Combine(root, "fallback-credentials.json"); Directory.CreateDirectory(root);
        File.WriteAllText(credentials, JsonSerializer.Serialize(new { claudeAiOauth = new { accessToken = "still-valid", refreshToken = "invalid", scopes = new[] { "user:inference" }, expiresAt = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds() } }));
        Task Refresh(CancellationToken _) => Task.FromException(new IOException("refresh failed"));
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, "{}"));
        await using var provider = new ClaudeProvider(root, http, credentials, Refresh);
        var snapshot = await provider.GetSnapshotAsync(default);
        Assert.Equal(UsageStatus.AuthenticationRequired, snapshot.Status);
    }
    [Fact] public async Task ClaudeProviderRefreshesAndRetriesAfterUnauthorizedUsageResponse()
    {
        var credentials = Path.Combine(root, "unauthorized-credentials.json"); Directory.CreateDirectory(root); var refreshed = false;
        File.WriteAllText(credentials, JsonSerializer.Serialize(new { claudeAiOauth = new { accessToken = "rejected", refreshToken = "refresh", scopes = new[] { "user:inference" }, expiresAt = DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeMilliseconds() } }));
        Task Refresh(CancellationToken _)
        {
            refreshed = true;
            File.WriteAllText(credentials, JsonSerializer.Serialize(new { claudeAiOauth = new { accessToken = "accepted", refreshToken = "rotated", scopes = new[] { "user:inference" }, expiresAt = DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeMilliseconds() } }));
            return Task.CompletedTask;
        }
        var handler = new UnauthorizedThenSuccessHandler();
        using var http = new HttpClient(handler);
        await using var provider = new ClaudeProvider(root, http, credentials, Refresh);
        await provider.GetSnapshotAsync(default);
        Assert.True(refreshed); Assert.Equal(2, handler.Requests);
    }
    [Fact] public async Task ClaudeProviderDoesNotRefreshWithExpiredRefreshToken()
    {
        var credentials = Path.Combine(root, "expired-credentials.json"); Directory.CreateDirectory(root);
        File.WriteAllText(credentials, JsonSerializer.Serialize(new { claudeAiOauth = new { accessToken = "expired", refreshToken = "expired-refresh", scopes = new[] { "user:inference" }, expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds(), refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeMilliseconds() } }));
        using var http = new HttpClient(new StubHandler(HttpStatusCode.OK, "{}"));
        await using var provider = new ClaudeProvider(root, http, credentials);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => provider.GetSnapshotAsync(default));
    }
    [Fact] public void ClaudeInstallUninstallPreservesOtherSettingsAndRejectsOverwrite()
    {
        var home = Path.Combine(root, "home"); Directory.CreateDirectory(home); var bridge = Path.Combine(root,"bridge.exe"); File.WriteAllText(bridge, "test");
        var path = Path.Combine(home,"settings.json"); File.WriteAllText(path,"""{"statusLine":{"type":"command","command":"oh-my-posh claude","padding":0},"theme":"dark"}""");
        ClaudeIntegration.Install(root, home, bridge); Assert.Throws<InvalidOperationException>(() => ClaudeIntegration.Install(root, home, bridge));
        var settings = JsonNode.Parse(File.ReadAllText(path))!; settings["newSetting"] = true; File.WriteAllText(path, settings.ToJsonString());
        ClaudeIntegration.Uninstall(root, home); settings = JsonNode.Parse(File.ReadAllText(path))!; Assert.True(settings["newSetting"]!.GetValue<bool>()); Assert.Equal("oh-my-posh claude", settings["statusLine"]!["command"]!.GetValue<string>());
        ClaudeIntegration.Install(root, home, bridge); settings = JsonNode.Parse(File.ReadAllText(path))!; settings["statusLine"]!["command"] = "user-change"; File.WriteAllText(path,settings.ToJsonString());
        Assert.Throws<InvalidOperationException>(() => ClaudeIntegration.Uninstall(root, home));
    }
    [Theory][InlineData(1,60)][InlineData(2,120)][InlineData(3,300)][InlineData(10,300)]
    public void BackoffIsBounded(int failures, int expected) => Assert.Equal(expected, UsageMonitor.RetrySeconds(failures));
    [Fact] public void ProviderMinimumRefreshIsRespected()
    {
        var descriptor = new ProviderDescriptor("claude", "Claude Code", "#fff", UpdateMode.Poll, UsageCapabilities.Quota, "") { MinimumRefreshSeconds = 300 };
        Assert.Equal(300, UsageMonitor.RefreshSeconds(descriptor, 60));
        Assert.Equal(600, UsageMonitor.RefreshSeconds(descriptor, 600));
    }
    [Fact] public void LegacyRefreshSettingMigratesPerProvider()
    {
        Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, "settings.json"), """{"refreshSeconds":90}""");
        var settings = WidgetSettings.Load(root);
        Assert.Equal(90, settings.CodexRefreshSeconds); Assert.Equal(90, settings.CopilotRefreshSeconds); Assert.Equal(300, settings.ClaudeRefreshSeconds);
        Assert.Equal(90, settings.GetRefreshSeconds("codex")); Assert.Equal(300, settings.GetRefreshSeconds("claude"));
    }
    [Fact] public async Task OneProviderFailureDoesNotBlockAnother()
    {
        using var db = new HistoryStore(root); var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var monitor = new UsageMonitor([new FakeProvider("bad", true),new FakeProvider("good",false)], db, new WidgetSettings { Notifications = false });
        monitor.Updated += (d,s) => { if (d.Id == "good" && s.Status == UsageStatus.Ready) ready.TrySetResult(); };
        monitor.Start(); await ready.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Single(db.Read("good",1));
    }
    private sealed class FakeProvider(string id, bool fail) : IUsageProvider
    {
        public ProviderDescriptor Descriptor => new(id,id,"#FFFFFF",UpdateMode.Poll,UsageCapabilities.Quota,"");
        public Task<UsageSnapshot> GetSnapshotAsync(CancellationToken ct) => fail ? Task.FromException<UsageSnapshot>(new IOException("offline")) : Task.FromResult(new UsageSnapshot(id,"a",DateTimeOffset.UtcNow,"test",UsageStatus.Ready,[]));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class StubHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
    }
    private sealed class UnauthorizedThenSuccessHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(Requests == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
            { Content = new StringContent("{}") });
        }
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
}
