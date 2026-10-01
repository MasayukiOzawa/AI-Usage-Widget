using System;
using System.Windows;

namespace AiUsageWidget.App;

public sealed class GpuViewModel
{
    public string Label { get; }
    public string Name { get; }
    public string Usage { get; }
    public double Used { get; }
    public Visibility UsageBarVisibility { get; }
    public string MemoryUsage { get; }
    public double MemoryUsed { get; }
    public Visibility MemoryBarVisibility { get; }
    public string MemoryAmount { get; }
    public string MemoryKind { get; }
    public string SharedAmount { get; }
    public Visibility SharedVisibility { get; }
    public GpuViewModel(int index, GpuSnapshot snapshot)
    {
        Label = $"GPU {index}"; Name = snapshot.Name;
        Usage = snapshot.UtilizationPercent is { } percent ? $"{percent:0}%" : "取得不可";
        Used = snapshot.UtilizationPercent ?? 0;
        UsageBarVisibility = snapshot.UtilizationPercent.HasValue ? Visibility.Visible : Visibility.Hidden;
        var total = snapshot.DedicatedTotalBytes;
        var used = snapshot.DedicatedUsedBytes;
        var dedicated = total is > 0;
        var memoryPercent = dedicated && used.HasValue ? Math.Clamp(100d * used.Value / total!.Value, 0, 100) : (double?)null;
        MemoryUsage = memoryPercent is { } value ? $"{value:0}%" : "取得不可";
        MemoryUsed = memoryPercent ?? 0;
        MemoryBarVisibility = memoryPercent.HasValue ? Visibility.Visible : Visibility.Hidden;
        MemoryAmount = dedicated
            ? $"{(used.HasValue ? Gb(used.Value) : "—")} / {Gb(total!.Value)} GB"
            : snapshot.SharedUsedBytes is { } shared ? $"共有 {Gb(shared)} GB" : "取得不可";
        MemoryKind = dedicated ? "専用" : "";
        SharedAmount = dedicated && snapshot.SharedUsedBytes is { } bytes ? $"共有 {Gb(bytes)} GB" : "";
        SharedVisibility = dedicated && snapshot.SharedUsedBytes is > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private static string Gb(ulong bytes) => (bytes / 1073741824d).ToString("0.0");
}
