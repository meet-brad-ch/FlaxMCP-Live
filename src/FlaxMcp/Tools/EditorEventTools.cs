using System.ComponentModel;
using FlaxMcp.Flax;
using ModelContextProtocol.Server;

namespace FlaxMcp.Tools;

/// <summary>The editor's recorded events: what happened, in order, instead of reading the log file.</summary>
[McpServerToolType]
public sealed class EditorEventTools
{
    /// <summary>The longest wait editor_wait_for accepts.</summary>
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(10);
    /// <summary>The bridge client that reads the events.</summary>
    private readonly IFlaxEditorEventClient _client;

    /// <summary>Creates the tools.</summary>
    /// <param name="client">Reads the editor's events through the bridge.</param>
    public EditorEventTools(IFlaxEditorEventClient client)
    {
        _client = client;
    }

    /// <summary>Returns the recorded events after a sequence number.</summary>
    /// <param name="sinceSeq">Only events after this sequence number; 0 for all held.</param>
    /// <param name="kinds">Only these kinds; null for all.</param>
    /// <param name="limit">At most this many events, 1 to 500.</param>
    /// <param name="waitSeconds">How long to wait when none match yet, 0 to 2 seconds.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The page of events.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> or <paramref name="waitSeconds"/> is out of range.</exception>
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
        var waitMilliseconds = (int)(waitSeconds * 1000);
        return _client.GetEventsAsync(sinceSeq, kinds, limit, waitMilliseconds, cancellationToken);
    }

    /// <summary>Waits for the first editor event of a kind.</summary>
    /// <param name="kind">The event kind.</param>
    /// <param name="textContains">Only an event whose text contains this (ordinal); null for any.</param>
    /// <param name="sinceSeq">Count events after this sequence number; null to wait for a new one.</param>
    /// <param name="timeoutSeconds">How long to wait, 0 to 600 seconds.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The event, or a timed-out result.</returns>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeoutSeconds"/> is out of range.</exception>
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
