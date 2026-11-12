using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Management.Deployment;

namespace VideoTestApp;

public sealed record GpuAdapter(string Name, string? DriverVersion, bool IsHardware);

public sealed record CodecPackage(string Name, string Version);

public sealed record SystemInfo(
    string WindowsName,
    string WindowsVersion,
    IReadOnlyList<GpuAdapter> Adapters,
    IReadOnlyList<CodecPackage> HevcPackages)
{
    private const uint SoftwareAdapterFlag = 2;
    private const uint MicrosoftVendorId = 0x1414;
    private const uint BasicRenderDeviceId = 0x8C;

    public static SystemInfo Collect() => new(
        GetWindowsName(), GetWindowsVersion(), GetAdapters(), GetHevcPackages());

    public static string FormatWindowsName(string productName, int build) =>
        // Windows 11 still reports "Windows 10" as its registry ProductName; winver shows Windows 11.
        build >= 22000 ? productName.Replace("Windows 10", "Windows 11") : productName;

    public static string FormatWindowsVersion(string? displayVersion, string build, int? ubr)
    {
        var osBuild = ubr is null ? build : $"{build}.{ubr}";
        return string.IsNullOrEmpty(displayVersion)
            ? $"OS Build {osBuild}"
            : $"Version {displayVersion} (OS Build {osBuild})";
    }

    /// <summary>Formats the user-mode driver version returned by IDXGIAdapter::CheckInterfaceSupport.</summary>
    public static string FormatDriverVersion(long umdVersion) =>
        $"{(umdVersion >> 48) & 0xFFFF}.{(umdVersion >> 32) & 0xFFFF}.{(umdVersion >> 16) & 0xFFFF}.{umdVersion & 0xFFFF}";

    public static bool IsHardwareAdapter(uint vendorId, uint deviceId, uint flags) =>
        (flags & SoftwareAdapterFlag) == 0 && !(vendorId == MicrosoftVendorId && deviceId == BasicRenderDeviceId);

    private static RegistryKey? OpenCurrentVersion() =>
        Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");

    private static string GetWindowsName()
    {
        using var key = OpenCurrentVersion();
        var productName = key?.GetValue("ProductName") as string ?? "Windows";
        return FormatWindowsName(productName, Environment.OSVersion.Version.Build);
    }

    private static string GetWindowsVersion()
    {
        using var key = OpenCurrentVersion();
        var build = key?.GetValue("CurrentBuildNumber") as string ?? Environment.OSVersion.Version.Build.ToString();
        return FormatWindowsVersion(key?.GetValue("DisplayVersion") as string, build, key?.GetValue("UBR") as int?);
    }

    private static IReadOnlyList<GpuAdapter> GetAdapters()
    {
        var adapters = new List<GpuAdapter>();
        if (CreateDXGIFactory1(typeof(IDXGIFactory1).GUID, out var factory) < 0)
        {
            return adapters;
        }

        try
        {
            for (uint index = 0; factory.EnumAdapters1(index, out var adapter) >= 0; index++)
            {
                try
                {
                    if (adapter.GetDesc1(out var desc) < 0)
                    {
                        continue;
                    }

                    var driver = adapter.CheckInterfaceSupport(typeof(IDXGIDevice).GUID, out var umd) >= 0
                        ? FormatDriverVersion(umd)
                        : null;
                    adapters.Add(new GpuAdapter(
                        desc.Description, driver, IsHardwareAdapter(desc.VendorId, desc.DeviceId, desc.Flags)));
                }
                finally
                {
                    Marshal.ReleaseComObject(adapter);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }

        return adapters;
    }

    private static IReadOnlyList<CodecPackage> GetHevcPackages() =>
        new PackageManager().FindPackagesForUser(string.Empty)
            .Where(package => package.Id.Name.StartsWith("Microsoft.HEVCVideoExtension", StringComparison.OrdinalIgnoreCase))
            .Select(package =>
            {
                var v = package.Id.Version;
                return new CodecPackage(package.Id.Name, $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}");
            })
            .Distinct()
            .ToList();

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(in Guid riid, out IDXGIFactory1 factory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    // Vtable placeholders keep inherited IDXGIObject/IDXGIFactory/IDXGIAdapter slots in order; they are never called.
    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumAdapters();
        void MakeWindowAssociation();
        void GetWindowAssociation();
        void CreateSwapChain();
        void CreateSoftwareAdapter();

        [PreserveSig]
        int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumOutputs();
        void GetDesc();

        [PreserveSig]
        int CheckInterfaceSupport(in Guid interfaceName, out long umdVersion);

        [PreserveSig]
        int GetDesc1(out DxgiAdapterDesc1 desc);
    }

    [ComImport, Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIDevice
    {
    }
}
