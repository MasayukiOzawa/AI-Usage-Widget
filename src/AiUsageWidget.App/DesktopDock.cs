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
    private NativeRect approvedBounds;
    private double minimumWidth, minimumHeight;
    public event Action<string>? Failed;
    private readonly Func<IntPtr, uint, bool> registerAppBar;
    public bool Enabled { get; private set; }
    public DesktopDock(Window window, Func<IntPtr, uint, bool>? registerAppBar = null)
    {
        this.window = window;
        handle = new WindowInteropHelper(window).Handle;
        this.registerAppBar = registerAppBar ?? ((hwnd, message) =>
        {
            var data = Data(); data.Window = hwnd; data.Callback = message;
            return SHAppBarMessage(0, ref data) != UIntPtr.Zero;
        });
        HwndSource.FromHwnd(handle)?.AddHook(Hook);
    }
    public bool SetEnabled(bool enabled)
    {
        if (!enabled) { Disable(); return true; }
        if (enabled && !Enabled)
        {
            minimumWidth = window.MinWidth; minimumHeight = window.MinHeight;
            window.MinWidth = 0; window.MinHeight = 0;
        }
        Enabled = enabled;
        if (enabled && monitor == null) monitor = System.Windows.Forms.Screen.FromHandle(handle).DeviceName;
        // Validate registration even before the window is visible, before a caller persists the option.
        if (enabled && !Register()) { Disable(); return false; }
        return Refresh(false);
    }
    public bool Refresh(bool reportFailure = true)
    {
        if (disposed) return false;
        if (!Enabled || !window.IsVisible || window.WindowState == WindowState.Minimized)
        {
            Remove(); return true;
        }
        try
        {
            if (!Register()) throw new InvalidOperationException("デスクトップへの固定を登録できませんでした。");
            Position(); return true;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            Disable();
            if (reportFailure) Failed?.Invoke(error.Message);
            return false;
        }
    }
    private bool Register()
    {
        if (!registered)
        {
            registered = registerAppBar(handle, callback);
        }
        return registered;
    }
    private void Disable()
    {
        Remove(); monitor = null;
        if (Enabled) { window.MinWidth = minimumWidth; window.MinHeight = minimumHeight; }
        Enabled = false;
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
            if (width <= 0 || data.Rect.Bottom <= data.Rect.Top)
                throw new InvalidOperationException("デスクトップに固定できる領域がありません。");
            SHAppBarMessage(3, ref data);
            approvedBounds = data.Rect;
            // Keep WPF's layout properties in sync; a later layout pass must not restore the floating size.
            var dpi = VisualTreeHelper.GetDpi(window);
            window.Left = data.Rect.Left / dpi.DpiScaleX;
            window.Top = data.Rect.Top / dpi.DpiScaleY;
            window.Width = (data.Rect.Right - data.Rect.Left) / dpi.DpiScaleX;
            window.Height = (data.Rect.Bottom - data.Rect.Top) / dpi.DpiScaleY;
            if (!SetWindowPos(handle, IntPtr.Zero, data.Rect.Left, data.Rect.Top,
                data.Rect.Right - data.Rect.Left, data.Rect.Bottom - data.Rect.Top, 0x0014))
                throw new InvalidOperationException("固定ウィンドウの位置を設定できませんでした。");
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
        else if (registered && message == 0x0047 && !positioning)
        {
            var data = Data(); SHAppBarMessage(9, ref data);
            if (window.WindowState != WindowState.Minimized && GetWindowRect(handle, out var actual)
                && (actual.Left != approvedBounds.Left || actual.Top != approvedBounds.Top
                    || actual.Right != approvedBounds.Right || actual.Bottom != approvedBounds.Bottom)) QueueRefresh();
        }
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
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
