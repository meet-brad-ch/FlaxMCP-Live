using System.ComponentModel;
using FlaxMcp.Flax;
using ModelContextProtocol.Server;

namespace FlaxMcp.Tools;

/// <summary>Commands the running game registered with the bridge (FlaxMcpBridge.GameCommands).</summary>
[McpServerToolType]
public sealed class GameTools
{
    private readonly IFlaxGameCommandClient _client;

    public GameTools(IFlaxGameCommandClient client)
    {
        _client = client;
    }

    [McpServerTool(Name = "game_commands", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Lists the commands the game registered with the bridge (FlaxMcpBridge.GameCommands.Register): name, " +
        "description and the type that registered it. Game scripts usually register while enabled, so the list in " +
        "play mode can differ from the one in edit mode."
    )]
    public Task<FlaxGameCommandList> ListGameCommandsAsync(CancellationToken cancellationToken)
    {
        return _client.ListGameCommandsAsync(cancellationToken);
    }

    [McpServerTool(Name = "game_command", UseStructuredContent = true)]
    [Description(
        "Runs a registered game command on the editor's main thread and returns its text result. Errors: " +
        "unknown_command (lists the registered names), command_failed (the handler's exception type and message)."
    )]
    public Task<FlaxGameCommandResult> RunGameCommandAsync(
        [Description("The command name, as game_commands lists it.")] string name,
        [Description("The argument string the command's description asks for.")] string args = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _client.RunGameCommandAsync(name, args, cancellationToken);
    }
}
