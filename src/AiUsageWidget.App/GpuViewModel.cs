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
    public string DedicatedAmount { get; }
    public string SharedAmount { get; }
    public Visibility DedicatedVisibility => DedicatedAmount.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SharedVisibility => SharedAmount.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    public GpuViewModel(int index, GpuSnapshot snapshot)
    {
        Label = $"GPU {index}"; Name = snapshot.Name;
        Usage = snapshot.UtilizationPercent is { } percent ? $"{percent:0}%" : "";
        Used = snapshot.UtilizationPercent ?? 0;
        UsageBarVisibility = snapshot.UtilizationPercent.HasValue ? Visibility.Visible : Visibility.Collapsed;
        DedicatedAmount = Amount(snapshot.DedicatedUsedBytes, snapshot.DedicatedTotalBytes);
        SharedAmount = Amount(snapshot.SharedUsedBytes, snapshot.SharedTotalBytes);
    }
    private static string Amount(ulong? used, ulong? total) => used.HasValue && total is > 0
        ? $"{used.Value / 1073741824d:0.0} / {total.Value / 1073741824d:0.0} GB" : "";
}
