using System.ComponentModel;
using FlaxMcp.Flax;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace FlaxMcp.Tools;

/// <summary>Editor tools of the live fork: checked compile, scene open and the bridge host's state.</summary>
[McpServerToolType]
public sealed class EditorLiveTools
{
    /// <summary>The editor's bridge.</summary>
    private readonly IFlaxBridgeClient _bridgeClient;

    /// <summary>Runs the compile as an exclusive job.</summary>
    private readonly FlaxJobManager _jobs;

    /// <summary>Compiles the scripts outside the editor before the editor's compile.</summary>
    private readonly IFlaxScriptChecker _checker;

    /// <summary>Creates the tools.</summary>
    /// <param name="bridgeClient">The editor's bridge.</param>
    /// <param name="jobs">The server's job manager.</param>
    /// <param name="checker">Compiles the scripts outside the editor.</param>
    public EditorLiveTools(IFlaxBridgeClient bridgeClient, FlaxJobManager jobs, IFlaxScriptChecker checker)
    {
        _bridgeClient = bridgeClient;
        _jobs = jobs;
        _checker = checker;
    }

    /// <summary>Checks the scripts outside the editor, then compiles them in the editor and waits for the reload.</summary>
    /// <param name="progress">Receives the compile's progress messages; may be null.</param>
    /// <param name="cancellationToken">Stops the wait, not the compile.</param>
    /// <returns>The compile's outcome.</returns>
    /// <exception cref="McpException">The compile is still running after its deadline, or failed.</exception>
    [McpServerTool(Name = "editor_compile", UseStructuredContent = true)]
    [Description(
        "Compiles the project's scripts and returns when the editor has reloaded and settled. The scripts are first " +
        "compiled outside the editor with the compiler arguments Flax.Build last used (build_check_scripts): errors " +
        "come back at once, structured (file, line, column, code), and the editor is not touched. A clean check goes on " +
        "to the editor's compile; its outcome is reloaded, unchanged or failed (build tool lines in BuildErrors). Check " +
        "says how the check went (passed, failed, or unavailable with the reason). Stops play mode first."
    )]
    public Task<FlaxEditorCompileResult> CompileAsync(
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return EditorJobRunner.RunAsync(
            _jobs,
            "compile",
            jobProgress => FlaxCheckedCompile.RunAsync(_checker, _bridgeClient, jobProgress, CancellationToken.None),
            EditorJobRunner.CompileDeadline,
            progress,
            cancellationToken);
    }

    /// <summary>Opens a scene in the editor and waits until it has loaded.</summary>
    /// <param name="path">The scene asset path, absolute or relative to Content/.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The opened scene.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null, empty or white space.</exception>
    [McpServerTool(Name = "editor_open_scene", UseStructuredContent = true)]
    [Description(
        "Opens a scene in the live Flax Editor and returns when it has loaded. The path is absolute or relative to " +
        "the project's Content folder (for example Scenes/Main.scene). Refused during play mode."
    )]
    public Task<FlaxEditorOpenSceneResult> OpenSceneAsync(
        [Description("Scene asset path, absolute or relative to Content/, ending in .scene.")] string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _bridgeClient.OpenSceneAsync(path, cancellationToken);
    }

    /// <summary>Reads the state of the bridge host.</summary>
    /// <param name="collect">Run a full garbage collection before counting the old plugin load contexts.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The host's state.</returns>
    [McpServerTool(Name = "editor_host_status", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Returns the state of the bridge host, the pipe server that outlives script reloads: its version and start " +
        "time, the attached plugin generation, attach and detach counts, the editor's memory and garbage collections, " +
        "and how many old plugin load contexts are still alive (reloads that leak). collect=true runs a full garbage " +
        "collection first so that count is a measurement; it takes long on a large heap."
    )]
    public Task<FlaxBridgeHostStatus> GetHostStatusAsync(
        [Description("Run a full garbage collection before counting the old plugin load contexts.")] bool collect = false,
        CancellationToken cancellationToken = default)
    {
        return _bridgeClient.GetHostStatusAsync(collect, cancellationToken);
    }
}
