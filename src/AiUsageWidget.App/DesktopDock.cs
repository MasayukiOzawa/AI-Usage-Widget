using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace AiUsageWidget.App;

/// <summary>Reserves the right edge through the shell instead of changing the global work area.</summary>
internal sealed class DesktopDock : IDisposable
{
    private readonly Window window;
    private readonly IntPtr handle;
    private readonly uint callback = RegisterWindowMessage("AiUsageWidget.AppBar");
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private bool registered, positioning, queued, disposed;
    private string? monitor;
    public bool Enabled { get; private set; }
    public DesktopDock(Window window)
    {
        this.window = window;
        handle = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(Hook);
    }
    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (enabled && monitor == null) monitor = System.Windows.Forms.Screen.FromHandle(handle).DeviceName;
        Refresh();
        if (!enabled) monitor = null;
    }
    public void Refresh()
    {
        if (disposed) return;
        if (!Enabled || !window.IsVisible || window.WindowState == WindowState.Minimized)
        {
            Remove(); return;
        }
        if (!registered)
        {
            var data = Data(); data.Callback = callback;
            registered = SHAppBarMessage(0, ref data) != UIntPtr.Zero;
            if (!registered) throw new InvalidOperationException("デスクトップへの固定を登録できませんでした。");
        }
        Position();
    }
    private void Position()
    {
        if (!registered || positioning) return;
        positioning = true;
        try
        {
            var screen = Array.Find(System.Windows.Forms.Screen.AllScreens, s => s.DeviceName == monitor)
                ?? System.Windows.Forms.Screen.FromHandle(handle);
            monitor = screen.DeviceName;
            var bounds = screen.Bounds;
            var width = Math.Min((int)Math.Round(360 * VisualTreeHelper.GetDpi(window).DpiScaleX), bounds.Width / 2);
            var data = Data(); data.Edge = 2;
            data.Rect = new NativeRect { Left = bounds.Right - width, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
            SHAppBarMessage(2, ref data);
            data.Rect.Left = data.Rect.Right - width;
            SHAppBarMessage(3, ref data);
            SetWindowPos(handle, IntPtr.Zero, data.Rect.Left, data.Rect.Top,
                data.Rect.Right - data.Rect.Left, data.Rect.Bottom - data.Rect.Top, 0x0014);
        }
        finally { positioning = false; }
    }
    private void QueueRefresh()
    {
        if (queued || disposed) return;
        queued = true;
        window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            queued = false; if (!disposed) Refresh();
        }));
    }
    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == taskbarCreated) { registered = false; QueueRefresh(); }
        else if ((uint)message == callback && wParam.ToInt32() == 1 && !positioning) QueueRefresh();
        else if (message is 0x007E or 0x02E0) QueueRefresh(); // display / DPI change
        else if (registered && message == 0x0006) { var data = Data(); SHAppBarMessage(6, ref data); }
        else if (registered && message == 0x0047 && !positioning) { var data = Data(); SHAppBarMessage(9, ref data); }
        return IntPtr.Zero;
    }
    private AppBarData Data() => new() { Size = (uint)Marshal.SizeOf<AppBarData>(), Window = handle };
    private void Remove()
    {
        if (!registered) return;
        registered = false; var data = Data(); SHAppBarMessage(1, ref data);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Remove(); HwndSource.FromHwnd(handle)?.RemoveHook(Hook);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct AppBarData
    {
        public uint Size; public IntPtr Window; public uint Callback, Edge; public NativeRect Rect; public IntPtr Parameter;
    }
    [DllImport("shell32.dll")] private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
