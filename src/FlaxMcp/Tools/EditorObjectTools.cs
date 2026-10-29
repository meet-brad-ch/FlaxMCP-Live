using System.ComponentModel;
using System.Text.Json;
using FlaxMcp.Configuration;
using FlaxMcp.Flax;
using ModelContextProtocol.Server;

namespace FlaxMcp.Tools;

/// <summary>Typed access to live objects: what editor_execute_csharp snippets were mostly written for.</summary>
[McpServerToolType]
public sealed class EditorObjectTools
{
    /// <summary>The bridge client that reads and changes the live objects.</summary>
    private readonly IFlaxEditorObjectClient _client;

    /// <summary>Creates the tools.</summary>
    /// <param name="client">Reaches the editor's objects through the bridge.</param>
    public EditorObjectTools(IFlaxEditorObjectClient client)
    {
        _client = client;
    }

    /// <summary>Finds actors in the loaded scenes.</summary>
    /// <param name="nameContains">Part of the actor's name, case-insensitive; null for any.</param>
    /// <param name="actorType">The actor's type; null for any.</param>
    /// <param name="scriptType">A script type the actor has; null for any.</param>
    /// <param name="limit">At most this many, 1 to 500.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The matching actors.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is out of range.</exception>
    [McpServerTool(Name = "editor_find_actors", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Finds actors in the loaded scenes (the play-time scene during play) by name, actor type and script type, all " +
        "optional and combined. Types match by full or short name. Returns id, name, type, path from the scene and " +
        "the scripts' types; Truncated is true when more than the limit matched."
    )]
    public Task<FlaxFoundActors> FindActorsAsync(
        [Description("Part of the actor's name, case-insensitive.")] string? nameContains = null,
        [Description("The actor's type, e.g. StaticModel or FlaxEngine.Camera.")] string? actorType = null,
        [Description("A script type the actor has, e.g. PlayerController.")] string? scriptType = null,
        [Description("At most this many, 1 to 500.")] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 500);
        return _client.FindActorsAsync(nameContains, actorType, scriptType, limit, cancellationToken);
    }

    /// <summary>Reads a field or property of an actor or one of its scripts.</summary>
    /// <param name="actorId">The actor's id.</param>
    /// <param name="member">The dotted member path.</param>
    /// <param name="scriptType">Read from the actor's script of this type; null for the actor.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException"><paramref name="actorId"/> or <paramref name="member"/> is empty.</exception>
    [McpServerTool(Name = "editor_get_member", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Reads a field or property of an actor, or of one of its scripts, by a dotted path (e.g. Clock.Steps). " +
        "Private members count. Unknown names list the members that exist."
    )]
    public Task<FlaxMemberValue> GetMemberAsync(
        [Description("The actor's id (editor_find_actors, editor_scene_graph).")] string actorId,
        [Description("The member path, e.g. Score or Clock.Steps.")] string member,
        [Description("Read from the actor's script of this type instead of the actor.")] string? scriptType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        return _client.GetMemberAsync(actorId, scriptType, member, cancellationToken);
    }

    /// <summary>Writes a field or property of an actor or one of its scripts.</summary>
    /// <param name="actorId">The actor's id.</param>
    /// <param name="member">The dotted member path.</param>
    /// <param name="value">The new value as JSON.</param>
    /// <param name="scriptType">Write to the actor's script of this type; null for the actor.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The value read back.</returns>
    /// <exception cref="ArgumentException"><paramref name="actorId"/> or <paramref name="member"/> is empty.</exception>
    [McpServerTool(Name = "editor_set_member", UseStructuredContent = true)]
    [Description(
        "Writes a field or property of an actor or of one of its scripts by a dotted path; a struct on the way is " +
        "written back. The value is JSON in the member's shape (a number, true, \"text\", an enum name, " +
        "{\"X\":1,\"Y\":2,\"Z\":3}). In edit mode it goes through the editor's undo and marks the scene edited " +
        "(editor_save keeps it); in play mode it lasts until play ends. Returns the value read back."
    )]
    public Task<FlaxMemberValue> SetMemberAsync(
        [Description("The actor's id.")] string actorId,
        [Description("The member path, e.g. Enabled or Settings.Speed.")] string member,
        [Description("The new value as JSON.")] JsonElement value,
        [Description("Write to the actor's script of this type instead of the actor.")] string? scriptType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(member);
        return _client.SetMemberAsync(actorId, scriptType, member, value, cancellationToken);
    }

    /// <summary>Calls a method on the editor's main thread.</summary>
    /// <param name="method">The method's name.</param>
    /// <param name="args">The arguments as a JSON array; null for none.</param>
    /// <param name="actorId">The actor whose (or whose script's) instance method to call; null for a static method.</param>
    /// <param name="scriptType">Call the method on the actor's script of this type; null for the actor.</param>
    /// <param name="typeName">For a static method: the type's name.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The method's result.</returns>
    /// <exception cref="ArgumentException"><paramref name="method"/> is empty.</exception>
    [McpServerTool(Name = "editor_invoke_method", UseStructuredContent = true)]
    [RequiresCodeExecution]
    [Description(
        "Calls a method on the editor's main thread: an instance method of an actor or of one of its scripts (actorId, " +
        "scriptType), or a static method of a type (typeName). Arguments are a JSON array converted to the parameter " +
        "types; the overload with that many parameters that accepts them is called. Runs any code the method runs, " +
        "with the editor's permissions: present only when code execution is allowed."
    )]
    public Task<FlaxMethodResult> InvokeMethodAsync(
        [Description("The method's name.")] string method,
        [Description("The arguments as a JSON array, e.g. [] or [2, \"fast\"].")] JsonElement? args = null,
        [Description("The actor whose (or whose script's) instance method to call.")] string? actorId = null,
        [Description("Call the method on the actor's script of this type.")] string? scriptType = null,
        [Description("For a static method: the type's full or short name.")] string? typeName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        using var emptyArguments = JsonDocument.Parse("[]");
        JsonElement arguments = args ?? emptyArguments.RootElement.Clone();
        return _client.InvokeMethodAsync(typeName, method, actorId, scriptType, arguments, cancellationToken);
    }

    /// <summary>Adds a script of a type to an actor.</summary>
    /// <param name="actorId">The actor's id.</param>
    /// <param name="scriptType">The script type.</param>
    /// <param name="orderInParent">The script's place among the actor's scripts; null for the end.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The added script.</returns>
    /// <exception cref="ArgumentException"><paramref name="actorId"/> or <paramref name="scriptType"/> is empty.</exception>
    [McpServerTool(Name = "editor_add_script", UseStructuredContent = true)]
    [Description(
        "Adds a script of a type to an actor (nothing when it has one already) and, in edit mode, marks the scene " +
        "edited so editor_save keeps it. The type is resolved by full or short name in the loaded assemblies."
    )]
    public Task<FlaxAddedScript> AddScriptAsync(
        [Description("The actor's id.")] string actorId,
        [Description("The script type, e.g. Game.Testing.TestDriver.")] string scriptType,
        [Description("The script's place among the actor's scripts.")] int? orderInParent = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptType);
        return _client.AddScriptAsync(actorId, scriptType, orderInParent, cancellationToken);
    }
}
