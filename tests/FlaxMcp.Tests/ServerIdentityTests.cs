using ModelContextProtocol.Protocol;
using Xunit;

namespace FlaxMcp.Tests;

/// <summary>The identity that the MCP handshake reports.</summary>
public sealed class ServerIdentityTests
{
    /// <summary>The handshake identity has the fork's name and the release version without the revision.</summary>
    [Fact]
    public void Create_GivesTheForkNameAndTheReleaseVersion()
    {
        Implementation identity = ServerIdentity.Create();

        Assert.Equal("FlaxMCP-Live", identity.Name);
        Assert.Equal(ServerIdentity.Version, identity.Version);
        Assert.DoesNotContain('+', identity.Version);
        Assert.False(string.IsNullOrWhiteSpace(identity.Version));
    }
}
