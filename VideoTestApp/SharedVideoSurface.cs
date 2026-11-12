using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.Direct3D9;
using Vortice.DXGI;
using Vortice.Mathematics;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
using D3D9Format = Vortice.Direct3D9.Format;
using DxgiFormat = Vortice.DXGI.Format;

namespace VideoTestApp;

/// <summary>
/// A D3D11 texture shared with a D3D9Ex surface. MediaPlayer copies decoded frames into it on the GPU,
/// and WPF's D3DImage displays it, so frames never pass through CPU memory.
/// </summary>
internal sealed class SharedVideoSurface : IDisposable
{
    private readonly IDirect3D9Ex _d3d9;
    private readonly IDirect3DDevice9Ex _device9;
    private readonly ID3D11Device _device11;
    private readonly ID3D11Texture2D _texture;
    private readonly IDirect3DTexture9 _texture9;
    private readonly IDirect3DSurface9 _surface9;

    public SharedVideoSurface(int width, int height)
    {
        Width = width;
        Height = height;
        var window = GetDesktopWindow();
        _d3d9 = D3D9.Direct3DCreate9Ex();
        _device9 = _d3d9.CreateDeviceEx(0, Vortice.Direct3D9.DeviceType.Hardware, window,
            CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded | CreateFlags.FpuPreserve,
            new Vortice.Direct3D9.PresentParameters
            {
                Windowed = true,
                SwapEffect = Vortice.Direct3D9.SwapEffect.Discard,
                DeviceWindowHandle = window,
                PresentationInterval = PresentInterval.Default
            });

        // A shared texture opens only on its own adapter, so match D3D9 adapter 0, which WPF renders with.
        var luid = _d3d9.GetAdapterLuid(0);
        using (var factory = DXGI.CreateDXGIFactory1<IDXGIFactory4>())
        using (var adapter = factory.EnumAdapterByLuid<IDXGIAdapter>(new Vortice.Luid(luid.LowPart, luid.HighPart)))
        {
            D3D11.D3D11CreateDevice(adapter, DriverType.Unknown, DeviceCreationFlags.BgraSupport, null!,
                out _device11!).CheckError();
        }

        // MediaPlayer copies frames on its own thread, so serialize access to this device's context.
        using (var multithread = _device11.QueryInterface<ID3D11Multithread>())
        {
            multithread.SetMultithreadProtected(true);
        }

        _texture = _device11.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = DxgiFormat.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            MiscFlags = ResourceOptionFlags.Shared
        });

        using (var resource = _texture.QueryInterface<IDXGIResource>())
        {
            var handle = resource.SharedHandle;
            _texture9 = _device9.CreateTexture((uint)width, (uint)height, 1, Vortice.Direct3D9.Usage.RenderTarget,
                D3D9Format.A8R8G8B8, Pool.Default, ref handle);
        }

        _surface9 = _texture9.GetSurfaceLevel(0);

        using var dxgiSurface = _texture.QueryInterface<IDXGISurface>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(dxgiSurface.NativePointer, out var inspectable));
        try
        {
            Surface = MarshalInterface<IDirect3DSurface>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>The texture as a WinRT surface, for MediaPlayer.CopyFrameToVideoSurface.</summary>
    public IDirect3DSurface Surface { get; }

    /// <summary>The same texture as an IDirect3DSurface9 pointer, for D3DImage.SetBackBuffer.</summary>
    public IntPtr D3D9Surface => _surface9.NativePointer;

    /// <summary>Submits pending GPU work so the D3D9 side reads the completed frame.</summary>
    public void Flush() => _device11.ImmediateContext.Flush();

    /// <summary>Reads one BGRA pixel back from the GPU. Used by tests to check frame content.</summary>
    public byte[] ReadPixel(int x, int y)
    {
        using var staging = _device11.CreateTexture2D(new Texture2DDescription
        {
            Width = 1,
            Height = 1,
            MipLevels = 1,
            ArraySize = 1,
            Format = DxgiFormat.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            CPUAccessFlags = CpuAccessFlags.Read
        });
        var context = _device11.ImmediateContext;
        context.CopySubresourceRegion(staging, 0, 0, 0, 0, _texture, 0, new Box(x, y, 0, x + 1, y + 1, 1));
        var mapped = context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            var pixel = new byte[4];
            Marshal.Copy(mapped.DataPointer, pixel, 0, 4);
            return pixel;
        }
        finally
        {
            context.Unmap(staging, 0);
        }
    }

    public void Dispose()
    {
        ((IDisposable)Surface).Dispose();
        _surface9.Dispose();
        _texture9.Dispose();
        _texture.Dispose();
        _device11.Dispose();
        _device9.Dispose();
        _d3d9.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("d3d11.dll")]
    private static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgiSurface, out IntPtr graphicsSurface);
}
