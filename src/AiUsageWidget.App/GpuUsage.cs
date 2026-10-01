using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AiUsageWidget.App;

public sealed record GpuSnapshot(string Id, string Name, double? UtilizationPercent,
    ulong? DedicatedUsedBytes, ulong? DedicatedTotalBytes, ulong? SharedUsedBytes, ulong? SharedTotalBytes = null);

// Read on a worker thread: driver enumeration and PDH can take time. LUIDs remain
// stable for the current Windows session, independently of adapter display order.
public sealed class GpuUsageReader : IDisposable
{
    private readonly object gate = new();
    private IntPtr query, engines, dedicated, shared;
    private bool disposed, sampled;
    private static readonly Regex Instance = new(@"luid_0x([0-9a-f]{1,8})_0x([0-9a-f]{1,8})_phys_(\d+)(?:_eng_(\d+))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public Task<IReadOnlyList<GpuSnapshot>> ReadAsync() => Task.Run(Read);
    public IReadOnlyList<GpuSnapshot> Read()
    {
        lock (gate)
        {
            if (disposed) return Array.Empty<GpuSnapshot>();
            var adapters = EnumerateAdapters();
            if (query == IntPtr.Zero && PdhOpenQueryW(null, UIntPtr.Zero, out query) == 0)
            {
                Add(@"\GPU Engine(*)\Utilization Percentage", out engines);
                Add(@"\GPU Adapter Memory(*)\Dedicated Usage", out dedicated);
                Add(@"\GPU Adapter Memory(*)\Shared Usage", out shared);
            }
            bool collected = query != IntPtr.Zero && PdhCollectQueryData(query) == 0;
            var engineValues = collected && sampled ? Values(engines) : new List<CounterSample>();
            var dedicatedValues = collected ? Values(dedicated) : new List<CounterSample>();
            var sharedValues = collected ? Values(shared) : new List<CounterSample>();
            sampled = collected;
            return adapters.Select(a => new GpuSnapshot(a.Id, a.Name,
                CalculateUtilization(a.Id, engineValues), Memory(a.Id, dedicatedValues), a.Total,
                Memory(a.Id, sharedValues), a.SharedTotal)).ToArray();
        }
    }
    public void Reset() { lock (gate) { sampled = false; } }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            if (query != IntPtr.Zero) PdhCloseQuery(query);
            query = engines = dedicated = shared = IntPtr.Zero;
        }
    }
    private void Add(string path, out IntPtr counter)
    {
        if (PdhAddEnglishCounterW(query, path, UIntPtr.Zero, out counter) != 0) counter = IntPtr.Zero;
    }
    public readonly record struct CounterSample(string InstanceName, double Value);
    public static double? CalculateUtilization(string adapterId, IEnumerable<CounterSample> samples)
    {
        var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var sample in samples)
        {
            var match = Instance.Match(sample.InstanceName);
            if (!match.Success || !match.Groups[4].Success || !Matches(adapterId, match) || !double.IsFinite(sample.Value) || sample.Value < 0) continue;
            // Multiple processes contribute to the same physical engine. Distinct
            // engines execute concurrently, so use the busiest rather than sum.
            string key = match.Groups[3].Value + ":" + match.Groups[4].Value;
            totals[key] = totals.GetValueOrDefault(key) + sample.Value;
        }
        return totals.Count == 0 ? null : Math.Clamp(totals.Values.Max(), 0, 100);
    }
    private static bool Matches(string id, Match match) => string.Equals(id,
        $"luid_0x{Convert.ToUInt32(match.Groups[1].Value, 16):x8}_0x{Convert.ToUInt32(match.Groups[2].Value, 16):x8}", StringComparison.OrdinalIgnoreCase);
    private static ulong? Memory(string id, List<CounterSample> samples)
    {
        double total = 0; bool found = false;
        foreach (var sample in samples)
        {
            var match = Instance.Match(sample.InstanceName);
            if (!match.Success || !Matches(id, match) || !double.IsFinite(sample.Value) || sample.Value < 0) continue;
            total += sample.Value; found = true;
        }
        return found && total < ulong.MaxValue ? (ulong)total : null;
    }
    private static List<CounterSample> Values(IntPtr counter)
    {
        var values = new List<CounterSample>();
        if (counter == IntPtr.Zero) return values;
        uint bytes = 0;
        const uint moreData = 0x800007D2;
        // NOCAP100 is necessary when processes are combined before clamping.
        const uint format = 0x200 | 0x8000;
        if (PdhGetFormattedCounterArrayW(counter, format, ref bytes, out _, IntPtr.Zero) != moreData || bytes == 0 || bytes > 64 * 1024 * 1024) return values;
        var buffer = Marshal.AllocHGlobal((int)bytes);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, format, ref bytes, out var count, buffer) != 0) return values;
            int size = Marshal.SizeOf<FormattedItem>();
            if ((ulong)count * (ulong)size > bytes) return values;
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<FormattedItem>(IntPtr.Add(buffer, i * size));
                if (item.Value.Status <= 1 && Marshal.PtrToStringUni(item.Name) is { } name)
                    values.Add(new(name, item.Value.Number));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return values;
    }
    private sealed record Adapter(string Id, string Name, ulong Total, ulong SharedTotal);
    private static List<Adapter> EnumerateAdapters()
    {
        var result = new List<Adapter>();
        Guid iid = new("770aae78-f26f-4dba-a829-253c83d1b387");
        if (CreateDXGIFactory1(ref iid, out var factory) < 0) return result;
        try
        {
            var enumerate = Method<EnumAdapters>(factory, 12);
            for (uint i = 0; i < 64; i++)
            {
                if (enumerate(factory, i, out var adapter) < 0) break;
                try
                {
                    if (Method<GetDesc>(adapter, 10)(adapter, out var desc) >= 0 && (desc.Flags & 2) == 0)
                        result.Add(new($"luid_0x{desc.High:x8}_0x{desc.Low:x8}", desc.Description.Trim(), desc.DedicatedVideo.ToUInt64(), desc.SharedSystem.ToUInt64()));
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return result;
    }
    private static T Method<T>(IntPtr instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapters(IntPtr self, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDesc(IntPtr self, out AdapterDescription description);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Vendor, Device, Subsystem, Revision;
        public UIntPtr DedicatedVideo, DedicatedSystem, SharedSystem;
        public uint Low, High, Flags;
    }
    [StructLayout(LayoutKind.Explicit, Size = 16)] private struct FormattedValue
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Number;
    }
    [StructLayout(LayoutKind.Sequential)] private struct FormattedItem { public IntPtr Name; public FormattedValue Value; }
    [DllImport("dxgi.dll", ExactSpelling = true)] private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint PdhOpenQueryW(string? source, UIntPtr data, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, UIntPtr data, out IntPtr counter);
    [DllImport("pdh.dll", ExactSpelling = true)] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", ExactSpelling = true)] private static extern uint PdhCloseQuery(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bytes, out uint count, IntPtr buffer);
}
