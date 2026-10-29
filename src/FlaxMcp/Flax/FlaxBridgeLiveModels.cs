using System.Text.Json;
using FlaxMcp.Flax.Models;
using ModelContextProtocol;

namespace FlaxMcp.Flax;

/// <summary>The bridge host's own state: its version, the attached plugin, and its memory.</summary>
/// <param name="HostVersion">The version of the bridge host.</param>
/// <param name="HostStartedUtc">When the bridge host started.</param>
/// <param name="Generation">The attached plugin's load time in UTC ticks, or null when no plugin is attached.</param>
/// <param name="Attached">A plugin is attached to the host.</param>
/// <param name="Accepting">The host accepts clients.</param>
/// <param name="AcceptLoopFault">The message of the error that stopped the host's accept loop, or null.</param>
/// <param name="AttachCount">How many times a plugin attached.</param>
/// <param name="DetachCount">How many times a plugin detached.</param>
/// <param name="PluginContextsAlive">The plugin load contexts that are not collected yet.</param>
/// <param name="PluginContextsSeen">The plugin load contexts the host has seen.</param>
/// <param name="Collected">The host ran a full garbage collection before it measured.</param>
/// <param name="PrivateBytes">The editor process's private memory in bytes.</param>
/// <param name="ManagedBytes">The managed heap's size in bytes.</param>
/// <param name="Gen0Collections">The generation 0 garbage collections so far.</param>
/// <param name="Gen1Collections">The generation 1 garbage collections so far.</param>
/// <param name="Gen2Collections">The generation 2 garbage collections so far.</param>
public sealed record FlaxBridgeHostStatus(
    string HostVersion,
    DateTime HostStartedUtc,
    long? Generation,
    bool Attached,
    bool Accepting,
    string? AcceptLoopFault,
    int AttachCount,
    int DetachCount,
    int PluginContextsAlive,
    int PluginContextsSeen,
    bool Collected,
    long PrivateBytes,
    long ManagedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);

/// <summary>An error that the Flax Editor bridge returned for a request, with its error code.</summary>
/// <param name="code">The bridge's error code, for example <c>interrupted_by_reload</c>.</param>
/// <param name="message">The bridge's error message.</param>
public sealed class FlaxBridgeRequestException(string code, string message)
    : McpException($"Flax Editor bridge error [{code}]: {message}")
{
    /// <summary>The bridge's error code.</summary>
    public string Code { get; } = code;
}

/// <summary>The game's registered commands.</summary>
/// <param name="Commands">The commands.</param>
public sealed record FlaxGameCommandList(IReadOnlyList<FlaxGameCommandInfo> Commands);

/// <summary>An actor that a search found.</summary>
/// <param name="Id">The actor's id.</param>
/// <param name="Name">The actor's name.</param>
/// <param name="TypeName">The actor's type.</param>
/// <param name="Path">The actor's path in its scene.</param>
/// <param name="Scripts">The types of the actor's scripts.</param>
public sealed record FlaxFoundActor(string Id, string Name, string TypeName, string Path, IReadOnlyList<string> Scripts);

/// <summary>The actors that a search found.</summary>
/// <param name="MainThreadId">The editor's main thread id, where the search ran.</param>
/// <param name="Actors">The actors found.</param>
/// <param name="Truncated">More actors matched than the limit allowed.</param>
public sealed record FlaxFoundActors(int MainThreadId, IReadOnlyList<FlaxFoundActor> Actors, bool Truncated);

/// <summary>The value of a member of an actor or of one of its scripts.</summary>
/// <param name="MainThreadId">The editor's main thread id, where the member was read or written.</param>
/// <param name="TargetId">The id of the actor or script that holds the member.</param>
/// <param name="TargetType">The type of the actor or script that holds the member.</param>
/// <param name="Member">The member's name.</param>
/// <param name="TypeName">The value's type, or null.</param>
/// <param name="Value">The value as JSON, or null.</param>
/// <param name="RecordedUndo">For a write: the editor recorded an undo step; null for a read.</param>
public sealed record FlaxMemberValue(
    int MainThreadId,
    string TargetId,
    string TargetType,
    string Member,
    string? TypeName,
    JsonElement? Value,
    bool? RecordedUndo = null);

/// <summary>The result of a method call.</summary>
/// <param name="MainThreadId">The editor's main thread id, where the method ran.</param>
/// <param name="Method">The method's name.</param>
/// <param name="TypeName">The type of the returned value, or null.</param>
/// <param name="Result">The returned value as JSON, or null.</param>
public sealed record FlaxMethodResult(int MainThreadId, string Method, string? TypeName, JsonElement? Result);

/// <summary>A script added to an actor.</summary>
/// <param name="MainThreadId">The editor's main thread id, where the script was added.</param>
/// <param name="ActorId">The actor's id.</param>
/// <param name="ScriptId">The script's id.</param>
/// <param name="ScriptType">The script's type.</param>
/// <param name="Added">The script was added.</param>
/// <param name="OrderInParent">The script's position in the actor's scripts.</param>
/// <param name="Scripts">The types of the actor's scripts after the change.</param>
public sealed record FlaxAddedScript(
    int MainThreadId,
    string ActorId,
    string ScriptId,
    string ScriptType,
    bool Added,
    int OrderInParent,
    IReadOnlyList<string> Scripts);

/// <summary>One recorded editor event.</summary>
/// <param name="Seq">Its position in the event sequence, from 1.</param>
/// <param name="Utc">When it was recorded.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Level">The log level for log lines, else 0.</param>
/// <param name="Text">The log message, the scene name, or null.</param>
public sealed record FlaxEditorEvent(long Seq, DateTime Utc, string Kind, int Level, string? Text);

/// <summary>The editor events after a sequence number.</summary>
/// <param name="HostStartedUtc">When the bridge host that recorded the events started.</param>
/// <param name="Events">The events, oldest first.</param>
/// <param name="LastSeq">The newest sequence number recorded (0 when none).</param>
/// <param name="OldestSeq">The oldest sequence number still held.</param>
/// <param name="Truncated">Events after the requested one were overwritten before they were read.</param>
public sealed record FlaxEditorEventPage(
    DateTime HostStartedUtc,
    IReadOnlyList<FlaxEditorEvent> Events,
    long LastSeq,
    long OldestSeq,
    bool Truncated);

/// <summary>The result of a wait for an editor event.</summary>
/// <param name="Matched">An event matched.</param>
/// <param name="Event">The event that matched, or null.</param>
/// <param name="LastSeq">The newest sequence number seen.</param>
/// <param name="TimedOut">The wait ended at its timeout.</param>
/// <param name="Seconds">How long the wait took.</param>
public sealed record FlaxEditorEventWait(bool Matched, FlaxEditorEvent? Event, long LastSeq, bool TimedOut, double Seconds);

/// <summary>A registered game command.</summary>
/// <param name="Name">The command's name.</param>
/// <param name="Description">What the command does.</param>
/// <param name="Owner">Who registered the command.</param>
public sealed record FlaxGameCommandInfo(string Name, string Description, string Owner);

/// <summary>The result of a game command.</summary>
/// <param name="MainThreadId">The editor's main thread id, where the command ran.</param>
/// <param name="Name">The command's name.</param>
/// <param name="Result">The command's text result, or null.</param>
/// <param name="Milliseconds">How long the command ran.</param>
/// <param name="IsPlayMode">The editor was in play mode.</param>
public sealed record FlaxGameCommandResult(int MainThreadId, string Name, string? Result, double Milliseconds, bool IsPlayMode);

/// <summary>The editor's script compilation state.</summary>
/// <param name="IsCompiling">The editor compiles scripts now.</param>
/// <param name="LastCompilationFailed">The last compilation failed.</param>
/// <param name="CompilationsCount">The compilations since the editor started.</param>
/// <param name="LoadedUtcTicks">When the bridge plugin was loaded, in UTC ticks; changes with every script reload.</param>
/// <param name="IsSourceDirty">Changed script sources wait for a compile.</param>
public sealed record FlaxCompileStatus(
    bool IsCompiling,
    bool LastCompilationFailed,
    int CompilationsCount,
    long LoadedUtcTicks,
    bool IsSourceDirty);

/// <summary>The result of a compile in the editor.</summary>
/// <param name="Outcome">"reloaded", "unchanged" or "failed".</param>
/// <param name="Seconds">How long the compile took.</param>
/// <param name="Errors">The compiler errors from the editor's log.</param>
/// <param name="BuildErrors">The build tool's error lines from the editor's log.</param>
/// <param name="Reloads">The script reloads seen.</param>
/// <param name="Check">The status of the check before the compile, or null when there was no check.</param>
public sealed record FlaxEditorCompileResult(
    string Outcome,
    double Seconds,
    IReadOnlyList<FlaxCompilerDiagnostic> Errors,
    IReadOnlyList<string> BuildErrors,
    int Reloads,
    string? Check = null);

/// <summary>The result of opening a scene.</summary>
/// <param name="MainThreadId">The editor's main thread id, where the scene was opened.</param>
/// <param name="SceneId">The opened scene's id.</param>
/// <param name="SceneName">The opened scene's name.</param>
/// <param name="Scenes">The names of the loaded scenes after the change.</param>
public sealed record FlaxEditorOpenSceneResult(int MainThreadId, string SceneId, string SceneName, IReadOnlyList<string> Scenes);

/// <summary>A play mode state: in play mode, and paused.</summary>
/// <param name="IsPlayMode">The editor is in play mode.</param>
/// <param name="IsPaused">The play mode is paused.</param>
internal sealed record FlaxPlayState(bool IsPlayMode, bool IsPaused);
