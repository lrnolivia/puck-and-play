using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace PuckPlay;

// Capture the composed menu and its surrounding host area for native QA.
// Capturing just the XAML popup cannot expose an unwanted desktop host window.
internal static class MenuCapture
{
    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfo
    {
        internal uint Size;
        internal int Width, Height;
        internal ushort Planes, Bits;
        internal uint Compression, ImageSize;
        internal int X, Y;
        internal uint Colors, Important, Mask;
    }
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr item);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
    [DllImport("gdi32.dll", SetLastError = true)] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);

    internal static async Task Save(int x, int y, int width, int height, string path)
    {
        byte[] pixels = new byte[checked(width * height * 4)];
        IntPtr screen = GetDC(IntPtr.Zero), dc = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            dc = CreateCompatibleDC(screen);
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 };
            bitmap = CreateDIBSection(screen, ref info, 0, out IntPtr data, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || dc == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            previous = SelectObject(dc, bitmap);
            if (!BitBlt(dc, 0, 0, width, height, screen, x, y, 0x40CC0020))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            Marshal.Copy(data, pixels, 0, pixels.Length);
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(dc, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (dc != IntPtr.Zero) DeleteDC(dc);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
        }
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        await File.WriteAllBytesAsync(path, Array.Empty<byte>());
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, (uint)width, (uint)height, 96, 96, pixels);
        await encoder.FlushAsync();
    }
}
