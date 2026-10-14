using System.ComponentModel;
using FlaxMcp.Flax;
using ModelContextProtocol.Server;

namespace FlaxMcp.Tools;

/// <summary>The editor's recorded events: what happened, in order, instead of reading the log file.</summary>
[McpServerToolType]
public sealed class EditorEventTools
{
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(10);
    private readonly IFlaxEditorEventClient _client;

    public EditorEventTools(IFlaxEditorEventClient client)
    {
        _client = client;
    }

    [McpServerTool(Name = "editor_events", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Returns the editor's recorded events after a sequence number, oldest first. Kinds: log (managed log lines " +
        "only; the engine's native lines such as compiler output stay in the log file), exception, compile_begin, " +
        "compile_succeeded, compile_failed, reload_begin, reload_end, play_begin, play_end, scene_loaded, " +
        "scene_load_error, scene_saved, scene_save_error, scene_unloaded. The bridge host keeps the last 4096 events " +
        "across script reloads; Truncated is true when some after sinceSeq were overwritten. A new HostStartedUtc " +
        "means the editor restarted and the sequence began again."
    )]
    public Task<FlaxEditorEventPage> GetEventsAsync(
        [Description("Only events after this sequence number (LastSeq of an earlier call); 0 for all held.")] long sinceSeq = 0,
        [Description("Only these kinds; omit for all.")] string[]? kinds = null,
        [Description("At most this many events, 1 to 500.")] int limit = 100,
        [Description("When none match yet, wait up to this long for one, 0 to 2 seconds.")] double waitSeconds = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 500);
        ArgumentOutOfRangeException.ThrowIfLessThan(waitSeconds, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(waitSeconds, 2);
        return _client.GetEventsAsync(sinceSeq, kinds, limit, (int)(waitSeconds * 1000), cancellationToken);
    }

    [McpServerTool(Name = "editor_wait_for", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Waits for the first editor event of a kind (see editor_events), optionally with a text, and returns it. " +
        "Without sinceSeq it waits for an event after the call; with it, an event already recorded after that number " +
        "counts. A timeout is a result (TimedOut true), not an error. Example: start play with editor_play_mode, " +
        "then editor_wait_for kind=log textContains=\"[Test]\" to read a game's answer."
    )]
    public Task<FlaxEditorEventWait> WaitForAsync(
        [Description("The event kind, e.g. play_begin, reload_end, log.")] string kind,
        [Description("Only an event whose text contains this (ordinal).")] string? textContains = null,
        [Description("Count events after this sequence number; omit to wait for a new one.")] long? sinceSeq = null,
        [Description("How long to wait, up to 600 seconds.")] double timeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, MaxWait);
        return _client.WaitForEventAsync(kind, textContains, sinceSeq, timeout, cancellationToken);
    }
}
