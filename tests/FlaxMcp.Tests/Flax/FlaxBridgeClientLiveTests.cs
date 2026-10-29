using System.Globalization;
using System.Text;
using FlaxMcp.Flax;
using ModelContextProtocol;
using Xunit;

namespace FlaxMcp.Tests.Flax;

/// <summary>Tests of the fork's live editor requests in <see cref="FlaxBridgeClient"/>, against a fake bridge.</summary>
public sealed class FlaxBridgeClientLiveTests : IDisposable
{
    /// <summary>The play mode state of an editor that is not in play mode.</summary>
    private const string NotPlaying =
        "{\"id\":1,\"result\":{\"mainThreadId\":7,\"requestedAction\":\"state\",\"isPlayMode\":false,\"isPaused\":false}}";

    /// <summary>The temp folders and the handshake file of the test.</summary>
    private readonly FakeBridgeSession _session = new();

    /// <summary>Deletes the test's temp folders.</summary>
    public void Dispose()
    {
        _session.Dispose();
    }

    /// <summary>The client polls play_mode_state until the editor is in the requested state.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task SetPlayModeAsync_PollsUntilTheEditorReachesTheState()
    {
        string pipeName = FakeBridgeSession.NewPipeName();
        _session.WriteHandshake(pipeName);
        Task serverTask = FakeBridgeServer.ServeSequenceAsync(
            pipeName,
            TestContext.Current.CancellationToken,
            new FakeBridgeStep("play_mode", "{\"id\":1,\"result\":{\"mainThreadId\":7,\"requestedAction\":\"start\",\"isPlayMode\":false,\"isPaused\":false}}"),
            new FakeBridgeStep("play_mode_state", NotPlaying),
            new FakeBridgeStep("play_mode_state", "{\"id\":1,\"result\":{\"mainThreadId\":7,\"requestedAction\":\"state\",\"isPlayMode\":true,\"isPaused\":false}}")
        );
        FlaxBridgeClient client = _session.CreateClient();

        FlaxEditorPlayModeResult result = await client.SetPlayModeAsync("Start", TestContext.Current.CancellationToken);
        await serverTask;

        Assert.True(result.IsPlayMode);
        Assert.Equal("start", result.RequestedAction);
    }

    /// <summary>An unknown play mode action fails before any request.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task SetPlayModeAsync_WithUnknownAction_ThrowsClientVisibleError()
    {
        FlaxBridgeClient client = _session.CreateClient();

        McpException exception = await Assert.ThrowsAsync<McpException>(()
            => client.SetPlayModeAsync("jump", TestContext.Current.CancellationToken));

        Assert.Contains("start, stop, pause, or resume", exception.Message);
    }

    /// <summary>A compile followed by one reload reports "reloaded" with one reload.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task CompileAsync_WhenTheBridgeReloads_ReportsReloaded()
    {
        string pipeName = FakeBridgeSession.NewPipeName();
        _session.WriteHandshake(pipeName);
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task serverTask = FakeBridgeServer.ServeThenRepeatLastAsync(
            pipeName,
            stop.Token,
            new FakeBridgeStep("play_mode_state", NotPlaying),
            new FakeBridgeStep("compile", "{\"id\":1,\"result\":{\"isCompiling\":true,\"lastCompilationFailed\":false,\"compilationsCount\":4,\"loadedUtcTicks\":100}}"),
            new FakeBridgeStep("compile_status", "{\"id\":1,\"result\":{\"isCompiling\":true,\"lastCompilationFailed\":false,\"compilationsCount\":4,\"loadedUtcTicks\":100}}"),
            new FakeBridgeStep("compile_status", "{\"id\":1,\"result\":{\"isCompiling\":false,\"lastCompilationFailed\":false,\"compilationsCount\":5,\"loadedUtcTicks\":200}}"),
            new FakeBridgeStep("compile_status", "{\"id\":1,\"result\":{\"isCompiling\":false,\"lastCompilationFailed\":false,\"compilationsCount\":5,\"loadedUtcTicks\":200}}"),
            new FakeBridgeStep("compile_status", "{\"id\":1,\"result\":{\"isCompiling\":false,\"lastCompilationFailed\":false,\"compilationsCount\":5,\"loadedUtcTicks\":200}}")
        );
        FlaxBridgeClient client = _session.CreateClient();

        FlaxEditorCompileResult result = await client.CompileAsync(null, TestContext.Current.CancellationToken);
        stop.Cancel();
        await serverTask;

        Assert.Equal("reloaded", result.Outcome);
        Assert.Empty(result.Errors);
        Assert.Equal(1, result.Reloads);
    }

    /// <summary>A second compile that the editor starts on its own is waited for and counted.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task CompileAsync_WhenTheEditorCompilesAgainOnItsOwn_WaitsForTheSecondReload()
    {
        string pipeName = FakeBridgeSession.NewPipeName();
        _session.WriteHandshake(pipeName);
        string settled300 = "{\"id\":1,\"result\":{\"isCompiling\":false,\"lastCompilationFailed\":false,\"compilationsCount\":6,\"loadedUtcTicks\":300}}";
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task serverTask = FakeBridgeServer.ServeThenRepeatLastAsync(
            pipeName,
            stop.Token,
            new FakeBridgeStep("play_mode_state", NotPlaying),
            new FakeBridgeStep("compile", "{\"id\":1,\"result\":{\"isCompiling\":false,\"lastCompilationFailed\":false,\"compilationsCount\":4,\"loadedUtcTicks\":100}}"),
            new FakeBridgeStep("compile_status", "{\"id\":1,\"result\":{\"isCompiling\":false,\"lastCompilationFailed\":false,\"compilationsCount\":5,\"loadedUtcTicks\":200}}"),
            new FakeBridgeStep("compile_status", "{\"id\":1,\"result\":{\"isCompiling\":false,\"lastCompilationFailed\":false,\"compilationsCount\":5,\"loadedUtcTicks\":200,\"isSourceDirty\":true}}"),
            new FakeBridgeStep("compile_status", "{\"id\":1,\"result\":{\"isCompiling\":true,\"lastCompilationFailed\":false,\"compilationsCount\":6,\"loadedUtcTicks\":200}}"),
            new FakeBridgeStep("compile_status", settled300),
            new FakeBridgeStep("compile_status", settled300),
            new FakeBridgeStep("compile_status", settled300)
        );
        FlaxBridgeClient client = _session.CreateClient();

        FlaxEditorCompileResult result = await client.CompileAsync(null, TestContext.Current.CancellationToken);
        stop.Cancel();
        await serverTask;

        Assert.Equal("reloaded", result.Outcome);
        Assert.Equal(2, result.Reloads);
    }

    /// <summary>A failed compile reports the compiler and build tool errors that the log shows after the request.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task CompileAsync_WhenTheCompileFails_ReportsTheLogsErrors()
    {
        string pipeName = FakeBridgeSession.NewPipeName();
        _session.WriteHandshake(pipeName);
        string logs = Path.Combine(_session.ProjectFolder, "Logs");
        Directory.CreateDirectory(logs);
        string log = Path.Combine(logs, "Log_2026_10_14_10_00_00.txt");
        File.WriteAllText(log, "[ 00:00:00.500 ]: [Info] an earlier line\n", Encoding.Unicode);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Task serverTask = Task.Run(async () =>
        {
            await FakeBridgeServer.ServeSequenceAsync(
                pipeName,
                cancellationToken,
                new FakeBridgeStep("play_mode_state", NotPlaying),
                new FakeBridgeStep("compile", "{\"id\":1,\"result\":{\"isCompiling\":true,\"lastCompilationFailed\":false,\"compilationsCount\":4,\"loadedUtcTicks\":100}}")
            );
            File.AppendAllText(
                log,
                "[ 00:00:01.000 ]: [Info] C:\\Game\\Source\\A.cs(3,9,3,10): error CS1525: Invalid expression term ';'\n" +
                "[ 00:00:01.100 ]: [Error] Failed to run build tool, result: 1\n",
                Encoding.Unicode
            );
            await FakeBridgeServer.ServeSequenceAsync(
                pipeName,
                cancellationToken,
                new FakeBridgeStep("compile_status", "{\"id\":1,\"result\":{\"isCompiling\":false,\"lastCompilationFailed\":true,\"compilationsCount\":5,\"loadedUtcTicks\":100}}")
            );
        }, cancellationToken);
        FlaxBridgeClient client = _session.CreateClient();

        FlaxEditorCompileResult result = await client.CompileAsync(null, TestContext.Current.CancellationToken);
        await serverTask;

        Assert.Equal("failed", result.Outcome);
        Assert.Contains(result.Errors, error => error.Code == "CS1525");
        Assert.Contains(result.BuildErrors, line => line.Contains("Failed to run build tool"));
    }

    /// <summary>A query that a script reload interrupted is sent again.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task Query_InterruptedByAReload_IsSentAgain()
    {
        string pipeName = FakeBridgeSession.NewPipeName();
        _session.WriteHandshake(pipeName);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Task serverTask = FakeBridgeServer.ServeSequenceAsync(
            pipeName,
            cancellationToken,
            new FakeBridgeStep("get_selection", "{\"id\":1,\"error\":{\"code\":\"interrupted_by_reload\",\"message\":\"reloaded\"}}"),
            new FakeBridgeStep("get_selection", "{\"id\":1,\"result\":{\"mainThreadId\":7,\"selected\":[]}}")
        );
        FlaxBridgeClient client = _session.CreateClient();

        FlaxEditorSelection selection = await client.GetSelectionAsync(cancellationToken);
        await serverTask;

        Assert.Equal(7, selection.MainThreadId);
    }

    /// <summary>A command that a script reload interrupted is not sent again: its effects are unknown.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task Command_InterruptedByAReload_IsNotSentAgain()
    {
        string pipeName = FakeBridgeSession.NewPipeName();
        _session.WriteHandshake(pipeName);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Task serverTask = FakeBridgeServer.ServeSequenceAsync(
            pipeName,
            cancellationToken,
            new FakeBridgeStep("execute_csharp", "{\"id\":1,\"error\":{\"code\":\"interrupted_by_reload\",\"message\":\"effects unknown\"}}")
        );
        FlaxBridgeClient client = _session.CreateClient();

        FlaxBridgeRequestException exception = await Assert.ThrowsAsync<FlaxBridgeRequestException>(() =>
            client.ExecuteCSharpAsync("return 1;", cancellationToken));
        await serverTask;

        Assert.Equal("interrupted_by_reload", exception.Code);
        Assert.Contains("effects unknown", exception.Message);
    }

    /// <summary>A poll that meets the reload (no plugin attached) does not stop the wait.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task CompileAsync_WhenPollingMeetsTheReload_ReportsReloaded()
    {
        string pipeName = FakeBridgeSession.NewPipeName();
        _session.WriteHandshake(pipeName);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task serverTask = FakeBridgeServer.ServeThenRepeatLastAsync(
            pipeName,
            stop.Token,
            new FakeBridgeStep("play_mode_state", NotPlaying),
            new FakeBridgeStep("compile", "{\"id\":1,\"result\":{\"isCompiling\":true,\"lastCompilationFailed\":false,\"compilationsCount\":4,\"loadedUtcTicks\":100}}"),
            new FakeBridgeStep("compile_status", "{\"id\":1,\"error\":{\"code\":\"plugin_not_attached\",\"message\":\"reloading\"}}"),
            new FakeBridgeStep("compile_status", "{\"id\":1,\"result\":{\"isCompiling\":false,\"lastCompilationFailed\":false,\"compilationsCount\":5,\"loadedUtcTicks\":200}}"),
            new FakeBridgeStep("compile_status", "{\"id\":1,\"result\":{\"isCompiling\":false,\"lastCompilationFailed\":false,\"compilationsCount\":5,\"loadedUtcTicks\":200}}"),
            new FakeBridgeStep("compile_status", "{\"id\":1,\"result\":{\"isCompiling\":false,\"lastCompilationFailed\":false,\"compilationsCount\":5,\"loadedUtcTicks\":200}}")
        );
        FlaxBridgeClient client = _session.CreateClient();

        FlaxEditorCompileResult result = await client.CompileAsync(null, cancellationToken);
        stop.Cancel();
        await serverTask;

        Assert.Equal("reloaded", result.Outcome);
    }

    /// <summary>A request to a session whose editor process is gone says so.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task PingAsync_WhenTheEditorProcessIsGone_SaysSo()
    {
        _session.WriteHandshake(FakeBridgeSession.NewPipeName(), int.MaxValue);
        FlaxBridgeClient client = _session.CreateClient();

        McpException exception = await Assert.ThrowsAsync<McpException>(() => client.PingAsync(TestContext.Current.CancellationToken));

        Assert.Contains("is not running", exception.Message);
    }

    /// <summary>The status of a session whose editor process is gone is disconnected, with the reason.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task GetStatusAsync_WhenTheEditorProcessIsGone_ReportsDisconnectedWithTheReason()
    {
        _session.WriteHandshake(FakeBridgeSession.NewPipeName(), int.MaxValue);
        FlaxBridgeClient client = _session.CreateClient();

        FlaxBridgeStatus status = await client.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.False(status.Connected);
        Assert.Contains("is not running", status.Error);
    }

    /// <summary>The host status request sends the collect flag and reads the host's answer.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task GetHostStatusAsync_SendsCollect()
    {
        string pipeName = FakeBridgeSession.NewPipeName();
        _session.WriteHandshake(pipeName);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Task serverTask = FakeBridgeServer.ServeResponseAsync(
            pipeName,
            "{\"id\":1,\"result\":{\"hostVersion\":\"abc\",\"hostStartedUtc\":\"2026-10-14T10:00:00Z\",\"generation\":5," +
            "\"attached\":true,\"accepting\":true,\"acceptLoopFault\":null,\"attachCount\":3,\"detachCount\":2," +
            "\"pluginContextsAlive\":1,\"pluginContextsSeen\":3,\"collected\":true,\"privateBytes\":10,\"managedBytes\":5," +
            "\"gen0Collections\":1,\"gen1Collections\":1,\"gen2Collections\":1}}",
            cancellationToken,
            "host_status",
            "\"collect\":true"
        );
        FlaxBridgeClient client = _session.CreateClient();

        FlaxBridgeHostStatus status = await client.GetHostStatusAsync(true, cancellationToken);
        await serverTask;

        Assert.Equal("abc", status.HostVersion);
        Assert.Equal(3, status.AttachCount);
        Assert.Equal(1, status.PluginContextsAlive);
    }

    /// <summary>An unknown game command gives the bridge's error code and the registered commands.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task RunGameCommandAsync_WithAnUnknownName_ReportsUnknownCommand()
    {
        string pipeName = FakeBridgeSession.NewPipeName();
        _session.WriteHandshake(pipeName);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Task serverTask = FakeBridgeServer.ServeResponseAsync(
            pipeName,
            "{\"id\":1,\"error\":{\"code\":\"unknown_command\",\"message\":\"Registered: status.\"}}",
            cancellationToken,
            "game_command",
            "\"name\":\"stauts\""
        );
        FlaxBridgeClient client = _session.CreateClient();

        FlaxBridgeRequestException exception = await Assert.ThrowsAsync<FlaxBridgeRequestException>(() =>
            client.RunGameCommandAsync("stauts", "", cancellationToken));
        await serverTask;

        Assert.Equal("unknown_command", exception.Code);
        Assert.Contains("Registered: status.", exception.Message);
    }

    /// <summary>A wait for an event skips the events without the text and returns the first match.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task WaitForEventAsync_SkipsEventsWithoutTheTextAndReturnsTheMatch()
    {
        string pipeName = FakeBridgeSession.NewPipeName();
        _session.WriteHandshake(pipeName);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        const string page = "{{\"id\":1,\"result\":{{\"hostStartedUtc\":\"2026-10-14T10:00:00Z\",\"events\":[{0}],\"lastSeq\":{1},\"oldestSeq\":1,\"truncated\":false}}}}";
        Task serverTask = FakeBridgeServer.ServeSequenceAsync(
            pipeName,
            cancellationToken,
            new FakeBridgeStep("events_since", string.Format(CultureInfo.InvariantCulture, page, "", 10)),
            new FakeBridgeStep("events_wait", string.Format(CultureInfo.InvariantCulture, page, "{\"seq\":11,\"utc\":\"2026-10-14T10:00:01Z\",\"kind\":\"log\",\"level\":1,\"text\":\"other\"}", 11)),
            new FakeBridgeStep("events_wait", string.Format(CultureInfo.InvariantCulture, page, "{\"seq\":13,\"utc\":\"2026-10-14T10:00:02Z\",\"kind\":\"log\",\"level\":1,\"text\":\"[Test] status: ok\"}", 13))
        );
        FlaxBridgeClient client = _session.CreateClient();

        FlaxEditorEventWait result = await client.WaitForEventAsync("log", "[Test]", null, TimeSpan.FromSeconds(10), cancellationToken);
        await serverTask;

        Assert.True(result.Matched);
        Assert.Equal(13, result.Event!.Seq);
        Assert.Equal("[Test] status: ok", result.Event.Text);
    }
}
