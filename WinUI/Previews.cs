using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SteamControllerGuard;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage;

namespace PuckPlay;

internal static class Previews
{
    internal static async Task VerifyRenderer(string directory)
    {
        Directory.CreateDirectory(directory);
        var window = new FluentWindow("Renderer checks", 32, 32);
        window.AppWindow.Move(new PointInt32(-12000, -12000));
        window.AppWindow.Show(false);
        var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(window.Root).Compositor;
        var process = System.Diagnostics.Process.GetCurrentProcess();
        int controlStart = 0;
        long controlBytes = 0;
        for (int cycle = 0; cycle < 30; cycle++)
        {
            Vortice.Direct3D11.D3D11.D3D11CreateDevice(IntPtr.Zero, Vortice.Direct3D.DriverType.Hardware, Vortice.Direct3D11.DeviceCreationFlags.BgraSupport,
                new[] { Vortice.Direct3D.FeatureLevel.Level_11_0 }, out var controlDevice, out var controlContext).CheckError();
            controlContext.ClearState();
            controlContext.Flush();
            controlContext.Dispose();
            controlDevice.Dispose();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(40);
            process.Refresh();
            if (cycle == 5)
            {
                controlStart = process.HandleCount;
                controlBytes = process.PrivateMemorySize64;
            }
        }
        process.Refresh();
        await File.WriteAllTextAsync(Path.Combine(directory, "device-control.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            Cycles = 30,
            HandleGrowth = process.HandleCount - controlStart,
            PrivateBytesGrowth = process.PrivateMemorySize64 - controlBytes
        }));
        using var sharedCanvas = new Microsoft.Graphics.Canvas.CanvasDevice();
        using var sharedGraphics = Microsoft.Graphics.Canvas.UI.Composition.CanvasComposition.CreateCompositionGraphicsDevice(compositor, sharedCanvas);
        for (int cycle = 0; cycle < 30; cycle++)
        {
            using (var drawingSurface = sharedGraphics.CreateDrawingSurface(new Windows.Foundation.Size(192, 192), Microsoft.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized, Microsoft.Graphics.DirectX.DirectXAlphaMode.Premultiplied))
            using (var visual = compositor.CreateSpriteVisual())
            using (var brush = compositor.CreateSurfaceBrush(drawingSurface))
            {
                visual.Brush = brush;
                visual.Size = new System.Numerics.Vector2(32, 32);
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(window.Root, visual);
                using (var drawing = Microsoft.Graphics.Canvas.UI.Composition.CanvasComposition.CreateDrawingSession(drawingSurface))
                    drawing.Clear(Microsoft.UI.Colors.Transparent);
                await Task.Delay(50);
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(window.Root, null);
                visual.Brush = null;
            }
            await compositor.RequestCommitAsync();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(80);
            process.Refresh();
            if (cycle == 5)
            {
                controlStart = process.HandleCount;
                controlBytes = process.PrivateMemorySize64;
            }
            await File.AppendAllTextAsync(Path.Combine(directory, "composition-control.jsonl"), System.Text.Json.JsonSerializer.Serialize(new
            {
                Cycle = cycle + 1,
                Handles = process.HandleCount,
                Bytes = process.PrivateMemorySize64,
                Threads = process.Threads.Count
            }) + Environment.NewLine);
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "composition-control.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            Cycles = 30,
            HandleGrowth = process.HandleCount - controlStart,
            PrivateBytesGrowth = process.PrivateMemorySize64 - controlBytes
        }));
        long baselineBytes = 0;
        int baselineHandles = 0;
        object? timing = null;
        // The packaged graphics runtime initializes native caches asynchronously.
        // A single long-lived mark exercises that startup before the lifecycle
        // measurement, so its one-time allocation isn't counted as per-mark growth.
        using (var startup = new CrystalRenderer(compositor, 192, error => Bridge.Log(error)))
        {
            startup.Update("green", true, false);
            for (int sample = 0; sample < 10; sample++)
            {
                await Task.Delay(1000);
                process.Refresh();
                await File.AppendAllTextAsync(Path.Combine(directory, "startup-control.jsonl"), System.Text.Json.JsonSerializer.Serialize(new { Sample = sample, Handles = process.HandleCount, Bytes = process.PrivateMemorySize64, Threads = process.Threads.Count, Modules = process.Modules.Count }) + Environment.NewLine);
            }
        }
        for (int cycle = 0; cycle < 24; cycle++)
        {
            Exception? failure = null;
            using (var renderer = new CrystalRenderer(compositor, 192, error => failure = error))
            {
                renderer.Visual.Size = new System.Numerics.Vector2(32, 32);
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(window.Root, renderer.Visual);
                renderer.Update("green", true, false);
                await Task.Delay(100);
                if (cycle == 23)
                    timing = await renderer.SampleFrames(2500);
                renderer.Update("red", false, false);
                await Task.Delay(80);
                long restingFrames = renderer.TotalFrames;
                for (int update = 0; update < 10; update++)
                    renderer.Update("red", false, false);
                await Task.Delay(80);
                if (renderer.TotalFrames != restingFrames)
                    throw new InvalidOperationException("An unchanged static mark was redrawn.");
                if (failure != null)
                    throw failure;
                Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(window.Root, null);
                renderer.Dispose();
            }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(80);
            process.Refresh();
            await File.AppendAllTextAsync(Path.Combine(directory, "stages.jsonl"), System.Text.Json.JsonSerializer.Serialize(new
            {
                Cycle = cycle + 1,
                Stage = "normal-disposed",
                Handles = process.HandleCount,
                Bytes = process.PrivateMemorySize64
            }) + Environment.NewLine);
            try
            {
                using var invalid = new CrystalRenderer(compositor, 192, _ => { }, 1.8f, "invalid shader source");
                throw new InvalidOperationException("The invalid shader was accepted.");
            }
            catch (Exception error) when (error is not InvalidOperationException) { }
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(80);
            process.Refresh();
            await File.AppendAllTextAsync(Path.Combine(directory, "stages.jsonl"), System.Text.Json.JsonSerializer.Serialize(new
            {
                Cycle = cycle + 1,
                Stage = "failed-disposed",
                Handles = process.HandleCount,
                Bytes = process.PrivateMemorySize64
            }) + Environment.NewLine);
            if (cycle == 5)
            {
                baselineBytes = process.PrivateMemorySize64;
                baselineHandles = process.HandleCount;
            }
            await File.AppendAllTextAsync(Path.Combine(directory, "cycles.jsonl"), System.Text.Json.JsonSerializer.Serialize(new
            {
                Cycle = cycle + 1,
                PrivateBytes = process.PrivateMemorySize64,
                Handles = process.HandleCount,
                BaselineBytes = baselineBytes,
                BaselineHandles = baselineHandles
            }) + Environment.NewLine);
            await File.WriteAllTextAsync(Path.Combine(directory, "progress.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                Cycle = cycle + 1,
                PrivateBytes = process.PrivateMemorySize64,
                Handles = process.HandleCount
            }));
        }
        process.Refresh();
        long growth = process.PrivateMemorySize64 - baselineBytes;
        int handles = process.HandleCount - baselineHandles;
        await File.WriteAllTextAsync(Path.Combine(directory, "renderer-lifecycle.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            Cycles = 24,
            FailureCycles = 24,
            PrivateBytesGrowth = growth,
            HandleGrowth = handles,
            StaticRedraws = 0,
            Timing = timing
        }));
        if (growth > 32 * 1024 * 1024 || handles > 12)
            throw new InvalidOperationException("Renderer resources did not settle after repeated disposal: " + growth + " bytes, " + handles + " handles.");
        window.Close();
    }
    internal static async Task ShowCrystal()
    {
        var mark = new CrystalStatusMark(false, true) { Width = 512, Height = 512, MotionEnabled = true, RequestedTheme = ElementTheme.Dark };
        mark.SetState("green");
        var layout = new StackPanel { Spacing = 12, Margin = new Thickness(20), HorizontalAlignment = HorizontalAlignment.Center };
        layout.Children.Add(mark);
        var note = FluentWindow.Text("Glossy crystal with a soft internal light", 14, true);
        note.TextAlignment = TextAlignment.Center;
        layout.Children.Add(note);
        var window = new ArtworkWindow(layout, 580, 620);
        window.Closed += delegate
        {
            mark.DisposeGraphics();
            Application.Current.Exit();
        };
        window.Root.RequestedTheme = ElementTheme.Dark;
        window.AppWindow.Show();
        window.Activate();
        await mark.EnsureReady();
        Directory.CreateDirectory(Bridge.UserRoot);
        await File.WriteAllTextAsync(Path.Combine(Bridge.UserRoot, "crystal-proof.json"), await mark.AnimationInfo());
        string study = Path.Combine(Bridge.UserRoot, "crystal-study");
        Directory.CreateDirectory(study);
        for (int frame = 0; frame < 4; frame++)
        {
            await Task.Delay(1600);
            await mark.Capture(Path.Combine(study, "crystal-" + frame + ".png"));
        }
        await mark.CaptureTransparent(Path.Combine(study, "crystal-transparent.png"));
        await File.WriteAllTextAsync(Path.Combine(study, "frame-timing.json"), await mark.SampleFrames());
    }
    sealed class ArtworkWindow : FluentWindow
    {
        internal ArtworkWindow(UIElement content, int width = 300, int height = 360) : base("Artwork", width, height) { Body.Content = content; }
    }
    internal static async Task ExportArtwork(string directory)
    {
        Directory.CreateDirectory(directory);
        var keeper = new FluentWindow("Artwork", 10, 10);
        keeper.AppWindow.Move(new PointInt32(-12000, -12000));
        keeper.AppWindow.Show(false);
        var renderer = new CrystalStatusMark(true, true) { Width = 96, Height = 96, MotionEnabled = false };
        var browserWindow = new ArtworkWindow(renderer);
        browserWindow.AppWindow.Move(new PointInt32(-12000, -12000));
        browserWindow.AppWindow.Show(false);
        await renderer.EnsureReady();
        string installedAssets = Path.Combine(AppContext.BaseDirectory, "Assets", "Status");
        Directory.CreateDirectory(installedAssets);
        foreach (string state in new[] { "green", "yellow", "red", "white", "grey" })
        {
            string markFile = Path.Combine(directory, "play-" + state + ".png");
            await renderer.ExportMark(state, markFile);
            string buttonFile = Path.Combine(directory, "button-" + state + ".png");
            string lightButton = Path.Combine(directory, "button-" + state + "-light.png");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "Rendered", Path.GetFileName(buttonFile)), buttonFile, true);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "Rendered", Path.GetFileName(lightButton)), lightButton, true);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "Rendered", "puck-" + state + ".png"), Path.Combine(directory, "puck-" + state + ".png"), true);
            File.Copy(markFile, Path.Combine(installedAssets, Path.GetFileName(markFile)), true);
            File.Copy(buttonFile, Path.Combine(installedAssets, Path.GetFileName(buttonFile)), true);
            File.Copy(lightButton, Path.Combine(installedAssets, Path.GetFileName(lightButton)), true);
        }
        renderer.DisposeGraphics();
        browserWindow.Close();
        keeper.Close();
    }
    static async Task Gallery(ElementTheme theme, string path)
    {
        var grid = new Grid { Width = 560, Margin = new Thickness(12), ColumnSpacing = 8, RowSpacing = 12 };
        for (int i = 0; i < 5; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        string[] keys = { "green", "yellow", "red", "white", "grey" };
        for (int i = 0; i < keys.Length; i++)
        {
            var column = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
            var mark = new CrystalStatusMark { Width = 96, Height = 96, MotionEnabled = false };
            mark.SetState(keys[i]);
            column.Children.Add(mark);
            var label = FluentWindow.Text(keys[i], 14);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            column.Children.Add(label);
            var puck = CrystalStatusMark.Puck(keys[i]);
            var scaled = new Viewbox { Width = 96, Height = 96, Child = puck };
            column.Children.Add(scaled);
            Grid.SetColumn(column, i);
            grid.Children.Add(column);
        }
        await Save(new ArtworkWindow(grid, 600, 360), theme, path);
    }
    static async Task VerifyMotion(string directory)
    {
        var mark = new CrystalStatusMark(true, true) { Width = 96, Height = 96, MotionEnabled = false, RequestedTheme = ElementTheme.Dark };
        var window = new ArtworkWindow(mark);
        window.AppWindow.Move(new PointInt32(-12000, -12000));
        window.AppWindow.Show(false);
        await mark.EnsureReady();
        bool enabled = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        bool contrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        Bridge.Preview = false;
        try
        {
            // Capture the widest and narrowest halo at exact pulse endpoints.
            // A transparent border alone cannot detect clipping at the old
            // control boundary; these include the full overscanned surface.
            foreach (string state in new[] { "green", "yellow", "white", "red" })
            {
                mark.SetState(state);
                foreach (int endpoint in new[] { 0, 1 })
                {
                    mark.SetEnergy(endpoint);
                    await Task.Delay(80);
                    await mark.CaptureTransparent(Path.Combine(directory, "halo-" + state + (endpoint == 1 ? "-peak.png" : "-trough.png")));
                    await mark.Capture(Path.Combine(directory, "halo-" + state + (endpoint == 1 ? "-peak-surface.png" : "-trough-surface.png")));
                }
            }
            mark.SetEnergy(-1);
            if (enabled && !contrast)
            {
                mark.SetState("green");
                mark.MotionEnabled = true;
                if (!mark.IsAnimating)
                    throw new InvalidOperationException("Green animation did not start.");
                await Task.Delay(120);
                await mark.Capture(Path.Combine(directory, "motion-green-bright.png"));
                await File.WriteAllTextAsync(Path.Combine(directory, "animation-before.json"), await mark.AnimationInfo());
                await Task.Delay(2000);
                mark.SetState("green");
                await mark.Capture(Path.Combine(directory, "motion-green-dim.png"));
                await File.WriteAllTextAsync(Path.Combine(directory, "animation-after.json"), await mark.AnimationInfo());
                await File.WriteAllTextAsync(Path.Combine(directory, "frame-timing.json"), await mark.SampleFrames());
                await mark.Capture(Path.Combine(directory, "motion-green-crystal.png"));
                await mark.CaptureTransparent(Path.Combine(directory, "crystal-transparent.png"));
                string crystalInfo = await mark.AnimationInfo();
                if (!crystalInfo.Contains("crystal-radiance", StringComparison.Ordinal) || crystalInfo.Contains("confined-volume", StringComparison.Ordinal) || crystalInfo.Contains("cloudDrift", StringComparison.Ordinal))
                    throw new InvalidOperationException("The native crystal light did not animate.");
                await File.WriteAllTextAsync(Path.Combine(directory, "crystal-animation.json"), crystalInfo);
                mark.SetState("yellow");
                if (!mark.IsAnimating)
                    throw new InvalidOperationException("Yellow animation did not start.");
                await Task.Delay(120);
                await mark.Capture(Path.Combine(directory, "motion-yellow-bright.png"));
                await Task.Delay(720);
                await mark.Capture(Path.Combine(directory, "motion-yellow-dim.png"));
            }
            mark.SetState("red");
            if (mark.IsAnimating)
                throw new InvalidOperationException("Disconnected state must be still.");
            await Task.Delay(80);
            await File.WriteAllTextAsync(Path.Combine(directory, "red-animation.json"), await mark.AnimationInfo());
            mark.SetState("green");
            mark.MotionEnabled = false;
            if (mark.IsAnimating)
                throw new InvalidOperationException("Hidden animation must stop.");
            await Task.Delay(80);
            await File.WriteAllTextAsync(Path.Combine(directory, "paused-animation.json"), await mark.AnimationInfo());
            await File.WriteAllTextAsync(Path.Combine(directory, "motion-result.json"), "{\"AnimationsEnabled\":" + enabled.ToString().ToLowerInvariant() + ",\"HighContrast\":" + contrast.ToString().ToLowerInvariant() + ",\"RedStatic\":true,\"HiddenStopped\":true,\"HardwareActions\":0}");
        }
        finally { mark.MotionEnabled = false; mark.DisposeGraphics(); Bridge.Preview = true; window.Close(); }
    }
    static Snapshot Sample(bool present = true) => new Snapshot
    {
        At = DateTime.Now.ToString("o"),
        Version = "1.2.0",
        Controller = new ControllerObservation { At = DateTime.Now.ToString("o"), Status = present ? "Connected" : "Unavailable", Connected = present ? (bool?)true : null, Detail = present ? "Live controller reports received through the puck." : "Connect the puck to check the controller." },
        Status = present ? "Puck ready; Steam running" : "Puck absent: waiting for USB connection",
        SteamRunning = true,
        RepairService = "Installed",
        Devices = present ? new List<UsbDevice> { new UsbDevice { Id = Scope.PuckId, Name = "Steam Controller puck", Present = true } } : new List<UsbDevice>()
    };
    static async Task Capture(UIElement element, string path)
    {
        await Task.Delay(300);
        var bitmap = new RenderTargetBitmap();
        await bitmap.RenderAsync(element);
        if (bitmap.PixelWidth < 32 || bitmap.PixelHeight < 32)
            throw new InvalidOperationException("Native capture is empty: " + path);
        var buffer = await bitmap.GetPixelsAsync();
        CryptographicBuffer.CopyToByteArray(buffer, out byte[] pixels);
        await File.WriteAllBytesAsync(path, Array.Empty<byte>());
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)bitmap.PixelWidth, (uint)bitmap.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
    }
    static async Task Save(FluentWindow window, ElementTheme theme, string path)
    {
        window.PreviewTheme(theme);
        window.AppWindow.Move(new PointInt32(-12000, -12000));
        window.AppWindow.Show(false);
        await Task.Delay(200);
        window.Root.UpdateLayout();
        await Capture(window.Root, path);
        window.Close();
    }
    static async Task Menu(App app, ElementTheme theme, string path)
    {
        var window = new MenuWindow(app, -12000, -12000);
        window.Root.RequestedTheme = theme;
        window.AppWindow.Show(false);
        await Task.Delay(400);
        var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(window.Root.XamlRoot).FirstOrDefault();
        if (popup?.Child == null)
            throw new InvalidOperationException("Native menu did not open.");
        // RenderTargetBitmap cannot capture the desktop acrylic behind a popup.
        // Use an opaque theme fallback for this capture only; the live flyout keeps acrylic.
        if (popup.Child is MenuFlyoutPresenter presenter)
        {
            presenter.RequestedTheme = theme;
            presenter.Background = new SolidColorBrush(theme == ElementTheme.Dark ? ColorHelper.FromArgb(255, 44, 44, 44) : ColorHelper.FromArgb(255, 249, 249, 249));
        }
        await Capture(popup.Child, path);
        window.Flyout.Hide();
    }
    internal static async Task Render(App app, string directory)
    {
        ReceiverConfiguration.UseTestProfile(new ReceiverProfile { DeviceId = @"USB\VID_28DE&PID_1304\PREVIEW", Parent = @"USB\ROOT_HUB30\PREVIEW", Port = 2, Locations = new[] { "PREVIEW_PORT" } });
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);
        var keeper = new FluentWindow("Preview", 10, 10);
        keeper.AppWindow.Move(new PointInt32(-12000, -12000));
        keeper.AppWindow.Show(false);
        Bridge.Sample = Sample();
        await Save(new AboutWindow(), ElementTheme.Dark, Path.Combine(directory, "about-dark.png"));
        await Save(new AboutWindow(), ElementTheme.Light, Path.Combine(directory, "about-light.png"));
        await Save(new StatusWindow(app), ElementTheme.Dark, Path.Combine(directory, "status-ready.png"));
        var hover = new StatusWindow(app);
        hover.PreviewPlayInteraction("PointerOver");
        await Save(hover, ElementTheme.Dark, Path.Combine(directory, "status-play-hover.png"));
        var pressed = new StatusWindow(app);
        pressed.PreviewPlayInteraction("Pressed");
        await Save(pressed, ElementTheme.Dark, Path.Combine(directory, "status-play-pressed.png"));
        await Save(new StatusWindow(app), ElementTheme.Light, Path.Combine(directory, "status-ready-light.png"));
        Bridge.Sample = Sample(false);
        await Save(new StatusWindow(app), ElementTheme.Light, Path.Combine(directory, "status-absent.png"));
        await Save(new StatusWindow(app), ElementTheme.Dark, Path.Combine(directory, "status-absent-dark.png"));
        Bridge.Sample = Sample();
        Bridge.Sample.SteamRunning = false;
        await Save(new StatusWindow(app), ElementTheme.Light, Path.Combine(directory, "status-attention-light.png"));
        Bridge.Sample = Sample();
        Bridge.Sample.PausedUntil = DateTime.UtcNow.AddMinutes(30);
        var paused = new StatusWindow(app);
        paused.ExpandTroubleshooting();
        await Save(paused, ElementTheme.Dark, Path.Combine(directory, "status-paused.png"));
        Bridge.Sample = Sample();
        Bridge.Sample.Devices[0].Problem = 43;
        Bridge.Sample.Status = "Puck USB error: unplug/replug needed";
        var error = new StatusWindow(app);
        error.ExpandTroubleshooting();
        await Save(error, ElementTheme.Dark, Path.Combine(directory, "status-usb-error.png"));
        Bridge.Sample = Sample();
        Bridge.Sample.At = DateTime.Now.AddMinutes(-2).ToString("o");
        await Save(new StatusWindow(app), ElementTheme.Dark, Path.Combine(directory, "status-stale.png"));
        Bridge.Sample = Sample();
        await Save(new DetailsWindow(app), ElementTheme.Dark, Path.Combine(directory, "details.png"));
        await Menu(app, ElementTheme.Dark, Path.Combine(directory, "menu-dark.png"));
        await Menu(app, ElementTheme.Light, Path.Combine(directory, "menu-light.png"));
        await Gallery(ElementTheme.Dark, Path.Combine(directory, "artwork-dark.png"));
        await Gallery(ElementTheme.Light, Path.Combine(directory, "artwork-light.png"));
        await VerifyMotion(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "result.json"), "{\"Renderer\":\"WinUI 3 RenderTargetBitmap\",\"HardwareActions\":0,\"Themes\":[\"Dark\",\"Light\"],\"NativeMenuFlyout\":true}");
        keeper.Close();
    }
}
