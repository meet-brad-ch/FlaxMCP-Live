using System.ComponentModel;
using FlaxMcp.Flax;
using ModelContextProtocol.Server;

namespace FlaxMcp.Tools;

/// <summary>Commands the running game registered with the bridge (FlaxMcpBridge.GameCommands).</summary>
[McpServerToolType]
public sealed class GameTools
{
    /// <summary>The bridge client that runs the game commands.</summary>
    private readonly IFlaxGameCommandClient _client;

    /// <summary>Creates the tools.</summary>
    /// <param name="client">Talks to the game commands through the bridge.</param>
    public GameTools(IFlaxGameCommandClient client)
    {
        _client = client;
    }

    /// <summary>Lists the registered game commands.</summary>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The commands.</returns>
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

    /// <summary>Runs one registered game command.</summary>
    /// <param name="name">The command name.</param>
    /// <param name="args">The argument string for the command.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The command's text result.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
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
