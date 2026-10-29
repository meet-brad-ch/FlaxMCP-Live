using System.Diagnostics;
using System.Text.Json;
using FlaxMcp.Flax.Models;
using ModelContextProtocol;

namespace FlaxMcp.Flax;

/// <summary>The live editor requests of this fork: compile, open a scene, and the bridge host's status.</summary>
public partial interface IFlaxBridgeClient
{
    /// <summary>Compiles the scripts in the editor and waits until the editor has settled.</summary>
    /// <param name="progress">Receives what the compile is doing.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The compile's outcome, its errors, and the reloads seen.</returns>
    Task<FlaxEditorCompileResult> CompileAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Opens a scene in the editor.</summary>
    /// <param name="path">The scene asset's path.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The opened scene and the loaded scenes.</returns>
    Task<FlaxEditorOpenSceneResult> OpenSceneAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Reads the bridge host's status.</summary>
    /// <param name="collect">Run a full garbage collection before the host measures.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The bridge host's status.</returns>
    Task<FlaxBridgeHostStatus> GetHostStatusAsync(bool collect, CancellationToken cancellationToken = default);
}

/// <summary>The game's registered commands (the bridge's runtime module, FlaxMcpBridge.GameCommands).</summary>
public interface IFlaxGameCommandClient
{
    /// <summary>Lists the registered game commands.</summary>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The registered commands.</returns>
    Task<FlaxGameCommandList> ListGameCommandsAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs a game command on the editor's main thread.</summary>
    /// <param name="name">The command's name.</param>
    /// <param name="args">The command's arguments as one text.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The command's result.</returns>
    Task<FlaxGameCommandResult> RunGameCommandAsync(string name, string args, CancellationToken cancellationToken = default);
}

/// <summary>The editor's recorded events (the bridge host's ring: log lines, compiles, reloads, play mode, scenes).</summary>
public interface IFlaxEditorEventClient
{
    /// <summary>The events after a sequence number, optionally waiting up to 2 seconds for one.</summary>
    /// <param name="afterSeq">Return only events after this sequence number.</param>
    /// <param name="kinds">The event kinds to return, or null for all.</param>
    /// <param name="max">The maximum number of events to return.</param>
    /// <param name="waitMilliseconds">How long the host waits for an event when there is none; 0 does not wait.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>A page of events.</returns>
    Task<FlaxEditorEventPage> GetEventsAsync(
        long afterSeq, IReadOnlyList<string>? kinds, int max, int waitMilliseconds, CancellationToken cancellationToken = default);

    /// <summary>Waits for the first event of a kind (and text) after a sequence number, or the current end.</summary>
    /// <param name="kind">The event kind to wait for.</param>
    /// <param name="textContains">Text that the event must contain, or null for any event of the kind.</param>
    /// <param name="afterSeq">Wait for events after this sequence number, or null for events after the current end.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The matching event, or a timed-out result.</returns>
    Task<FlaxEditorEventWait> WaitForEventAsync(
        string kind, string? textContains, long? afterSeq, TimeSpan timeout, CancellationToken cancellationToken = default);
}

/// <summary>Typed access to live objects: find actors, read and write members, call methods, add scripts.</summary>
public interface IFlaxEditorObjectClient
{
    /// <summary>Finds actors by name, actor type and script type.</summary>
    /// <param name="nameContains">Text that the actor's name must contain, or null.</param>
    /// <param name="actorType">The actor type, or null.</param>
    /// <param name="scriptType">A script type that the actor must have, or null.</param>
    /// <param name="limit">The maximum number of actors to return.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The actors found.</returns>
    Task<FlaxFoundActors> FindActorsAsync(
        string? nameContains, string? actorType, string? scriptType, int limit, CancellationToken cancellationToken = default);

    /// <summary>Reads a member of an actor or of one of its scripts.</summary>
    /// <param name="actorId">The actor's id.</param>
    /// <param name="scriptType">The script's type, or null to read a member of the actor.</param>
    /// <param name="member">The member's name.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The member's value.</returns>
    Task<FlaxMemberValue> GetMemberAsync(string actorId, string? scriptType, string member, CancellationToken cancellationToken = default);

    /// <summary>Writes a member of an actor or of one of its scripts.</summary>
    /// <param name="actorId">The actor's id.</param>
    /// <param name="scriptType">The script's type, or null to write a member of the actor.</param>
    /// <param name="member">The member's name.</param>
    /// <param name="value">The new value as JSON.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The member's value after the write.</returns>
    Task<FlaxMemberValue> SetMemberAsync(
        string actorId, string? scriptType, string member, JsonElement value, CancellationToken cancellationToken = default);

    /// <summary>Calls an instance or static method.</summary>
    /// <param name="typeName">The type for a static method, or null.</param>
    /// <param name="method">The method's name.</param>
    /// <param name="actorId">The actor's id for an instance method, or null.</param>
    /// <param name="scriptType">The script's type for a method of a script, or null.</param>
    /// <param name="args">The arguments as a JSON array.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The method's result.</returns>
    Task<FlaxMethodResult> InvokeMethodAsync(
        string? typeName, string method, string? actorId, string? scriptType, JsonElement args, CancellationToken cancellationToken = default);

    /// <summary>Adds a script to an actor.</summary>
    /// <param name="actorId">The actor's id.</param>
    /// <param name="scriptType">The script's type.</param>
    /// <param name="orderInParent">The script's position in the actor's scripts, or null for the end.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The added script.</returns>
    Task<FlaxAddedScript> AddScriptAsync(string actorId, string scriptType, int? orderInParent, CancellationToken cancellationToken = default);
}

/// <summary>
/// The live editor requests of this fork: waits for play mode and compiles, events, game commands, typed object access,
/// the bridge host's status, the check that the editor process runs, and the second try of a query after a reload.
/// </summary>
public sealed partial class FlaxBridgeClient : IFlaxGameCommandClient, IFlaxEditorEventClient, IFlaxEditorObjectClient
{
    /// <summary>The longest wait of one events_wait request.</summary>
    private const int EventWaitMilliseconds = 2000;

    /// <summary>The events in one page of a wait for an event.</summary>
    private const int EventPageSize = 500;

    /// <summary>The error code of a request that a script reload interrupted.</summary>
    internal const string InterruptedByReload = "interrupted_by_reload";

    /// <summary>The error code of a request sent while no plugin is attached to the bridge host.</summary>
    internal const string PluginNotAttached = "plugin_not_attached";

    /// <summary>Requests that only read: one interrupted by a script reload is sent again to the reloaded plugin.</summary>
    private static readonly HashSet<string> QueryMethods =
    [
        "ping", "scene_graph", "get_selection", "actor_details", "static_model_details", "material_details",
        "box_collider_details", "play_mode_state", "compile_status", "host_status", "game_commands", "events_since",
        "find_actors", "get_member",
    ];

    /// <summary>The longest wait for the editor to reach a requested play mode state.</summary>
    private static readonly TimeSpan PlayModeWait = TimeSpan.FromSeconds(30);

    /// <summary>The longest wait for a compile to settle.</summary>
    private static readonly TimeSpan CompileWait = TimeSpan.FromSeconds(120);

    /// <summary>A compile without a reload in this time did not change anything.</summary>
    private static readonly TimeSpan NothingToReloadAfter = TimeSpan.FromSeconds(3);

    /// <summary>The time between two state polls.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>A compile is settled when no compile and no reload happened for this time.</summary>
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(500);

    /// <summary>Requests a play mode action and waits until the editor reaches its state.</summary>
    /// <param name="action">start, stop, pause or resume (any case).</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <param name="progress">Receives what the wait is doing.</param>
    /// <returns>The editor's play mode state, with the requested action in lower case.</returns>
    /// <exception cref="McpException">The action is unknown, or the editor did not reach the state in time.</exception>
    public async Task<FlaxEditorPlayModeResult> SetPlayModeAsync(
        string action,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        FlaxPlayState expected = ExpectedPlayState(action);
        Stopwatch watch = Stopwatch.StartNew();
        FlaxEditorPlayModeResult state = await RequestPlayModeAsync(action, cancellationToken);
        DateTime deadline = DateTime.UtcNow + PlayModeWait;
        while (state.IsPlayMode != expected.IsPlayMode || state.IsPaused != expected.IsPaused)
        {
            progress?.Report($"waiting for the editor to {action.ToLowerInvariant()} play mode ({watch.Elapsed.TotalSeconds:0} s)");
            if (DateTime.UtcNow > deadline)
            {
                throw new McpException(
                    $"The Flax Editor did not complete the '{action}' play mode action within " +
                    $"{PlayModeWait.TotalSeconds:0} seconds (play mode {state.IsPlayMode}, paused {state.IsPaused})."
                );
            }

            await Task.Delay(PollInterval, cancellationToken);
            FlaxEditorPlayModeResult? polled = await TryCallAsync<FlaxEditorPlayModeResult>("play_mode_state", cancellationToken);
            if (polled is not null)
            {
                state = polled;
            }
        }

        string requestedAction = action.ToLowerInvariant();
        return state with { RequestedAction = requestedAction };
    }

    /// <summary>The play mode state that an action leads to.</summary>
    /// <param name="action">start, stop, pause or resume (any case).</param>
    /// <returns>The state after the action.</returns>
    /// <exception cref="McpException">The action is unknown.</exception>
    private static FlaxPlayState ExpectedPlayState(string action)
    {
        return action.ToLowerInvariant() switch
        {
            "start" => new FlaxPlayState(true, false),
            "stop" => new FlaxPlayState(false, false),
            "pause" => new FlaxPlayState(true, true),
            "resume" => new FlaxPlayState(true, false),
            _ => throw new McpException("Play mode action must be start, stop, pause, or resume."),
        };
    }

    /// <inheritdoc />
    public async Task<FlaxEditorCompileResult> CompileAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        Stopwatch watch = Stopwatch.StartNew();
        int logMark = CurrentLogLength();
        FlaxEditorPlayModeResult playState =
            await CallAsync<FlaxEditorPlayModeResult>(RequireHandshake(), "play_mode_state", null, cancellationToken);
        if (playState.IsPlayMode)
        {
            await SetPlayModeAsync("stop", cancellationToken);
        }

        FlaxCompileStatus? started = await RequestCompileAsync(cancellationToken);
        progress?.Report("compiling");
        CompileWaitState wait = StartCompileWait(started, logMark, watch);
        return await WaitForCompileAsync(wait, progress, cancellationToken);
    }

    /// <summary>The length of the editor's newest log, so that a compile reads only the lines after it.</summary>
    /// <returns>The log's length in characters, or 0 when there is no log.</returns>
    private int CurrentLogLength()
    {
        FileInfo? logFile = FlaxLogReader.FindLatestLogFile(_projectFolder);
        if (logFile is null)
        {
            return 0;
        }

        return FlaxLogReader.ReadLog(logFile.FullName).Length;
    }

    /// <summary>Sends the compile request.</summary>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The compile state at the request, or null when a script reload interrupted the request.</returns>
    private async Task<FlaxCompileStatus?> RequestCompileAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await CallAsync<FlaxCompileStatus>(RequireHandshake(), "compile", null, cancellationToken);
        }
        catch (FlaxBridgeRequestException ex) when (ex.Code == InterruptedByReload)
        {
            return null;
        }
    }

    /// <summary>The start of a wait for a compile.</summary>
    /// <param name="started">The compile state at the request, or null when a reload interrupted the request.</param>
    /// <param name="logMark">The log's length before the request.</param>
    /// <param name="watch">Runs since the compile started.</param>
    /// <returns>The wait's state before the first poll.</returns>
    private static CompileWaitState StartCompileWait(FlaxCompileStatus? started, int logMark, Stopwatch watch)
    {
        CompileWaitState wait = new(started, logMark, watch);
        if (started is null)
        {
            // A compile request interrupted by a reload: the reload is the first one.
            wait.Reloads = 1;
            wait.Compiled = true;
            return wait;
        }

        wait.LoadedUtcTicks = started.LoadedUtcTicks;
        wait.Compiled = started.IsCompiling || started.IsSourceDirty;
        return wait;
    }

    /// <summary>
    /// Waits until the editor has settled after a compile request: not compiling, no changed sources pending, and no
    /// further reload for <see cref="SettleTime"/>. The editor compiles changed sources on its own as well, so one
    /// request can be followed by more than one reload; the result counts them.
    /// </summary>
    /// <param name="wait">The wait's state.</param>
    /// <param name="progress">Receives the reloads.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The compile's result.</returns>
    /// <exception cref="McpException">The editor did not settle within <see cref="CompileWait"/>.</exception>
    private async Task<FlaxEditorCompileResult> WaitForCompileAsync(
        CompileWaitState wait,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + CompileWait;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, cancellationToken);
            FlaxCompileStatus? status = await TryCallAsync<FlaxCompileStatus>("compile_status", cancellationToken);
            FlaxEditorCompileResult? result = ReadCompilePoll(wait, status, progress);
            if (result is not null)
            {
                return result;
            }
        }

        throw new McpException($"The Flax Editor did not finish compiling within {CompileWait.TotalSeconds:0} seconds.");
    }

    /// <summary>Takes one polled compile state into the wait.</summary>
    /// <param name="wait">The wait's state.</param>
    /// <param name="status">The polled state, or null when the poll got no answer.</param>
    /// <param name="progress">Receives the reloads.</param>
    /// <returns>The compile's result when the wait is over, else null.</returns>
    private FlaxEditorCompileResult? ReadCompilePoll(CompileWaitState wait, FlaxCompileStatus? status, IProgress<string>? progress)
    {
        if (status is null)
        {
            wait.SettledSince = null;
            return null;
        }

        CountReload(wait, status, progress);
        if (status.IsCompiling || status.IsSourceDirty)
        {
            wait.Compiled = true;
            wait.SettledSince = null;
            return null;
        }

        return ReadSettledCompile(wait, status);
    }

    /// <summary>Counts a script reload: the plugin's load time changed since the last poll.</summary>
    /// <param name="wait">The wait's state.</param>
    /// <param name="status">The polled state.</param>
    /// <param name="progress">Receives the reload.</param>
    private static void CountReload(CompileWaitState wait, FlaxCompileStatus status, IProgress<string>? progress)
    {
        bool reloaded = wait.LoadedUtcTicks is not null && status.LoadedUtcTicks != wait.LoadedUtcTicks;
        if (reloaded)
        {
            wait.Reloads++;
            progress?.Report($"reloaded {wait.Reloads}x, waiting for the editor to settle");
        }

        wait.LoadedUtcTicks = status.LoadedUtcTicks;
    }

    /// <summary>Reads the result of a compile that the editor no longer runs.</summary>
    /// <param name="wait">The wait's state.</param>
    /// <param name="status">The polled state: not compiling, no changed sources pending.</param>
    /// <returns>The compile's result when the wait is over, else null.</returns>
    private FlaxEditorCompileResult? ReadSettledCompile(CompileWaitState wait, FlaxCompileStatus status)
    {
        bool compiledSinceRequest = wait.Started is not null && status.CompilationsCount > wait.Started.CompilationsCount;
        if (compiledSinceRequest)
        {
            wait.Compiled = true;
        }

        double seconds = wait.Watch.Elapsed.TotalSeconds;
        if (wait.Compiled && status.LastCompilationFailed)
        {
            return Failed(wait.LogMark, seconds, wait.Reloads);
        }

        if (wait.Reloads == 0)
        {
            return ReadUnchangedCompile(wait);
        }

        wait.SettledSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - wait.SettledSince < SettleTime)
        {
            return null;
        }

        return new FlaxEditorCompileResult("reloaded", seconds, [], [], wait.Reloads);
    }

    /// <summary>Reads the result of a compile without a reload so far.</summary>
    /// <param name="wait">The wait's state.</param>
    /// <returns>An "unchanged" result after <see cref="NothingToReloadAfter"/>, else null.</returns>
    private static FlaxEditorCompileResult? ReadUnchangedCompile(CompileWaitState wait)
    {
        if (wait.Watch.Elapsed <= NothingToReloadAfter)
        {
            return null;
        }

        double seconds = wait.Watch.Elapsed.TotalSeconds;
        return new FlaxEditorCompileResult("unchanged", seconds, [], [], 0);
    }

    /// <summary>The result of a failed compile, with the errors that the editor's log shows after the request.</summary>
    /// <param name="logMark">The log's length before the request.</param>
    /// <param name="seconds">How long the compile took.</param>
    /// <param name="reloads">The script reloads seen.</param>
    /// <returns>A "failed" result.</returns>
    private FlaxEditorCompileResult Failed(int logMark, double seconds, int reloads)
    {
        FileInfo? logFile = FlaxLogReader.FindLatestLogFile(_projectFolder);
        string log = "";
        if (logFile is not null)
        {
            log = FlaxLogReader.ReadLog(logFile.FullName);
        }

        string since = log;
        if (logMark <= log.Length)
        {
            since = log[logMark..];
        }

        FlaxCompilerDiagnostic[] errors =
            FlaxCompilerDiagnosticParser.Parse(since).Where(diagnostic => diagnostic.Severity == "error").ToArray();
        string[] buildErrors = since.Split('\n')
            .Where(line => line.Contains("Sharing violation", StringComparison.Ordinal) ||
                line.Contains("Failed to run build tool", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .ToArray();
        return new FlaxEditorCompileResult("failed", seconds, errors, buildErrors, reloads);
    }

    /// <inheritdoc />
    public Task<FlaxEditorOpenSceneResult> OpenSceneAsync(string path, CancellationToken cancellationToken = default)
    {
        return CallAsync<FlaxEditorOpenSceneResult>(RequireHandshake(), "open_scene", new { path }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<FlaxBridgeHostStatus> GetHostStatusAsync(bool collect, CancellationToken cancellationToken = default)
    {
        return CallAsync<FlaxBridgeHostStatus>(RequireHandshake(), "host_status", new { collect }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<FlaxEditorEventPage> GetEventsAsync(
        long afterSeq, IReadOnlyList<string>? kinds, int max, int waitMilliseconds, CancellationToken cancellationToken = default)
    {
        string method = "events_since";
        if (waitMilliseconds > 0)
        {
            method = "events_wait";
        }

        return CallAsync<FlaxEditorEventPage>(
            RequireHandshake(),
            method,
            new { afterSeq, kinds, max, waitMs = waitMilliseconds },
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async Task<FlaxEditorEventWait> WaitForEventAsync(
        string kind, string? textContains, long? afterSeq, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Stopwatch watch = Stopwatch.StartNew();
        long after = afterSeq ?? (await GetEventsAsync(0, [kind], 1, 0, cancellationToken)).LastSeq;
        while (true)
        {
            TimeSpan left = timeout - watch.Elapsed;
            int wait = (int)Math.Clamp(left.TotalMilliseconds, 1, EventWaitMilliseconds);
            FlaxEditorEventPage page = await GetEventsAsync(after, [kind], EventPageSize, wait, cancellationToken);
            FlaxEditorEvent? match = FirstEventWithText(page, textContains);
            if (match is not null)
            {
                return new FlaxEditorEventWait(true, match, page.LastSeq, false, watch.Elapsed.TotalSeconds);
            }

            after = NextEventSeq(page, after);
            if (watch.Elapsed >= timeout)
            {
                return new FlaxEditorEventWait(false, null, page.LastSeq, true, watch.Elapsed.TotalSeconds);
            }
        }
    }

    /// <summary>The first event of a page that contains a text.</summary>
    /// <param name="page">The page.</param>
    /// <param name="textContains">The text, or null for any event.</param>
    /// <returns>The first matching event, or null.</returns>
    private static FlaxEditorEvent? FirstEventWithText(FlaxEditorEventPage page, string? textContains)
    {
        foreach (FlaxEditorEvent item in page.Events)
        {
            if (textContains is null || item.Text?.Contains(textContains, StringComparison.Ordinal) == true)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>
    /// The sequence number after which the next page starts: a full page may hold more matches than it returned, so
    /// the next page starts at its last event, else at the end.
    /// </summary>
    /// <param name="page">The page just read.</param>
    /// <param name="after">The sequence number that the page started after.</param>
    /// <returns>The sequence number for the next request.</returns>
    private static long NextEventSeq(FlaxEditorEventPage page, long after)
    {
        if (page.Events.Count == EventPageSize)
        {
            return page.Events[^1].Seq;
        }

        return Math.Max(after, page.LastSeq);
    }

    /// <inheritdoc />
    public Task<FlaxFoundActors> FindActorsAsync(
        string? nameContains, string? actorType, string? scriptType, int limit, CancellationToken cancellationToken = default)
    {
        return CallAsync<FlaxFoundActors>(RequireHandshake(), "find_actors", new { nameContains, actorType, scriptType, limit }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<FlaxMemberValue> GetMemberAsync(string actorId, string? scriptType, string member, CancellationToken cancellationToken = default)
    {
        return CallAsync<FlaxMemberValue>(RequireHandshake(), "get_member", new { actorId, scriptType, member }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<FlaxMemberValue> SetMemberAsync(
        string actorId, string? scriptType, string member, JsonElement value, CancellationToken cancellationToken = default)
    {
        return CallAsync<FlaxMemberValue>(RequireHandshake(), "set_member", new { actorId, scriptType, member, value }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<FlaxMethodResult> InvokeMethodAsync(
        string? typeName, string method, string? actorId, string? scriptType, JsonElement args, CancellationToken cancellationToken = default)
    {
        return CallAsync<FlaxMethodResult>(
            RequireHandshake(), "invoke_method", new { typeName, method, actorId, scriptType, args }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<FlaxAddedScript> AddScriptAsync(string actorId, string scriptType, int? orderInParent, CancellationToken cancellationToken = default)
    {
        return CallAsync<FlaxAddedScript>(RequireHandshake(), "add_script", new { actorId, scriptType, orderInParent }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<FlaxGameCommandList> ListGameCommandsAsync(CancellationToken cancellationToken = default)
    {
        return CallAsync<FlaxGameCommandList>(RequireHandshake(), "game_commands", null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<FlaxGameCommandResult> RunGameCommandAsync(string name, string args, CancellationToken cancellationToken = default)
    {
        return CallAsync<FlaxGameCommandResult>(RequireHandshake(), "game_command", new { name, args }, cancellationToken);
    }

    /// <summary>Sends a request without parameters for a poll; a failure that a reload or a lost session explains gives null.</summary>
    /// <typeparam name="T">The result's type.</typeparam>
    /// <param name="method">The request's method.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The result, or null when there is no session or the request failed for one of those reasons.</returns>
    private async Task<T?> TryCallAsync<T>(string method, CancellationToken cancellationToken) where T : class
    {
        FlaxBridgeHandshake? handshake = ReadHandshake();
        if (handshake is null)
        {
            return null;
        }

        try
        {
            return await CallAsync<T>(handshake, method, null, cancellationToken);
        }
        catch (McpException ex) when (ex.InnerException is IOException or OperationCanceledException)
        {
            return null;
        }
        catch (FlaxBridgeRequestException ex) when (ex.Code is "request_timeout" or InterruptedByReload or PluginNotAttached)
        {
            return null;
        }
    }

    /// <summary>Sends a request; a query that a script reload interrupted is sent again to the reloaded plugin.</summary>
    /// <typeparam name="T">The result's type.</typeparam>
    /// <param name="handshake">The bridge session.</param>
    /// <param name="method">The request's method.</param>
    /// <param name="parameters">The request's parameters, or null.</param>
    /// <param name="cancellationToken">Stops the request.</param>
    /// <returns>The result.</returns>
    /// <exception cref="FlaxBridgeRequestException">The bridge returned an error.</exception>
    /// <exception cref="McpException">The bridge did not answer.</exception>
    private async Task<T> CallAsync<T>(
        FlaxBridgeHandshake handshake,
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync<T>(handshake, method, parameters, cancellationToken);
        }
        catch (FlaxBridgeRequestException ex) when (ex.Code == InterruptedByReload && QueryMethods.Contains(method))
        {
            // The bridge host waits for the reloaded plugin before it answers the second request.
            return await SendAsync<T>(RequireHandshake(), method, parameters, cancellationToken);
        }
    }

    /// <summary>Checks that the editor process that opened the bridge session runs.</summary>
    /// <param name="handshake">The bridge session.</param>
    /// <returns>The same session.</returns>
    /// <exception cref="McpException">The editor process does not run.</exception>
    private static FlaxBridgeHandshake RequireEditorRunning(FlaxBridgeHandshake handshake)
    {
        if (!IsEditorRunning(handshake))
        {
            throw new McpException(EditorNotRunningMessage(handshake));
        }

        return handshake;
    }

    /// <summary>The status of a bridge session whose editor process does not run.</summary>
    /// <param name="handshake">The bridge session.</param>
    /// <returns>A disconnected status with the reason.</returns>
    private static FlaxBridgeStatus EditorNotRunningStatus(FlaxBridgeHandshake handshake)
    {
        string message = EditorNotRunningMessage(handshake);
        return FlaxBridgeStatus.Disconnected with { Error = message };
    }

    /// <summary>The error message for a bridge session whose editor process does not run.</summary>
    /// <param name="handshake">The bridge session.</param>
    /// <returns>The message.</returns>
    private static string EditorNotRunningMessage(FlaxBridgeHandshake handshake)
    {
        return $"The Flax Editor that opened the bridge session (process {handshake.Pid}) is not running.";
    }

    /// <summary>Tells whether the editor process that opened the bridge session runs.</summary>
    /// <param name="handshake">The bridge session.</param>
    /// <returns>True when the process runs.</returns>
    private static bool IsEditorRunning(FlaxBridgeHandshake handshake)
    {
        try
        {
            using Process process = Process.GetProcessById(handshake.Pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            // GetProcessById's answer for a process id that is not running.
            return false;
        }
    }

    /// <summary>The state of a wait for a compile, carried from one poll to the next.</summary>
    /// <param name="started">The compile state at the request, or null when a reload interrupted the request.</param>
    /// <param name="logMark">The log's length before the request.</param>
    /// <param name="watch">Runs since the compile started.</param>
    private sealed class CompileWaitState(FlaxCompileStatus? started, int logMark, Stopwatch watch)
    {
        /// <summary>The compile state at the request, or null when a reload interrupted the request.</summary>
        public FlaxCompileStatus? Started { get; } = started;

        /// <summary>The log's length before the request.</summary>
        public int LogMark { get; } = logMark;

        /// <summary>Runs since the compile started.</summary>
        public Stopwatch Watch { get; } = watch;

        /// <summary>The script reloads seen.</summary>
        public int Reloads { get; set; }

        /// <summary>The plugin's load time at the last answered poll, or null before the first one.</summary>
        public long? LoadedUtcTicks { get; set; }

        /// <summary>The editor compiled since the request.</summary>
        public bool Compiled { get; set; }

        /// <summary>When the editor was last seen settled without a break, or null.</summary>
        public DateTime? SettledSince { get; set; }
    }
}
