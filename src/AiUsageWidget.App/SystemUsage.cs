using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AiUsageWidget.App;

// Windows system counters; no agent requests or performance-counter dependencies.
public sealed class SystemUsage : INotifyPropertyChanged
{
    private CpuTimes? previous;
    public double CpuPercent { get; private set; }
    public double MemoryPercent { get; private set; }
    public string CpuText { get; private set; } = "—";
    public string MemoryText { get; private set; } = "—";
    public string MemoryAmount { get; private set; } = "—";
    public string MemoryDetail { get; private set; } = "物理メモリ使用量";
    public event PropertyChangedEventHandler? PropertyChanged;

    public void Reset() => previous = null;
    public void Update()
    {
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var current = new CpuTimes(idle, kernel, user);
            var percent = previous is { } last ? CalculateCpu(last, current) : null;
            previous = current;
            CpuPercent = percent ?? 0;
            CpuText = percent is { } value ? $"{value:0}%" : "—";
        }
        else { Reset(); CpuPercent = 0; CpuText = "取得不可"; }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0 && memory.AvailablePhysical <= memory.TotalPhysical)
        {
            var used = memory.TotalPhysical - memory.AvailablePhysical;
            MemoryPercent = 100d * used / memory.TotalPhysical;
            MemoryText = $"{MemoryPercent:0}%";
            MemoryAmount = $"{used / 1073741824d:0.0} / {memory.TotalPhysical / 1073741824d:0.0} GB";
            MemoryDetail = $"物理メモリ使用量: {MemoryAmount}";
        }
        else { MemoryPercent = 0; MemoryText = "取得不可"; MemoryAmount = "取得不可"; MemoryDetail = "物理メモリ使用量: 取得不可"; }
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    public readonly record struct CpuTimes(ulong Idle, ulong Kernel, ulong User);
    public static double? CalculateCpu(CpuTimes before, CpuTimes after)
    {
        if (after.Idle < before.Idle || after.Kernel < before.Kernel || after.User < before.User) return null;
        // Kernel includes idle time. Use double to avoid overflowing summed counters.
        var total = (double)(after.Kernel - before.Kernel) + (after.User - before.User);
        var idle = (double)(after.Idle - before.Idle);
        return total <= 0 || idle > total ? null : Math.Clamp(100 * (total - idle) / total, 0, 100);
    }
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
}
