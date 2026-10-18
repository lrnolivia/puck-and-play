using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Markup;
using Windows.UI.ViewManagement;

namespace PuckPlay;

internal sealed class CrystalStatusMark : UserControl
{
    readonly Grid root = new Grid();
    readonly Image fallback = new Image { Stretch = Stretch.Uniform };
    readonly FrameworkElement contrastMark = (FrameworkElement)XamlReader.Load("<Path xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Stretch='Uniform' Margin='26' Fill='{ThemeResource TextFillColorPrimaryBrush}' Data='M 0,0 L 1,0.577 L 0,1.154 Z' />");
    readonly UISettings settings = new UISettings();
    readonly AccessibilitySettings accessibility = new AccessibilitySettings();
    readonly bool platter, forceRenderer;
    CrystalRenderer? renderer;
    Task? initialization;
    bool closed, ready, motion = true, contrastSubscribed, settingsSubscribed;
    string key = "grey";
    float energy = -1;
    internal string StateKey => key;
    internal bool IsAnimating => ready && motion && settings.AnimationsEnabled && !accessibility.HighContrast && (key == "green" || key == "yellow");
    internal CrystalStatusMark(bool onPlatter = true, bool useRendererForPreview = false)
    {
        platter = onPlatter;
        forceRenderer = useRendererForPreview;
        Content = root;
        root.Children.Add(fallback);
        root.Children.Add(contrastMark);
        contrastMark.Visibility = Visibility.Collapsed;
        UpdateFallback();
        if (platter)
        {
            fallback.RenderTransformOrigin = new Windows.Foundation.Point(.5, .5);
            fallback.RenderTransform = new ScaleTransform { ScaleX = 1.5, ScaleY = 1.5 };
        }
        Loaded += async delegate
        {
            if (closed)
                return;
            if (!settingsSubscribed)
            {
                settings.AnimationsEnabledChanged += AnimationsChanged;
                settingsSubscribed = true;
            }
            try
            {
                if (!contrastSubscribed)
                {
                    accessibility.HighContrastChanged += ContrastChanged;
                    contrastSubscribed = true;
                }
            }
            catch (System.Runtime.InteropServices.COMException) { }
            if (!Bridge.Preview || forceRenderer)
            {
                try
                {
                    await EnsureReady();
                }
                catch (Exception error) { GraphicsFailed(error); }
            }
        };
        Unloaded += delegate
        {
            DisposeGraphics();
        };
        SizeChanged += delegate
        {
            UpdateVisualBounds();
        };
        ActualThemeChanged += delegate
        {
            UpdateFallback();
            Apply();
        };
    }
    void AnimationsChanged(UISettings sender, object args) => DispatcherQueue.TryEnqueue(Apply);
    void ContrastChanged(AccessibilitySettings sender, object args) => DispatcherQueue.TryEnqueue(Apply);
    internal bool MotionEnabled
    {
        set
        {
            if (motion == value)
                return;
            motion = value;
            Apply();
        }
    }
    internal void SetState(string value)
    {
        if (key != value)
        {
            key = value;
            UpdateFallback();
        }
        Apply();
    }
    void UpdateFallback()
    {
        string file = Path.Combine(AppContext.BaseDirectory, "Assets", "Status", (platter ? "button-" : "play-") + key + ".png");
        if (platter && ActualTheme == ElementTheme.Light)
        {
            string light = Path.Combine(AppContext.BaseDirectory, "Assets", "Status", "button-" + key + "-light.png");
            if (File.Exists(light))
                file = light;
        }
        if (!File.Exists(file))
            file = Path.Combine(AppContext.BaseDirectory, "Assets", "Status", "play-" + key + ".png");
        if (File.Exists(file))
            fallback.Source = new BitmapImage { CreateOptions = BitmapCreateOptions.IgnoreImageCache, UriSource = new Uri(file) };
    }
    internal Task EnsureReady()
    {
        ObjectDisposedException.ThrowIf(closed, this);
        return initialization ??= Initialize();
    }
    Task Initialize()
    {
        double width = ActualWidth > 0 ? ActualWidth : double.IsNaN(Width) ? 96 : Width;
        // Overscan belongs to the rendering surface, not the layout. Doubling
        // both the surface and its world extent preserves the crystal's size.
        float overscan = platter ? 2f : 1.5f;
        int size = (int)Math.Ceiling(width * overscan * (XamlRoot?.RasterizationScale ?? 1) * 1.5);
        renderer = new CrystalRenderer(ElementCompositionPreview.GetElementVisual(root).Compositor, size, error => DispatcherQueue.TryEnqueue(() => GraphicsFailed(error)), (platter ? 1.2f : 1.8f) * overscan);
        UpdateVisualBounds();
        ElementCompositionPreview.SetElementChildVisual(root, renderer.Visual);
        ready = true;
        fallback.Visibility = Visibility.Collapsed;
        Apply();
        if (!Bridge.Preview)
            _ = MeasureLive();
        return Task.CompletedTask;
    }
    void UpdateVisualBounds()
    {
        if (renderer == null)
            return;
        float width = (float)(ActualWidth > 0 ? ActualWidth : double.IsNaN(Width) ? 96 : Width);
        float height = (float)(ActualHeight > 0 ? ActualHeight : double.IsNaN(Height) ? width : Height);
        float overscan = platter ? 2f : 1.5f;
        renderer.Visual.Size = new Vector2(width, height) * overscan;
        renderer.Visual.Offset = new Vector3(width * (1 - overscan) * .5f, height * (1 - overscan) * .5f, 0);
    }
    async Task MeasureLive()
    {
        try
        {
            await Task.Delay(500);
            if (closed || renderer == null || !IsAnimating)
                return;
            object sample = await renderer.SampleFrames(2500);
            if (closed)
                return;
            Directory.CreateDirectory(Bridge.UserRoot);
            await File.WriteAllTextAsync(Path.Combine(Bridge.UserRoot, "graphics-performance.json"), JsonSerializer.Serialize(new
            {
                At = DateTime.Now.ToString("o"),
                Version = typeof(CrystalStatusMark).Assembly.GetName().Version!.ToString(),
                Renderer = "Direct3D 11 mesh on premultiplied native composition surface",
                Visible = IsLoaded && motion,
                Sample = sample
            }));
        }
        catch (Exception error) when (error is ObjectDisposedException || error is InvalidOperationException || error is IOException || error is UnauthorizedAccessException) { }
    }
    void GraphicsFailed(Exception error)
    {
        if (closed)
            return;
        ready = false;
        fallback.Visibility = Visibility.Visible;
        ElementCompositionPreview.SetElementChildVisual(root, null);
        renderer?.Dispose();
        renderer = null;
        Bridge.Log(error);
    }
    void Apply()
    {
        bool contrast = accessibility.HighContrast;
        contrastMark.Visibility = contrast ? Visibility.Visible : Visibility.Collapsed;
        if (renderer == null || closed)
            return;
        renderer.Update(key, motion && settings.AnimationsEnabled && (!Bridge.Preview || forceRenderer) && !contrast, contrast, energy);
    }
    internal Task ExportMark(string state, string path)
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "Rendered", "play-" + state + ".png"), path, true);
        return Task.CompletedTask;
    }
    internal async Task Capture(string path)
    {
        await EnsureReady();
        await renderer!.Save(path, ActualTheme == ElementTheme.Light ? ColorHelper.FromArgb(255, 250, 250, 250) : ColorHelper.FromArgb(255, 32, 32, 32));
    }
    internal async Task CaptureTransparent(string path)
    {
        await EnsureReady();
        await renderer!.Save(path);
    }
    internal async Task<string> AnimationInfo()
    {
        await EnsureReady();
        return JsonSerializer.Serialize(renderer!.AnimationInfo());
    }
    internal async Task<string> SampleFrames()
    {
        await EnsureReady();
        return JsonSerializer.Serialize(await renderer!.SampleFrames());
    }
    internal void Stop()
    {
        MotionEnabled = false;
    }
    internal void DisposeGraphics()
    {
        if (closed)
            return;
        closed = true;
        if (settingsSubscribed)
            settings.AnimationsEnabledChanged -= AnimationsChanged;
        if (contrastSubscribed)
            try
            {
                accessibility.HighContrastChanged -= ContrastChanged;
            }
            catch (System.Runtime.InteropServices.COMException) { }
        ElementCompositionPreview.SetElementChildVisual(root, null);
        renderer?.Dispose();
    }
    internal void SetEnergy(double value)
    {
        energy = (float)value;
        Apply();
    }
    internal static Canvas Puck(string state) => PuckArtwork.Puck(state);
}
