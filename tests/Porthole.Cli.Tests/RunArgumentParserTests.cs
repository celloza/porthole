using Microsoft.WSL.Containers;
using porthole_cli.Commands;

namespace Porthole.Cli.Tests;

public class RunArgumentParserTests
{
    [Fact]
    public void ParseVolume_WindowsPathAndContainerPath_ParsesCorrectly()
    {
        var mount = RunArgumentParser.ParseVolume("C:\\repo:/workspace");

        Assert.Equal("C:\\repo", mount.HostPath);
        Assert.Equal("/workspace", mount.ContainerPath);
        Assert.False(mount.ReadOnly);
    }

    [Fact]
    public void ParseVolume_ReadOnlyMode_ParsesCorrectly()
    {
        var mount = RunArgumentParser.ParseVolume("D:\\src:/workspace:ro");

        Assert.Equal("D:\\src", mount.HostPath);
        Assert.Equal("/workspace", mount.ContainerPath);
        Assert.True(mount.ReadOnly);
    }

    [Fact]
    public void ParseVolume_NamedVolume_ParsesCorrectly()
    {
        var mount = RunArgumentParser.ParseVolume("named-data:/workspace");

        Assert.Equal("named-data", mount.HostPath);
        Assert.Equal("/workspace", mount.ContainerPath);
        Assert.False(mount.ReadOnly);
    }

    [Fact]
    public void ParseVolume_InvalidFormat_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => RunArgumentParser.ParseVolume("bad-volume"));
    }

    [Fact]
    public void ParsePortMapping_HostContainer_DefaultsToTcp()
    {
        ContainerPortMapping mapping = RunArgumentParser.ParsePortMapping("8080:80");

        Assert.Equal((ushort)8080, mapping.WindowsPort);
        Assert.Equal((ushort)80, mapping.ContainerPort);
        Assert.Equal(PortProtocol.TCP, mapping.Protocol);
    }

    [Fact]
    public void ParsePortMapping_HostIpHostContainerUdp_ParsesCorrectly()
    {
        ContainerPortMapping mapping = RunArgumentParser.ParsePortMapping("127.0.0.1:8081:81/udp");

        Assert.Equal((ushort)8081, mapping.WindowsPort);
        Assert.Equal((ushort)81, mapping.ContainerPort);
        Assert.Equal(PortProtocol.UDP, mapping.Protocol);
    }

    [Fact]
    public void ParsePortMapping_NonNumericPort_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => RunArgumentParser.ParsePortMapping("abc:80"));
    }
}
