using System.ComponentModel;
using FlaxMcp.Flax;
using ModelContextProtocol.Server;

namespace FlaxMcp.Tools;

[McpServerToolType]
public sealed class ServerInfoTool
{
    private readonly IFlaxBridgeClient _bridgeClient;

    public ServerInfoTool(IFlaxBridgeClient bridgeClient)
    {
        _bridgeClient = bridgeClient;
    }

    [McpServerTool(Name = "server_info", ReadOnly = true, UseStructuredContent = true)]
    [Description("Returns the FlaxMcp server name and version.")]
    public ServerInfo GetServerInfo()
    {
        return new ServerInfo(ServerIdentity.Name, ServerIdentity.Version);
    }

    [McpServerTool(Name = "flax_status", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Reports whether the configured project has a reachable Flax Editor bridge and returns its session metadata."
    )]
    public Task<FlaxBridgeStatus> GetFlaxStatusAsync(CancellationToken cancellationToken)
    {
        return _bridgeClient.GetStatusAsync(cancellationToken);
    }
}

public sealed record ServerInfo(string Name, string Version);
