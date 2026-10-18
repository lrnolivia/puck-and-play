using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace PuckPlay;

// Exercise the same topmost-window conflict as Windows' tray overflow panel.
internal static class MenuStacking
{
    sealed class TrayPanel : FluentWindow
    {
        internal TrayPanel() : base("Tray overflow stacking check", 320, 220)
        {
            Body.Content = Text("Topmost tray panel fixture", 16);
        }
    }
    sealed class NativeWindow
    {
        public long Handle { get; init; }
        public string Class { get; init; } = "";
        public bool Topmost { get; init; }
    }
    delegate bool Enumerate(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] static extern bool EnumWindows(Enumerate callback, IntPtr state);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern int GetWindowLongW(IntPtr window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr window, StringBuilder text, int capacity);

    static List<NativeWindow> VisibleWindows()
    {
        var windows = new List<NativeWindow>();
        // EnumWindows visits desktop windows in front-to-back order.
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out uint process);
            if (process == Environment.ProcessId && IsWindowVisible(window))
            {
                var name = new StringBuilder(256);
                GetClassNameW(window, name, name.Capacity);
                windows.Add(new NativeWindow { Handle = window.ToInt64(), Class = name.ToString(), Topmost = (GetWindowLongW(window, -20) & 8) != 0 });
            }
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    internal static async Task Verify(MenuWindow menu, string directory)
    {
        var bounds = DisplayArea.Primary.WorkArea;
        var fixture = new TrayPanel();
        fixture.AppWindow.Move(new PointInt32(bounds.X + bounds.Width - 420, bounds.Y + bounds.Height - 250));
        ((OverlappedPresenter)fixture.AppWindow.Presenter).IsAlwaysOnTop = true;
        long fixtureHandle = WinRT.Interop.WindowNative.GetWindowHandle(fixture).ToInt64();
        var results = new List<object>();
        try
        {
            for (int cycle = 0; cycle < 3; cycle++)
            {
                fixture.Activate();
                await Task.Delay(100);
                var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnOpened(object? sender, object args) => opened.TrySetResult();
                void OnClosed(object? sender, object args) => closed.TrySetResult();
                menu.Flyout.Opened += OnOpened;
                menu.Flyout.Closed += OnClosed;
                try
                {
                    menu.Open(bounds.X + bounds.Width - 40, bounds.Y + bounds.Height - 10);
                    await opened.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    await Task.Delay(250);
                    var windows = VisibleWindows();
                    int popup = windows.FindIndex(window => window.Class == "Microsoft.UI.Content.PopupWindowSiteBridge");
                    int panel = windows.FindIndex(window => window.Handle == fixtureHandle);
                    bool above = popup >= 0 && panel >= 0 && popup < panel;
                    results.Add(new { Cycle = cycle, AboveTopmostPanel = above, Windows = windows });
                    await File.WriteAllTextAsync(Path.Combine(directory, "menu-stacking.json"), System.Text.Json.JsonSerializer.Serialize(results));
                    if (cycle == 0)
                        await MenuCapture.Save(bounds.X + bounds.Width - 900, bounds.Y + bounds.Height - 800, 900, 800, Path.Combine(directory, "menu-over-topmost-panel.png"));
                    if (!above || !windows[panel].Topmost || !windows[popup].Topmost)
                        throw new InvalidOperationException("The tray menu was behind an overlapping topmost panel.");
                }
                finally
                {
                    menu.Flyout.Opened -= OnOpened;
                    menu.Flyout.Hide();
                    await closed.Task.WaitAsync(TimeSpan.FromSeconds(2));
                    menu.Flyout.Closed -= OnClosed;
                }
                if (menu.AppWindow.IsVisible || ((OverlappedPresenter)menu.AppWindow.Presenter).IsAlwaysOnTop)
                    throw new InvalidOperationException("The dismissed tray menu retained a visible or topmost host.");
            }
        }
        finally { fixture.Close(); }
    }
}
