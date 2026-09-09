using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiUsageWidget.Core;

public enum UpdateMode { Poll, Activity }
public enum UsageStatus { Ready, Waiting, Stale, AuthenticationRequired, Error }
[Flags] public enum UsageCapabilities { Quota = 1, TokenHistory = 2 }
public sealed record ProviderDescriptor(string Id, string Name, string Color, UpdateMode UpdateMode, UsageCapabilities Capabilities, string HelpUrl)
{
    public int MinimumRefreshSeconds { get; init; }
}
public sealed record QuotaWindow(string Id, string Label, double? RemainingPercent, DateTimeOffset? ResetsAt, bool Unlimited = false, string Unit = "使用枠")
{
    public bool IsSupplemental { get; init; }
    public double? UsedAmount { get; init; }
    public double? LimitAmount { get; init; }
    public double? UsedPercent => RemainingPercent is { } value ? Math.Clamp(100 - value, 0, 100) : null;
}
public sealed record TokenDay(DateOnly Date, long Tokens);
public sealed record AvailableModel(string Id, string Name, string? Description);
public sealed record CapabilityItem(string Name, string Detail, string? Description = null);
public sealed record CapabilityLocation(string Label, string Value);
public sealed record ProviderCapabilities(IReadOnlyList<CapabilityItem> Skills, IReadOnlyList<CapabilityItem> Plugins,
    IReadOnlyList<CapabilityItem> McpServers, string? Message = null);
public sealed record UsageSnapshot(string ProviderId, string AccountKey, DateTimeOffset ReceivedAt, string Source, UsageStatus Status,
    IReadOnlyList<QuotaWindow> Windows, string? Message = null, IReadOnlyList<TokenDay>? TokenHistory = null)
{
    public string? Plan { get; init; }
    public IReadOnlyList<AvailableModel>? Models { get; init; }
    public string? ModelsMessage { get; init; }
    public ProviderCapabilities? Capabilities { get; init; }
}
public interface IUsageProvider : IAsyncDisposable
{
    ProviderDescriptor Descriptor { get; }
    Task<UsageSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static JsonElement? Get(this JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;
    public static string? Text(this JsonElement e, string key) => e.Get(key) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
    public static double? Number(this JsonElement e, string key) => e.Get(key) is { } v && v.TryGetDoubleSafe(out var n) && double.IsFinite(n) ? n : null;
    private static bool TryGetDoubleSafe(this JsonElement e, out double n) { n = 0; return e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out n); }
    public static bool Flag(this JsonElement e, string key) => e.Get(key)?.ValueKind == JsonValueKind.True;
    public static DateTimeOffset? Unix(this JsonElement e, string key)
    {
        if (e.Get(key) is not { ValueKind: JsonValueKind.Number } v || !v.TryGetInt64(out var n)) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(n); } catch (ArgumentOutOfRangeException) { return null; }
    }
}

public static class ProviderParsers
{
    public static IReadOnlyList<QuotaWindow> Codex(JsonElement result)
    {
        var output = new List<QuotaWindow>();
        if (result.Get("rateLimitsByLimitId") is { ValueKind: JsonValueKind.Object } map && map.EnumerateObject().Any())
            foreach (var pair in map.EnumerateObject().OrderBy(p => p.Name == "codex" ? "" : p.Name)) AddBucket(pair.Name, pair.Value);
        else if (result.Get("rateLimits") is { } legacy) AddBucket(legacy.Text("limitId") ?? "codex", legacy);
        return output;
        void AddBucket(string id, JsonElement bucket)
        {
            foreach (var key in new[] { "primary", "secondary" })
            {
                if (bucket.Get(key) is not { ValueKind: JsonValueKind.Object } window) continue;
                var minutes = window.Number("windowDurationMins");
                var period = minutes switch { 10080 => "週間", 300 => "5時間", > 0 when minutes % 1440 == 0 => $"{minutes / 1440:0}日", > 0 when minutes % 60 == 0 => $"{minutes / 60:0}時間", > 0 => $"{minutes:0}分", _ => key };
                var name = bucket.Text("limitName") ?? (id == "codex" ? "通常枠" : id);
                output.Add(new($"{id}/{key}", $"{name} · {period}", Remaining(window.Number("usedPercent")), window.Unix("resetsAt")) { IsSupplemental = id != "codex" });
            }
        }
    }
    public static IReadOnlyList<TokenDay> Tokens(JsonElement result)
    {
        if (result.Get("dailyUsageBuckets") is not { ValueKind: JsonValueKind.Array } rows) return [];
        return rows.EnumerateArray().Select(row =>
            DateOnly.TryParseExact(row.Text("startDate"), "yyyy-MM-dd", out var date) && row.Get("tokens") is { ValueKind: JsonValueKind.Number } n && n.TryGetInt64(out var count) && count >= 0
            ? new TokenDay(date, count) : null).OfType<TokenDay>().GroupBy(x => x.Date).Select(g => g.Last()).OrderBy(x => x.Date).ToArray();
    }
    public static IReadOnlyList<QuotaWindow> Copilot(JsonElement result, DateTimeOffset receivedAt, JsonElement? user = null)
    {
        if (result.Get("quotaSnapshots") is not { ValueKind: JsonValueKind.Object } map) return [];
        return map.EnumerateObject().OrderBy(x => x.Name == "premium_interactions" ? "" : x.Name).Select(pair =>
        {
            var v = pair.Value;
            // The SDK may synthesize the current time when no upstream reset is available.
            DateTimeOffset? reset = DateTimeOffset.TryParse(v.Text("resetDate"), out var parsed) && parsed > receivedAt.AddMinutes(1) ? parsed : null;
            var unlimited = v.Flag("isUnlimitedEntitlement") || v.Number("entitlementRequests") == -1;
            var name = pair.Name switch { "premium_interactions" => "プレミアム使用枠", "chat" => "チャット", "completions" => "コード補完", _ => pair.Name };
            var raw = (user?.Get("quota_snapshots") ?? user?.Get("quotaSnapshots"))?.Get(pair.Name);
            var billing = raw?.Get("token_based_billing") ?? raw?.Get("tokenBasedBilling") ?? user?.Get("token_based_billing") ?? user?.Get("tokenBasedBilling");
            var unit = billing?.ValueKind == JsonValueKind.True ? "クレジット" : billing?.ValueKind == JsonValueKind.False ? "リクエスト" : "単位不明";
            return new QuotaWindow(pair.Name, name, unlimited ? null : Clamp(v.Number("remainingPercentage")), reset, unlimited, unit)
            {
                IsSupplemental = unlimited,
                UsedAmount = NonNegative(v.Number("usedRequests")),
                LimitAmount = unlimited ? null : NonNegative(v.Number("entitlementRequests"))
            };
        }).ToArray();
    }
    public static IReadOnlyList<QuotaWindow> Claude(JsonElement input)
    {
        if (input.Get("rate_limits") is not { ValueKind: JsonValueKind.Object } limits) return [];
        return limits.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Object).Select(p => new QuotaWindow(p.Name,
            p.Name switch { "five_hour" => "5時間", "seven_day" => "週間", "seven_day_opus" => "Opus · 週間", "seven_day_sonnet" => "Sonnet · 週間", _ => p.Name },
            Remaining(p.Value.Number("used_percentage")), p.Value.Unix("resets_at"))).ToArray();
    }
    public static IReadOnlyList<QuotaWindow> ClaudeUsage(JsonElement input)
    {
        var output = new List<QuotaWindow>();
        if (input.ValueKind == JsonValueKind.Object)
        {
            foreach (var pair in input.EnumerateObject())
            {
                if (pair.Value.ValueKind != JsonValueKind.Object || pair.Value.Number("utilization") is not { } used) continue;
                output.Add(new(pair.Name, ClaudeLabel(pair.Name), Remaining(used), IsoDate(pair.Value.Text("resets_at"))));
            }
            if (input.Get("meters") is { ValueKind: JsonValueKind.Array } meters) AddMeters(meters);
        }
        else if (input.ValueKind == JsonValueKind.Array) AddMeters(input);
        return output.Where(x => x.Id != "nimbus_quill").GroupBy(x => x.Id).Select(x => x.Last()).ToArray();

        void AddMeters(JsonElement meters)
        {
            foreach (var meter in meters.EnumerateArray())
            {
                if (meter.ValueKind != JsonValueKind.Object || meter.Number("percent") is not { } used) continue;
                var id = meter.Text("kind") ?? meter.Text("group") ?? $"meter_{output.Count}";
                var label = ClaudeLabel(id);
                if (meter.Get("scope")?.Get("model")?.Text("display_name") is { Length: > 0 } model) label = $"{model} · 週間";
                output.Add(new(id, label, Remaining(used), IsoDate(meter.Text("resets_at"))));
            }
        }
    }
    private static string ClaudeLabel(string id) => id switch
    {
        "five_hour" or "session" => "5時間",
        "seven_day" or "weekly_all" => "週間",
        "seven_day_opus" => "Opus · 週間",
        "seven_day_sonnet" => "Sonnet · 週間",
        "weekly_scoped" => "モデル別 · 週間",
        "extra_usage" => "追加使用枠",
        _ => id
    };
    private static DateTimeOffset? IsoDate(string? value) => DateTimeOffset.TryParse(value, out var date) ? date : null;
    private static double? Remaining(double? used) => used is { } n ? Math.Clamp(100 - n, 0, 100) : null;
    private static double? NonNegative(double? value) => value is >= 0 ? value : null;
    private static double? Clamp(double? value) => value is { } n ? Math.Clamp(n, 0, 100) : null;
}
