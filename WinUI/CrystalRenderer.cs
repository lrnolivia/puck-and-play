using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.D3DCompiler;
using Vortice.DXGI;
using Vortice.Mathematics;
using Microsoft.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI;
using Color = Windows.UI.Color;
using Colors = Microsoft.UI.Colors;
using Buffer = Vortice.Direct3D11.ID3D11Buffer;

namespace PuckPlay;

internal sealed class CrystalRenderer : IDisposable
{
    readonly object gate = new object();
    readonly List<IDisposable> resources = new List<IDisposable>();
    readonly Microsoft.UI.Dispatching.DispatcherQueue ownerQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    readonly AutoResetEvent changed = new AutoResetEvent(false);
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly List<double> intervals = new List<double>();
    readonly ID3D11Device device;
    readonly ID3D11DeviceContext context;
    readonly CrystalDevice sharedDevice;
    readonly Buffer vertices, indices, constants;
    readonly ID3D11VertexShader crystalVS, glowVS;
    readonly ID3D11PixelShader crystalPS, glowPS;
    readonly ID3D11InputLayout layout;
    readonly ID3D11BlendState blend;
    readonly ID3D11RasterizerState raster;
    readonly ID3D11SamplerState sampler;
    readonly ID3D11Texture2D environment, target, multisample;
    readonly ID3D11ShaderResourceView environmentView;
    readonly ID3D11RenderTargetView targetView;
    readonly CanvasDevice canvasDevice;
    readonly CanvasBitmap bitmap;
    readonly IDirect3DDevice directDevice;
    readonly IDirect3DSurface directSurface;
    readonly CompositionGraphicsDevice graphicsDevice;
    readonly CompositionDrawingSurface surface;
    internal readonly SpriteVisual Visual;
    readonly Thread worker;
    readonly uint indexCount;
    readonly int pixels;
    readonly float viewHalf;
    volatile bool closed, animate, dirty = true;
    bool sampling;
    string state = "grey";
    double seconds, previousFrame;
    long frames;
    float energy = -1;
    string failure = "";
    readonly Action<Exception> failed;
    [StructLayout(LayoutKind.Sequential)]
    struct Frame
    {
        public Vector4 Tint, Lighting;
    }
    [DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr value, out IntPtr result);
    [DllImport("d3d11.dll")] static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr value, out IntPtr result);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWaitableTimerEx(IntPtr attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr callback, IntPtr arg, bool resume);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr value, uint milliseconds);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr value);

    internal CrystalRenderer(Compositor compositor, int resolution, Action<Exception> onFailure, float viewportHalf = 1.8f, string? shaderOverride = null)
    {
        failed = onFailure;
        pixels = Math.Clamp(resolution, 192, 768);
        viewHalf = viewportHalf;
        try
        {
            sharedDevice = CrystalDevice.For(compositor);
            device = sharedDevice.Device;
            context = sharedDevice.Context;
            canvasDevice = sharedDevice.Canvas;
            directDevice = sharedDevice.Direct;
            string root = Path.Combine(AppContext.BaseDirectory, "Assets", "Crystal");
            using (var input = new BinaryReader(File.OpenRead(Path.Combine(root, "play.mesh"))))
            {
                if (System.Text.Encoding.ASCII.GetString(input.ReadBytes(8)) != "PPCRYS1\0")
                    throw new InvalidDataException("Invalid crystal mesh.");
                int count = checked((int)input.ReadUInt32());
                indexCount = input.ReadUInt32();
                if (count < 3 || count > 100000 || indexCount < 3 || indexCount > 1000000)
                    throw new InvalidDataException("Invalid crystal geometry size.");
                var data = new float[count * 6];
                for (int i = 0; i < data.Length; i++)
                    data[i] = input.ReadSingle();
                var order = new uint[indexCount];
                for (int i = 0; i < order.Length; i++)
                    order[i] = input.ReadUInt32();
                if (order.Any(i => i >= count))
                    throw new InvalidDataException("Invalid crystal mesh index.");
                vertices = Own(device.CreateBuffer<float>(data, BindFlags.VertexBuffer));
                indices = Own(device.CreateBuffer<uint>(order, BindFlags.IndexBuffer));
            }
            string shader = shaderOverride ?? File.ReadAllText(Path.Combine(root, "crystal.hlsl"));
            var cv = CompileShader(shader, "CrystalVS", "vs_5_0");
            var cp = CompileShader(shader, "CrystalPS", "ps_5_0");
            var gv = CompileShader(shader, "GlowVS", "vs_5_0");
            var gp = CompileShader(shader, "GlowPS", "ps_5_0");
            crystalVS = Own(device.CreateVertexShader(cv.Span));
            crystalPS = Own(device.CreatePixelShader(cp.Span));
            glowVS = Own(device.CreateVertexShader(gv.Span));
            glowPS = Own(device.CreatePixelShader(gp.Span));
            layout = Own(device.CreateInputLayout(new[] { new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0), new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0) }, cv.Span));
            constants = Own(device.CreateBuffer(new BufferDescription { ByteWidth = 32, BindFlags = BindFlags.ConstantBuffer, Usage = ResourceUsage.Default }));
            blend = Own(device.CreateBlendState(BlendDescription.AlphaBlend));
            raster = Own(device.CreateRasterizerState(new RasterizerDescription { CullMode = CullMode.Back, FillMode = FillMode.Solid, DepthClipEnable = true, MultisampleEnable = true }));
            sampler = Own(device.CreateSamplerState(new SamplerDescription { Filter = Filter.MinMagMipLinear, AddressU = TextureAddressMode.Wrap, AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp, MaxLOD = float.MaxValue }));
            using (var source = new BinaryReader(File.OpenRead(Path.Combine(root, "studio.rgba16f"))))
            {
                uint width = source.ReadUInt32(), height = source.ReadUInt32();
                byte[] bytes = source.ReadBytes(checked((int)(width * height * 8)));
                if (width != 512 || height != 256 || bytes.Length != 512 * 256 * 8)
                    throw new InvalidDataException("Invalid crystal studio.");
                var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    environment = Own(device.CreateTexture2D(new Texture2DDescription { Width = width, Height = height, MipLevels = 1, ArraySize = 1, Format = Format.R16G16B16A16_Float, SampleDescription = new SampleDescription(1, 0), BindFlags = BindFlags.ShaderResource }, new SubresourceData(pin.AddrOfPinnedObject(), width * 8)));
                }
                finally { pin.Free(); }
            }
            environmentView = Own(device.CreateShaderResourceView(environment));
            target = Own(device.CreateTexture2D(new Texture2DDescription { Width = (uint)pixels, Height = (uint)pixels, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0), BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource }));
            multisample = Own(device.CreateTexture2D(new Texture2DDescription { Width = (uint)pixels, Height = (uint)pixels, MipLevels = 1, ArraySize = 1, Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(4, 0), BindFlags = BindFlags.RenderTarget }));
            targetView = Own(device.CreateRenderTargetView(multisample));
            using (var dxgiSurface = target.QueryInterface<IDXGISurface>())
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(dxgiSurface.NativePointer, out IntPtr pointer));
                try
                {
                    directSurface = Own(WinRT.MarshalInterface<IDirect3DSurface>.FromAbi(pointer));
                }
                finally { Marshal.Release(pointer); }
            }
            bitmap = Own(CanvasBitmap.CreateFromDirect3D11Surface(canvasDevice, directSurface, 96, CanvasAlphaMode.Premultiplied));
            graphicsDevice = sharedDevice.Graphics;
            surface = Own(graphicsDevice.CreateDrawingSurface(new Windows.Foundation.Size(pixels, pixels), DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied));
            Visual = Own(compositor.CreateSpriteVisual());
            Visual.Brush = Own(compositor.CreateSurfaceBrush(surface));
            Draw();
            worker = new Thread(Loop) { IsBackground = true, Name = "Crystal render" };
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        }
        catch { ReleaseResources(); changed.Dispose(); throw; }
    }
    T Own<T>(T resource) where T : IDisposable
    {
        resources.Add(resource);
        return resource;
    }
    static unsafe ReadOnlyMemory<byte> CompileShader(string source, string entry, string profile)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(source);
        fixed (byte* pointer = bytes)
        {
            var result = Compiler.Compile(pointer, (nuint)bytes.Length, "crystal.hlsl", null, null, entry, profile, ShaderFlags.OptimizationLevel3, EffectFlags.None, out Blob code, out Blob? errors);
            using (code)
            using (errors)
            {
                if (result.Failure)
                    throw new InvalidDataException(errors?.AsString() ?? "The crystal shader could not compile.");
                return code.AsMemory();
            }
        }
    }
    void ReleaseResources()
    {
        lock (sharedDevice?.Gate ?? gate)
        {
            try
            {
                if (Visual != null)
                    Visual.Brush = null;
                if (context != null)
                    context.ClearState();
            }
            catch (Exception error) { Debug.WriteLine(error.Message); }
            for (int i = resources.Count - 1; i >= 0; i--)
            {
                try
                {
                    resources[i].Dispose();
                }
                catch (Exception error) { Debug.WriteLine(error.Message); }
            }
            resources.Clear();
            context?.Flush();
        }
    }
    internal void Update(string key, bool running, bool contrast, float forcedEnergy = -1)
    {
        lock (gate)
        {
            if (closed)
                return;
            bool shouldAnimate = running && (key == "green" || key == "yellow");
            bool changedState = state != key || energy != forcedEnergy || animate != shouldAnimate || Visual.Opacity != (contrast ? 0 : 1);
            if (!changedState)
                return;
            state = key;
            energy = forcedEnergy;
            animate = running && (key == "green" || key == "yellow");
            if (changedState || !animate)
                dirty = true;
            Visual.Opacity = contrast ? 0 : 1;
        }
        changed.Set();
    }
    void Loop()
    {
        IntPtr timer = CreateWaitableTimerEx(IntPtr.Zero, null, 2, 0x1F0003);
        if (timer == IntPtr.Zero)
            timer = CreateWaitableTimerEx(IntPtr.Zero, null, 0, 0x1F0003);
        double next = clock.Elapsed.TotalSeconds, last = next;
        try
        {
            while (!closed)
            {
                if (!animate && !dirty)
                {
                    changed.WaitOne();
                    last = clock.Elapsed.TotalSeconds;
                    next = last;
                    continue;
                }
                double now = clock.Elapsed.TotalSeconds;
                lock (gate)
                {
                    if (animate)
                        seconds += Math.Clamp(now - last, 0, .05);
                    Draw();
                    dirty = false;
                    if (sampling && previousFrame > 0 && intervals.Count < 2048)
                        intervals.Add((now - previousFrame) * 1000);
                    previousFrame = now;
                    frames++;
                }
                last = now;
                next += 1.0 / 120.0;
                if (next < clock.Elapsed.TotalSeconds - .025)
                    next = clock.Elapsed.TotalSeconds;
                double remaining = next - clock.Elapsed.TotalSeconds;
                if (remaining > 0)
                {
                    long due = -(long)(remaining * 10000000);
                    if (timer != IntPtr.Zero && SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                        WaitForSingleObject(timer, 30);
                    else
                        changed.WaitOne(TimeSpan.FromSeconds(remaining));
                }
            }
        }
        catch (Exception error) { failure = error.Message; failed(error); }
        finally { if (timer != IntPtr.Zero) CloseHandle(timer); }
    }
    void Draw()
    {
        lock (sharedDevice.Gate)
        {
            var tint = state switch
            {
                "green" => new Vector3(.28f, .96f, .015f),
                "yellow" => new Vector3(1, .88f, .008f),
                "red" => new Vector3(1, .075f, .15f),
                "white" => new Vector3(.80f, .91f, 1),
                _ => new Vector3(.30f, .38f, .49f)
            };
            double period = state == "yellow" ? 1.8 : 5.5;
            float pulse = energy >= 0 ? energy : (float)(.5 + .5 * Math.Cos(seconds * Math.PI * 2 / period));
            if (state == "yellow")
                pulse = .25f + .75f * pulse;
            float radiance = state == "grey" ? 0 : state == "yellow" ? 1.4f : 1;
            var values = new Frame { Tint = new Vector4(tint, 1), Lighting = new Vector4(pulse, radiance, state == "green" || state == "yellow" || state == "white" ? 1 : 0, viewHalf) };
            using (var deviceLock = canvasDevice.Lock())
            {
                context.ClearState();
                context.UpdateSubresource(values, constants);
                context.OMSetRenderTargets(targetView);
                context.ClearRenderTargetView(targetView, new Color4(0, 0, 0, 0));
                context.OMSetBlendState(blend);
                context.RSSetState(raster);
                context.RSSetViewport(new Viewport(0, 0, pixels, pixels));
                context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
                context.VSSetConstantBuffer(0, constants);
                context.PSSetConstantBuffer(0, constants);
                context.PSSetShaderResource(0, environmentView);
                context.PSSetSampler(0, sampler);
                context.VSSetShader(glowVS);
                context.PSSetShader(glowPS);
                context.Draw(3, 0);
                context.IASetInputLayout(layout);
                context.IASetVertexBuffer(0, vertices, 24);
                context.IASetIndexBuffer(indices, Format.R32_UInt, 0);
                context.VSSetShader(crystalVS);
                context.PSSetShader(crystalPS);
                context.DrawIndexed(indexCount, 0, 0);
                context.OMSetRenderTargets((ID3D11RenderTargetView?)null);
                context.ResolveSubresource(target, 0, multisample, 0, Format.B8G8R8A8_UNorm);
                context.Flush();
            }
            using var drawing = CanvasComposition.CreateDrawingSession(surface);
            drawing.Clear(Colors.Transparent);
            drawing.DrawImage(bitmap);
        }
    }
    internal object[] AnimationInfo()
    {
        lock (gate)
            return state == "green" || state == "yellow" ? new object[] { new { name = "crystal-radiance", state = animate ? "running" : "paused", start = 0, time = seconds * 1000, frames, renderer = "Direct3D 11 mesh / native composition", triangles = indexCount / 3, resolution = pixels, error = failure } } : Array.Empty<object>();
    }
    internal long TotalFrames
    {
        get
        {
            lock (gate)
                return frames;
        }
    }
    internal async Task<object> SampleFrames(int milliseconds = 2500)
    {
        lock (gate)
        {
            if (closed)
                throw new ObjectDisposedException(nameof(CrystalRenderer));
            if (sampling)
                throw new InvalidOperationException("A frame sample is already running.");
            intervals.Clear();
            previousFrame = 0;
            sampling = true;
        }
        await Task.Delay(Math.Clamp(milliseconds, 100, 10000));
        double[] values;
        lock (gate)
        {
            sampling = false;
            values = intervals.OrderBy(x => x).ToArray();
        }
        double median = values.Length == 0 ? 0 : values[values.Length / 2];
        return new
        {
            samples = values.Length,
            medianMs = median,
            p95Ms = values.Length == 0 ? 0 : values[(int)(values.Length * .95)],
            longFrames = values.Count(x => x > 16.67),
            targetFps = 120,
            animations = AnimationInfo()
        };
    }
    internal async Task Save(string path, Color? backdrop = null)
    {
        byte[] bytes;
        lock (gate)
        {
            using var output = new CanvasRenderTarget(canvasDevice, pixels, pixels, 96);
            using (var drawing = output.CreateDrawingSession())
            {
                drawing.Clear(backdrop ?? Colors.Transparent);
                drawing.DrawImage(bitmap);
            }
            bytes = output.GetPixelBytes();
        }
        await File.WriteAllBytesAsync(path, Array.Empty<byte>());
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)pixels, (uint)pixels, 96, 96, bytes);
        await encoder.FlushAsync();
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (closed)
                return;
            closed = true;
        }
        changed.Set();
        if (worker.Join(2000))
        {
            ReleaseResources();
            changed.Dispose();
            return;
        }
        // A pending driver call must finish before its textures can be freed.
        // Keep cleanup attached to completion rather than abandoning the device.
        ThreadPool.QueueUserWorkItem(delegate
        {
            worker.Join();
            Action cleanup = () => { ReleaseResources(); changed.Dispose(); };
            if (!ownerQueue.TryEnqueue(() => cleanup()))
                cleanup();
        });
    }
}

// Composition retains a connection to each drawing device for its lifetime.
// Reuse that connection for marks in the same compositor rather than creating
// a new native device every time the status window is opened.
internal sealed class CrystalDevice : IDisposable
{
    bool closed;
    static readonly Dictionary<Compositor, CrystalDevice> devices = new Dictionary<Compositor, CrystalDevice>();
    internal readonly object Gate = new object();
    internal readonly ID3D11Device Device;
    internal readonly ID3D11DeviceContext Context;
    internal readonly IDirect3DDevice Direct;
    internal readonly CanvasDevice Canvas;
    internal readonly CompositionGraphicsDevice Graphics;
    readonly List<IDisposable> resources = new List<IDisposable>();
    [DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr value, out IntPtr result);

    CrystalDevice(Compositor compositor)
    {
        try
        {
            D3D11.D3D11CreateDevice(IntPtr.Zero, DriverType.Hardware, DeviceCreationFlags.BgraSupport, new[] { FeatureLevel.Level_11_0 }, out Device, out Context).CheckError();
            resources.Add(Device);
            resources.Add(Context);
            using (var multithread = Context.QueryInterface<ID3D11Multithread>())
                multithread.SetMultithreadProtected(true);
            using (var dxgi = Device.QueryInterface<IDXGIDevice>())
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer, out IntPtr pointer));
                try
                {
                    Direct = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(pointer);
                    resources.Add(Direct);
                }
                finally { Marshal.Release(pointer); }
            }
            Canvas = CanvasDevice.CreateFromDirect3D11Device(Direct);
            resources.Add(Canvas);
            Graphics = CanvasComposition.CreateCompositionGraphicsDevice(compositor, Canvas);
            resources.Add(Graphics);
        }
        catch { Dispose(); throw; }
    }
    internal static CrystalDevice For(Compositor compositor)
    {
        if (!devices.TryGetValue(compositor, out var device))
        {
            device = new CrystalDevice(compositor);
            devices.Add(compositor, device);
        }
        return device;
    }
    internal static void ReleaseAll()
    {
        foreach (var device in devices.Values)
            device.Dispose();
        devices.Clear();
    }
    public void Dispose()
    {
        lock (Gate)
        {
            if (closed)
                return;
            closed = true;
            try
            { Context?.ClearState(); Context?.Flush(); }
            catch (Exception error) { Debug.WriteLine(error.Message); }
            for (int i = resources.Count - 1; i >= 0; i--)
            {
                try
                {
                    resources[i].Dispose();
                }
                catch (Exception error) { Debug.WriteLine(error.Message); }
            }
            resources.Clear();
        }
    }
}
