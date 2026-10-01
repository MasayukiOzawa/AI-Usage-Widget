using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using AiUsageWidget.App;
using Xunit;

namespace AiUsageWidget.App.Tests;
public sealed class DesktopDockTests
{
    [Fact] public void RegistrationFailureRollsBackMinimumsAndEnabledState() => Sta(() =>
    {
        var window = CreateWindow();
        try
        {
            using var dock = new DesktopDock(window, (_, _) => false);
            Assert.False(dock.SetEnabled(true)); Assert.False(dock.Enabled);
            Assert.Equal(340, window.MinWidth); Assert.Equal(340, window.MinHeight);
        }
        finally { window.Close(); }
    });
    [Fact] public void HiddenDockValidatesRegistrationAndRestoresMinimumsOnDisable() => Sta(() =>
    {
        var window = CreateWindow(); var calls = 0;
        try
        {
            using var dock = new DesktopDock(window, (_, _) => { calls++; return true; });
            Assert.True(dock.SetEnabled(true)); Assert.Equal(1, calls);
            Assert.Equal(0, window.MinWidth); Assert.Equal(0, window.MinHeight);
            dock.SetEnabled(false);
            Assert.Equal(340, window.MinWidth); Assert.Equal(340, window.MinHeight);
        }
        finally { window.Close(); }
    });
    [Fact] public void EventRefreshReportsReregistrationFailureWithoutThrowing() => Sta(() =>
    {
        var window = CreateWindow(); var calls = 0; var failures = 0;
        try
        {
            using var dock = new DesktopDock(window, (_, _) => ++calls == 1);
            dock.Failed += _ => failures++;
            Assert.True(dock.SetEnabled(true)); window.Show();
            Assert.False(dock.Refresh()); Assert.False(dock.Enabled); Assert.Equal(1, failures);
            Assert.Equal(340, window.MinWidth); Assert.Equal(340, window.MinHeight);
            Assert.True(dock.Refresh()); Assert.Equal(1, failures);
        }
        finally { window.Close(); }
    });
    private static Window CreateWindow()
    {
        var window = new Window { Width = 360, Height = 400, MinWidth = 340, MinHeight = 340, ShowInTaskbar = false };
        new WindowInteropHelper(window).EnsureHandle(); return window;
    }
    private static void Sta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception caught) { error = caught; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
