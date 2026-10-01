using System;
using System.Linq;
using System.Threading;
using AiUsageWidget.App;
using Xunit;
using Xunit.Abstractions;

namespace AiUsageWidget.App.Tests;
public sealed class GpuUsageTests
{
    private readonly ITestOutputHelper output;
    public GpuUsageTests(ITestOutputHelper output) => this.output = output;
    private const string Id = "luid_0x00000000_0x00001234";
    [Fact] public void BusiestEngineCombinesProcessesWithoutCombiningConcurrentEngines()
    {
        Assert.Equal(75d, GpuUsageReader.CalculateUtilization(Id, new[] {
            new GpuUsageReader.CounterSample("pid_1_luid_0x00000000_0x00001234_phys_0_eng_0_engtype_3D", 40),
            new GpuUsageReader.CounterSample("pid_2_luid_0x00000000_0x00001234_phys_0_eng_0_engtype_3D", 35),
            new GpuUsageReader.CounterSample("pid_1_luid_0x00000000_0x00001234_phys_0_eng_1_engtype_Copy", 60),
            new GpuUsageReader.CounterSample("pid_1_luid_0x00000000_0x00005678_phys_0_eng_0_engtype_3D", 100) }));
    }
    [Fact] public void InvalidMissingAndOverflowingCountersHaveExplicitSemantics()
    {
        Assert.Null(GpuUsageReader.CalculateUtilization(Id, Array.Empty<GpuUsageReader.CounterSample>()));
        Assert.Null(GpuUsageReader.CalculateUtilization(Id, new[] { new GpuUsageReader.CounterSample("luid_0x0_0x1234_phys_0_eng_0", double.NaN) }));
        Assert.Equal(100d, GpuUsageReader.CalculateUtilization(Id, new[] { new GpuUsageReader.CounterSample("luid_0x0_0x1234_phys_0_eng_0", 120) }));
    }
    [Fact] public void NativeReaderHasStableAdapterIdentityAndResetBaseline()
    {
        using var reader = new GpuUsageReader();
        var initial = reader.Read();
        Assert.All(initial, gpu => Assert.Null(gpu.UtilizationPercent));
        Thread.Sleep(150);
        var next = reader.Read();
        foreach (var gpu in next) output.WriteLine($"GPU: {gpu.Name}; usage={gpu.UtilizationPercent}; dedicated={gpu.DedicatedUsedBytes}/{gpu.DedicatedTotalBytes}; shared={gpu.SharedUsedBytes}");
        Assert.Equal(initial.Select(g => g.Id), next.Select(g => g.Id));
        Assert.All(next, gpu => { Assert.NotEmpty(gpu.Name); if (gpu.UtilizationPercent is { } value) Assert.InRange(value, 0, 100); });
        reader.Reset();
        Assert.All(reader.Read(), gpu => Assert.Null(gpu.UtilizationPercent));
        reader.Dispose();
        Assert.Empty(reader.Read());
    }
}
