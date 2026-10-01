using System.Windows;
using AiUsageWidget.App;
using Xunit;

namespace AiUsageWidget.App.Tests;
public sealed class GpuViewModelTests
{
    [Fact] public void MissingDedicatedUsageIsOmittedWithoutInventingZero()
    {
        var row = new GpuViewModel(0, new("id", "Adapter", null, null, 12UL << 30, null));
        Assert.Empty(row.MemoryUsage); Assert.Empty(row.MemoryAmount); Assert.Empty(row.MemoryKind);
        Assert.Equal(Visibility.Collapsed, row.MemoryBarVisibility);
        Assert.Equal(Visibility.Collapsed, row.MemoryAmountVisibility);
        Assert.Equal(Visibility.Collapsed, row.UsageBarVisibility);
    }
    [Fact] public void MeasuredZeroRemainsVisible()
    {
        var row = new GpuViewModel(0, new("id", "Adapter", 0, 0, 12UL << 30, null));
        Assert.Equal("0%", row.Usage); Assert.Equal("0%", row.MemoryUsage);
        Assert.Equal(Visibility.Visible, row.MemoryAmountVisibility);
    }
    [Fact] public void DedicatedMemoryShowsPercentAndCapacity()
    {
        var row = new GpuViewModel(1, new("id", "Adapter", 32, 3UL << 30, 12UL << 30, 0));
        Assert.Equal("GPU 1", row.Label); Assert.Equal("25%", row.MemoryUsage);
        Assert.Equal(Visibility.Visible, row.MemoryBarVisibility);
        Assert.Contains(" / ", row.MemoryAmount); Assert.Equal("専用", row.MemoryKind);
    }
    [Fact] public void SharedMemoryDoesNotInventPercentage()
    {
        var row = new GpuViewModel(0, new("id", "Integrated", null, null, 0, 1UL << 30));
        Assert.Equal("", row.MemoryUsage); Assert.StartsWith("共有 ", row.MemoryAmount);
        Assert.Equal(Visibility.Collapsed, row.MemoryBarVisibility); Assert.Equal("", row.Usage);
    }
}
