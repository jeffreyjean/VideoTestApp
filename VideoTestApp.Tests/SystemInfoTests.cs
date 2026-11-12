using Xunit;

namespace VideoTestApp.Tests;

public sealed class SystemInfoTests
{
    [Theory]
    [InlineData("Windows 10 Enterprise", 26300, "Windows 11 Enterprise")]
    [InlineData("Windows 10 Pro", 22000, "Windows 11 Pro")]
    [InlineData("Windows 10 Pro", 19045, "Windows 10 Pro")]
    public void ReportsWindows11LikeWinverDespiteRegistryProductName(string productName, int build, string expected)
    {
        Assert.Equal(expected, SystemInfo.FormatWindowsName(productName, build));
    }

    [Theory]
    [InlineData("26H2", "26300", 9911, "Version 26H2 (OS Build 26300.9911)")]
    [InlineData(null, "17763", 1, "OS Build 17763.1")]
    [InlineData("24H2", "26100", null, "Version 24H2 (OS Build 26100)")]
    public void FormatsVersionLikeWinver(string? displayVersion, string build, int? ubr, string expected)
    {
        Assert.Equal(expected, SystemInfo.FormatWindowsVersion(displayVersion, build, ubr));
    }

    [Fact]
    public void FormatsUserModeDriverVersion()
    {
        Assert.Equal("32.0.101.6881", SystemInfo.FormatDriverVersion((32L << 48) | (0L << 32) | (101L << 16) | 6881));
    }

    [Theory]
    [InlineData(0x8086u, 0x46A6u, 0u, true)]
    [InlineData(0x1414u, 0x8Cu, 2u, false)]
    [InlineData(0x1414u, 0x8Cu, 0u, false)]
    [InlineData(0x10DEu, 0x2684u, 2u, false)]
    public void TreatsBasicRenderDriverAndSoftwareAdaptersAsNoGpu(uint vendor, uint device, uint flags, bool expected)
    {
        Assert.Equal(expected, SystemInfo.IsHardwareAdapter(vendor, device, flags));
    }

    [Fact]
    public void CollectsInformationFromThisMachine()
    {
        var info = SystemInfo.Collect();

        Assert.StartsWith("Windows", info.WindowsName);
        Assert.Contains($"OS Build {Environment.OSVersion.Version.Build}", info.WindowsVersion);
        Assert.NotEmpty(info.Adapters);
        Assert.All(info.Adapters, adapter => Assert.False(string.IsNullOrWhiteSpace(adapter.Name)));
        Assert.All(info.HevcPackages, package => Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", package.Version));
    }
}
