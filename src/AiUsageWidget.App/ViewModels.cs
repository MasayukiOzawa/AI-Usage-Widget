using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using AiUsageWidget.Core;
namespace AiUsageWidget.App;
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Change([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}
public sealed class QuotaViewModel(QuotaWindow quota, bool showReset = true)
{
    public System.Windows.Visibility AmountVisibility => quota.Unlimited || quota.UsedAmount is null && quota.LimitAmount is null
        ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    public string Label => quota.Label;
    public string Remaining => quota.Unlimited ? "無制限" : quota.RemainingPercent is { } p ? $"残り {p:0.#}%" : "取得不可";
    public string Amount => quota.UsedAmount is null && quota.LimitAmount is null
        ? quota.Unlimited ? "使用量：取得不可 · 上限：無制限" : "使用量 / 上限：取得不可"
        : $"使用 {Format(quota.UsedAmount)} / {(quota.Unlimited ? "無制限" : Format(quota.LimitAmount))} {quota.Unit}";
    private static string Format(double? value) => value is { } n ? n.ToString("#,0.##") : "取得不可";
    public double Used => quota.UsedPercent ?? 0;
    public Brush Color => new SolidColorBrush((Color)ColorConverter.ConvertFromString(quota.RemainingPercent is <= 10 ? "#FF8E95" : quota.RemainingPercent is <= 20 ? "#F4C56A" : "#6CE6C0"));
    public System.Windows.Visibility ResetVisibility => showReset &&
        (quota.Unlimited || quota.ResetsAt != null || quota.RemainingPercent is < 100)
        ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public string Reset => quota.Unlimited ? "制限なし" : quota.ResetsAt is not { } reset ? "リセット時刻：取得不可" : reset <= DateTimeOffset.UtcNow ? "リセット確認待ち" : $"{reset.ToLocalTime():M/d HH:mm} にリセット · あと {Duration(reset - DateTimeOffset.UtcNow)}";
    private static string Duration(TimeSpan span) => span.TotalDays >= 1 ? $"{(int)span.TotalDays}日 {span.Hours}時間" : span.TotalHours >= 1 ? $"{(int)span.TotalHours}時間 {span.Minutes}分" : $"{Math.Max(1, (int)span.TotalMinutes)}分";
}
public sealed class ResetCreditViewModel(RateLimitResetCredit credit)
{
    public string Title => credit.ResetType == "codexRateLimits" || string.Equals(credit.Title, "Rate-limit reset", StringComparison.OrdinalIgnoreCase)
        ? "完全リセット" : string.IsNullOrWhiteSpace(credit.Title) ? "利用上限のリセット" : credit.Title;
    public string Status => credit.Status switch { "available" => "利用可能", "consumed" or "redeemed" => "使用済み", "expired" => "期限切れ", { Length: > 0 } value => value, _ => "取得不可" };
    public string Expires => $"有効期限: {Format(credit.ExpiresAt)}";
    public string Granted => $"付与日時: {Format(credit.GrantedAt)}";
    private static string Format(DateTimeOffset? value) => value is { } date ? date.ToLocalTime().ToString("M/d HH:mm") : "取得不可";
}
public sealed class ResetStatusViewModel(RateLimitResetStatus status)
{
    public string Availability => status.AvailableCount is { } count ? $"利用可能 {count}回" : "取得不可";
    public string LimitState => $"制限状態: {(status.ReachedTypes.Count == 0 ? "未到達" : string.Join(" / ", status.ReachedTypes))}";
    public IReadOnlyList<ResetCreditViewModel> Credits { get; } = status.Credits.Select(x => new ResetCreditViewModel(x)).ToArray();
}

public sealed class ProviderViewModel(ProviderDescriptor descriptor) : Observable
{
    private readonly IReadOnlyList<CapabilityLocation> baseDetailLocations = CapabilityLocations.For(descriptor.Id);
    public ProviderDescriptor Descriptor { get; } = descriptor;
    public string Name => Descriptor.Name;
    public IReadOnlyList<CapabilityLocation> DetailLocations => baseDetailLocations.Concat(Snapshot?.Details ?? []).ToArray();
    public Brush Accent => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Descriptor.Color));
    public ObservableCollection<QuotaViewModel> Quotas { get; } = [];
    public UsageSnapshot? Snapshot { get; private set; }
    public IReadOnlyList<AvailableModel> Models => Snapshot?.Models ?? [];
    public ResetStatusViewModel? ResetStatus => Snapshot?.ResetStatus is { } status ? new(status) : null;
    public System.Windows.Visibility ResetStatusVisibility => ResetStatus != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    private string modelSearch = "";
    public string ModelSearch
    {
        get => modelSearch;
        set { if (modelSearch == value) return; modelSearch = value ?? ""; Change(); Change(nameof(FilteredModels)); Change(nameof(ModelsLabel)); }
    }
    public IReadOnlyList<AvailableModel> FilteredModels => Models.Where(m =>
        m.Name.Contains(modelSearch.Trim(), StringComparison.OrdinalIgnoreCase) ||
        m.Id.Contains(modelSearch.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
    public string ModelsLabel => modelSearch.Trim().Length > 0 ? $"使用可能なモデル ({FilteredModels.Count} / {Models.Count})"
        : Models.Count > 0 ? $"使用可能なモデル ({Models.Count})" : "使用可能なモデル";
    public string ModelsMessage => Snapshot?.ModelsMessage ?? (Models.Count == 0 ? "モデル一覧は未取得です。" : "");
    public System.Windows.Visibility ModelsMessageVisibility => ModelsMessage.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public IReadOnlyList<CapabilityItem> Skills => Snapshot?.Capabilities?.Skills ?? [];
    public IReadOnlyList<CapabilityItem> Plugins => Snapshot?.Capabilities?.Plugins ?? [];
    public IReadOnlyList<CapabilityItem> McpServers => Snapshot?.Capabilities?.McpServers ?? [];
    private string skillSearch = "";
    private string skillSource = "";
    public string SkillSearch
    {
        get => skillSearch;
        set { if (skillSearch == value) return; skillSearch = value ?? ""; Change(); RefreshSkills(); }
    }
    public bool AllSkills { get => skillSource == ""; set { if (value) SetSkillSource(""); } }
    public bool UserSkills { get => skillSource == "ユーザー"; set { if (value) SetSkillSource("ユーザー"); } }
    public bool PluginSkills { get => skillSource == "プラグイン提供"; set { if (value) SetSkillSource("プラグイン提供"); } }
    public IReadOnlyList<CapabilityItem> FilteredSkills => Skills.Where(s =>
        s.Name.Contains(skillSearch.Trim(), StringComparison.OrdinalIgnoreCase) &&
        (skillSource.Length == 0 || s.Detail == skillSource)).ToArray();
    public string SkillsLabel => skillSearch.Trim().Length == 0 && AllSkills
        ? $"スキル ({Skills.Count})" : $"スキル ({FilteredSkills.Count} / {Skills.Count})";
    private void SetSkillSource(string source)
    {
        if (skillSource == source) return;
        skillSource = source;
        Change(nameof(AllSkills)); Change(nameof(UserSkills)); Change(nameof(PluginSkills)); RefreshSkills();
    }
    private void RefreshSkills() { Change(nameof(FilteredSkills)); Change(nameof(SkillsLabel)); }
    private string pluginSearch = "";
    private string mcpSearch = "";
    public string PluginSearch
    {
        get => pluginSearch;
        set { if (pluginSearch == value) return; pluginSearch = value ?? ""; Change(); Change(nameof(FilteredPlugins)); Change(nameof(PluginsLabel)); }
    }
    public string McpSearch
    {
        get => mcpSearch;
        set { if (mcpSearch == value) return; mcpSearch = value ?? ""; Change(); Change(nameof(FilteredMcpServers)); Change(nameof(McpLabel)); }
    }
    public IReadOnlyList<CapabilityItem> FilteredPlugins => Plugins.Where(p => p.Name.Contains(pluginSearch.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
    public IReadOnlyList<CapabilityItem> FilteredMcpServers => McpServers.Where(m => m.Name.Contains(mcpSearch.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
    public string PluginsLabel => pluginSearch.Trim().Length == 0 ? $"プラグイン ({Plugins.Count})" : $"プラグイン ({FilteredPlugins.Count} / {Plugins.Count})";
    public string McpLabel => mcpSearch.Trim().Length == 0 ? $"MCP ({McpServers.Count})" : $"MCP ({FilteredMcpServers.Count} / {McpServers.Count})";
    public string CapabilitiesMessage => Snapshot?.Capabilities?.Message ?? "";
    public System.Windows.Visibility CapabilitiesMessageVisibility => CapabilitiesMessage.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public string Status => Snapshot?.Status switch { UsageStatus.Ready => "接続済み", UsageStatus.Stale => "前回の値", UsageStatus.AuthenticationRequired => Descriptor.Id == "claude" ? "未接続" : "要ログイン", UsageStatus.Error => "更新失敗", _ => "受信待ち" };
    public string Detail => Descriptor.Id == "claude" && Snapshot?.Status == UsageStatus.AuthenticationRequired ? "" : Snapshot?.Message ?? "";
    public string Plan => $"プラン / SKU：{(string.IsNullOrWhiteSpace(Snapshot?.Plan) ? "取得不可" : Snapshot.Plan)}";
    public string Updated => Snapshot?.ReceivedAt is { } time && time != DateTimeOffset.MinValue ? $"更新 {time.ToLocalTime():M/d HH:mm:ss}" : "";
    public System.Windows.Visibility UpdatedVisibility => Updated.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public void Apply(UsageSnapshot snapshot)
    {
        Snapshot = snapshot; Quotas.Clear();
        Change(nameof(Models)); Change(nameof(ModelsLabel)); Change(nameof(ModelsMessage)); Change(nameof(ModelsMessageVisibility));
        Change(nameof(FilteredModels));
        Change(nameof(Skills)); Change(nameof(Plugins)); Change(nameof(McpServers)); Change(nameof(SkillsLabel)); Change(nameof(PluginsLabel)); Change(nameof(McpLabel));
        RefreshSkills();
        Change(nameof(FilteredPlugins)); Change(nameof(FilteredMcpServers));
        Change(nameof(CapabilitiesMessage)); Change(nameof(CapabilitiesMessageVisibility));
        Change(nameof(DetailLocations));
        Change(nameof(ResetStatus)); Change(nameof(ResetStatusVisibility));
        foreach (var q in snapshot.Windows.Where(q => !q.IsSupplemental)) Quotas.Add(new(q, Descriptor.Id != "copilot"));
        Change(nameof(Status)); Change(nameof(Detail)); Change(nameof(Updated)); Change(nameof(UpdatedVisibility)); Change(nameof(Plan));
    }
}
