using FlaxMcp.Flax;
using FlaxMcp.Tools;
using ModelContextProtocol.Protocol;
using Xunit;

namespace FlaxMcp.Tests.Tools;

/// <summary>server_info and the MCP handshake give one identity.</summary>
public partial class ServerInfoToolTests
{
    /// <summary>server_info reports exactly the name and version of the handshake.</summary>
    [Fact]
    public void GetServerInfo_ReportsTheHandshakeIdentity()
    {
        var tool = new ServerInfoTool(new StubBridgeClient(FlaxBridgeStatus.Disconnected));

        ServerInfo info = tool.GetServerInfo();
        Implementation identity = ServerIdentity.Create();

        Assert.Equal(identity.Name, info.Name);
        Assert.Equal(identity.Version, info.Version);
    }
}
