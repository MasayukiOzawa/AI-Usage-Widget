using System.Windows;
using AiUsageWidget.App;
using Xunit;

namespace AiUsageWidget.App.Tests;
public sealed class GpuViewModelTests
{
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
        Assert.Equal("取得不可", row.MemoryUsage); Assert.StartsWith("共有 ", row.MemoryAmount);
        Assert.Equal(Visibility.Hidden, row.MemoryBarVisibility); Assert.Equal("取得不可", row.Usage);
    }
}
