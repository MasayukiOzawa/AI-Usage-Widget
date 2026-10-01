using System.Threading;
using AiUsageWidget.App;
using Xunit;

namespace AiUsageWidget.App.Tests;
public sealed class SystemUsageTests
{
    [Fact] public void CpuSubtractsIdleFromKernelAndUserTotal()
    {
        Assert.Equal(25d, SystemUsage.CalculateCpu(new(100, 200, 100), new(400, 500, 200)));
        Assert.Equal(0d, SystemUsage.CalculateCpu(new(0, 0, 0), new(100, 100, 0)));
        Assert.Equal(100d, SystemUsage.CalculateCpu(new(0, 0, 0), new(0, 100, 100)));
    }
    [Fact] public void InvalidOrResetCountersAreUnknown()
    {
        Assert.Null(SystemUsage.CalculateCpu(new(100, 200, 100), new(99, 200, 100)));
        Assert.Null(SystemUsage.CalculateCpu(new(0, 0, 0), new(100, 50, 0)));
        Assert.Null(SystemUsage.CalculateCpu(new(1, 2, 3), new(1, 2, 3)));
    }
    [Fact] public void WindowsCountersReturnLiveMemoryAndCpuAndResetBaseline()
    {
        var usage = new SystemUsage();
        usage.Update(); Assert.Equal("—", usage.CpuText);
        Assert.InRange(usage.MemoryPercent, 0, 100); Assert.Contains("GB", usage.MemoryDetail);
        Thread.Sleep(100); usage.Update();
        Assert.EndsWith("%", usage.CpuText); Assert.InRange(usage.CpuPercent, 0, 100);
        usage.Reset(); usage.Update(); Assert.Equal("—", usage.CpuText);
    }
}
