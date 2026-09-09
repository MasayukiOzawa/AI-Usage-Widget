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
    public System.Windows.Visibility ResetVisibility => showReset ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public string Reset => quota.Unlimited ? "制限なし" : quota.ResetsAt is not { } reset ? "リセット時刻：取得不可" : reset <= DateTimeOffset.UtcNow ? "リセット確認待ち" : $"{reset.ToLocalTime():M/d HH:mm} にリセット · あと {Duration(reset - DateTimeOffset.UtcNow)}";
    private static string Duration(TimeSpan span) => span.TotalDays >= 1 ? $"{(int)span.TotalDays}日 {span.Hours}時間" : span.TotalHours >= 1 ? $"{(int)span.TotalHours}時間 {span.Minutes}分" : $"{Math.Max(1, (int)span.TotalMinutes)}分";
}
public sealed class ProviderViewModel(ProviderDescriptor descriptor) : Observable
{
    public ProviderDescriptor Descriptor { get; } = descriptor;
    public string Name => Descriptor.Name;
    public IReadOnlyList<CapabilityLocation> DetailLocations { get; } = CapabilityLocations.For(descriptor.Id);
    public Brush Accent => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Descriptor.Color));
    public ObservableCollection<QuotaViewModel> Quotas { get; } = [];
    public ObservableCollection<QuotaViewModel> AdditionalQuotas { get; } = [];
    public string AdditionalLabel => $"その他の枠 ({AdditionalQuotas.Count})";
    public System.Windows.Visibility AdditionalVisibility => AdditionalQuotas.Count > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public UsageSnapshot? Snapshot { get; private set; }
    public IReadOnlyList<AvailableModel> Models => Snapshot?.Models ?? [];
    public string ModelsLabel => Models.Count > 0 ? $"使用可能なモデル ({Models.Count})" : "使用可能なモデル";
    public string ModelsMessage => Snapshot?.ModelsMessage ?? (Models.Count == 0 ? "モデル一覧は未取得です。" : "");
    public System.Windows.Visibility ModelsMessageVisibility => ModelsMessage.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public IReadOnlyList<CapabilityItem> Skills => Snapshot?.Capabilities?.Skills ?? [];
    public IReadOnlyList<CapabilityItem> Plugins => Snapshot?.Capabilities?.Plugins ?? [];
    public IReadOnlyList<CapabilityItem> McpServers => Snapshot?.Capabilities?.McpServers ?? [];
    public string SkillsLabel => $"スキル ({Skills.Count})";
    public string PluginsLabel => $"プラグイン ({Plugins.Count})";
    public string McpLabel => $"MCP ({McpServers.Count})";
    public string CapabilitiesMessage => Snapshot?.Capabilities?.Message ?? "";
    public System.Windows.Visibility CapabilitiesMessageVisibility => CapabilitiesMessage.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public string Status => Snapshot?.Status switch { UsageStatus.Ready => "接続済み", UsageStatus.Stale => "前回の値", UsageStatus.AuthenticationRequired => Descriptor.Id == "claude" ? "未接続" : "要ログイン", UsageStatus.Error => "更新失敗", _ => "受信待ち" };
    public string Detail => Descriptor.Id == "claude" && Snapshot?.Status == UsageStatus.AuthenticationRequired ? "" : Snapshot?.Message ?? "";
    public string Plan => $"プラン / SKU：{(string.IsNullOrWhiteSpace(Snapshot?.Plan) ? "取得不可" : Snapshot.Plan)}";
    public string Updated => Snapshot?.ReceivedAt is { } time && time != DateTimeOffset.MinValue ? $"更新 {time.ToLocalTime():M/d HH:mm:ss}" : "";
    public System.Windows.Visibility UpdatedVisibility => Updated.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public void Apply(UsageSnapshot snapshot)
    {
        Snapshot = snapshot; Quotas.Clear(); AdditionalQuotas.Clear();
        Change(nameof(Models)); Change(nameof(ModelsLabel)); Change(nameof(ModelsMessage)); Change(nameof(ModelsMessageVisibility));
        Change(nameof(Skills)); Change(nameof(Plugins)); Change(nameof(McpServers)); Change(nameof(SkillsLabel)); Change(nameof(PluginsLabel)); Change(nameof(McpLabel));
        Change(nameof(CapabilitiesMessage)); Change(nameof(CapabilitiesMessageVisibility));
        foreach (var q in snapshot.Windows) (q.IsSupplemental ? AdditionalQuotas : Quotas).Add(new(q, Descriptor.Id != "copilot"));
        Change(nameof(Status)); Change(nameof(Detail)); Change(nameof(Updated)); Change(nameof(UpdatedVisibility)); Change(nameof(AdditionalLabel)); Change(nameof(AdditionalVisibility)); Change(nameof(Plan));
    }
}
