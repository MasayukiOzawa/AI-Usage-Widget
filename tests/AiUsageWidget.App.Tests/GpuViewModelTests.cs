using System.Windows;
using AiUsageWidget.App;
using Xunit;
namespace AiUsageWidget.App.Tests;
public sealed class GpuViewModelTests
{
    [Fact] public void DedicatedAndSharedShowUsedAndMaximum()
    {
        var row = new GpuViewModel(1, new("id", "Adapter", 32, 2UL << 30, 8UL << 30, 1UL << 30, 32UL << 30));
        Assert.Equal("GPU 1", row.Label);
        Assert.Equal("2.0 / 8.0 GB", row.DedicatedAmount);
        Assert.Equal("1.0 / 32.0 GB", row.SharedAmount);
    }
    [Theory][InlineData(null)][InlineData(0UL)]
    public void MissingOrZeroDedicatedCapacityHidesDedicated(ulong? total)
    {
        var row = new GpuViewModel(0, new("id", "Integrated", null, 0, total, 0, 32UL << 30));
        Assert.Equal(Visibility.Collapsed, row.DedicatedVisibility);
        Assert.Equal(Visibility.Visible, row.SharedVisibility);
        Assert.Equal("0.0 / 32.0 GB", row.SharedAmount);
    }
    [Fact] public void MissingUsageOrSharedCapacityIsHidden()
    {
        var row = new GpuViewModel(0, new("id", "Adapter", null, null, 8UL << 30, 0));
        Assert.Equal(Visibility.Collapsed, row.DedicatedVisibility);
        Assert.Equal(Visibility.Collapsed, row.SharedVisibility);
        Assert.Equal(Visibility.Collapsed, row.UsageBarVisibility);
    }
    [Fact] public void MeasuredZeroDedicatedUsageRemainsVisible()
    {
        var row = new GpuViewModel(0, new("id", "Adapter", 0, 0, 8UL << 30, null));
        Assert.Equal("0%", row.Usage);
        Assert.Equal("0.0 / 8.0 GB", row.DedicatedAmount);
        Assert.Equal(Visibility.Visible, row.DedicatedVisibility);
    }
}
