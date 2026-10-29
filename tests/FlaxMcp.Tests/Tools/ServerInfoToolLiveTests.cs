using FlaxMcp.Tools;
using Xunit;

namespace FlaxMcp.Tests.Tools;

/// <summary>Tests of what the live fork changed in server_info.</summary>
public sealed class ServerInfoToolLiveTests
{
    /// <summary>The version is the release version, with its prerelease label when it has one.</summary>
    [Fact]
    public void GetServerInfo_ReportsTheReleaseVersionWithItsLabel()
    {
        var tool = new ServerInfoTool(new EditorToolsTests.StubBridgeClient());

        var info = tool.GetServerInfo();

        Assert.Matches(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$", info.Version);
    }
}
