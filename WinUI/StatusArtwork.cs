using System;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using SteamControllerGuard;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace PuckPlay;

internal static class StatusArt
{
    internal static string Key(ConnectionState state) => state == ConnectionState.Ready ? "green" : state == ConnectionState.Attention ? "yellow" : state == ConnectionState.Disconnected ? "red" : "grey";
    internal static string CurrentKey()
    {
        var snapshot = Bridge.Read();
        return Key(ConnectionView.Create(snapshot, snapshot?.PausedUntil ?? DateTime.MinValue, DateTime.Now).State);
    }
    internal static string File(string key, string extension = "png")
    {
        string path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Status", "puck-" + key + "." + extension);
        return System.IO.File.Exists(path) ? path : System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", extension == "ico" ? "puck.ico" : "puck.png");
    }
    internal static Color Tint(string key) => key == "green" ? ColorHelper.FromArgb(255, 122, 242, 38)
        : key == "yellow" ? ColorHelper.FromArgb(255, 255, 209, 9)
        : key == "red" ? ColorHelper.FromArgb(255, 250, 38, 56)
        : key == "white" ? ColorHelper.FromArgb(255, 240, 247, 255) : ColorHelper.FromArgb(255, 128, 140, 158);
    internal static Color Mix(Color a, Color b, double fraction) => ColorHelper.FromArgb(255, (byte)(a.R + (b.R - a.R) * fraction), (byte)(a.G + (b.G - a.G) * fraction), (byte)(a.B + (b.B - a.B) * fraction));
    internal static Color Alpha(Color color, byte alpha) => ColorHelper.FromArgb(alpha, color.R, color.G, color.B);
}

internal static class PuckArtwork
{
    internal static Canvas Puck(string state)
    {
        var canvas = new Canvas { Width = 256, Height = 256 };
        canvas.Children.Add(new Image
        {
            Width = 256,
            Height = 256,
            Stretch = Stretch.Uniform,
            Source = new BitmapImage { CreateOptions = BitmapCreateOptions.IgnoreImageCache, UriSource = new Uri(StatusArt.File(state)) }
        });
        return canvas;
    }
}
