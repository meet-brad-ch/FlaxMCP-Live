using System.ComponentModel;
using FlaxMcp.Flax;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace FlaxMcp.Tools;

/// <summary>Long operations as jobs: start one, read its state, wait for it with progress notifications.</summary>
[McpServerToolType]
public sealed class JobTools
{
    /// <summary>The longest wait job_wait accepts.</summary>
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(30);

    /// <summary>Runs and tracks the jobs.</summary>
    private readonly FlaxJobManager _jobs;

    /// <summary>The editor's bridge.</summary>
    private readonly IFlaxBridgeClient _bridgeClient;

    /// <summary>Compiles the scripts outside the editor before a compile job.</summary>
    private readonly IFlaxScriptChecker _checker;

    /// <summary>Creates the tools.</summary>
    /// <param name="jobs">Runs and tracks the jobs.</param>
    /// <param name="bridgeClient">The editor's bridge.</param>
    /// <param name="checker">Compiles the scripts outside the editor.</param>
    public JobTools(FlaxJobManager jobs, IFlaxBridgeClient bridgeClient, IFlaxScriptChecker checker)
    {
        _jobs = jobs;
        _bridgeClient = bridgeClient;
        _checker = checker;
    }

    /// <summary>Starts a long editor operation as a job.</summary>
    /// <param name="kind">compile, play_start, play_stop, play_pause, play_resume or open_scene.</param>
    /// <param name="path">For open_scene: the scene path.</param>
    /// <returns>The job's handle.</returns>
    /// <exception cref="McpException">The kind is unknown, open_scene has no path, or another editor job runs.</exception>
    [McpServerTool(Name = "editor_job_start", UseStructuredContent = true)]
    [Description(
        "Starts a long editor operation as a job and returns at once: compile (compile scripts and wait until the " +
        "editor has reloaded and settled), play_start, play_stop, play_pause, play_resume, open_scene (path). Editor " +
        "jobs run one at a time: another one while one runs fails with job_conflict naming it. Follow the job with " +
        "job_wait or job_status; its result is what editor_compile, editor_play_mode or editor_open_scene return."
    )]
    public FlaxJobHandle StartEditorJob(
        [Description("compile, play_start, play_stop, play_pause, play_resume or open_scene.")] string kind,
        [Description("For open_scene: the scene path, absolute or relative to Content/.")] string? path = null)
    {
        if (kind == "open_scene" && string.IsNullOrWhiteSpace(path))
        {
            throw new McpException("open_scene needs a path.");
        }

        Func<FlaxJobProgress, CancellationToken, Task<object>> run = BuildRun(kind, path);
        return _jobs.Start(kind, exclusive: true, run);
    }

    /// <summary>Builds the work of an editor job.</summary>
    /// <param name="kind">The job kind.</param>
    /// <param name="path">For open_scene: the scene path.</param>
    /// <returns>The work: it reports progress and returns the result object.</returns>
    /// <exception cref="McpException">The kind is unknown.</exception>
    private Func<FlaxJobProgress, CancellationToken, Task<object>> BuildRun(string kind, string? path)
    {
        if (kind == "compile")
        {
            return CompileRunAsync;
        }

        if (kind is "play_start" or "play_stop" or "play_pause" or "play_resume")
        {
            string action = kind["play_".Length..];
            return (progress, cancellationToken) => PlayRunAsync(action, progress, cancellationToken);
        }

        if (kind == "open_scene")
        {
            return (progress, cancellationToken) => OpenSceneRunAsync(path!, progress, cancellationToken);
        }

        throw new McpException(
            $"Unknown editor job kind '{kind}'. Kinds: compile, play_start, play_stop, play_pause, play_resume, open_scene.");
    }

    /// <summary>The compile job: the script check, then the editor's compile.</summary>
    /// <param name="progress">Receives what the compile is doing.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The compile result.</returns>
    private async Task<object> CompileRunAsync(FlaxJobProgress progress, CancellationToken cancellationToken)
    {
        FlaxEditorCompileResult result = await FlaxCheckedCompile.RunAsync(_checker, _bridgeClient, progress, cancellationToken);
        return result;
    }

    /// <summary>A play-mode job.</summary>
    /// <param name="action">start, stop, pause or resume.</param>
    /// <param name="progress">Receives what the editor is doing.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The play-mode result.</returns>
    private async Task<object> PlayRunAsync(string action, FlaxJobProgress progress, CancellationToken cancellationToken)
    {
        object result = await _bridgeClient.SetPlayModeAsync(action, cancellationToken, progress);
        return result;
    }

    /// <summary>The open_scene job.</summary>
    /// <param name="path">The scene path.</param>
    /// <param name="progress">Receives what the editor is doing.</param>
    /// <param name="cancellationToken">Stops waiting.</param>
    /// <returns>The open-scene result.</returns>
    private async Task<object> OpenSceneRunAsync(string path, FlaxJobProgress progress, CancellationToken cancellationToken)
    {
        progress.Report("opening " + path);
        object result = await _bridgeClient.OpenSceneAsync(path, cancellationToken);
        return result;
    }

    /// <summary>Returns a job's state.</summary>
    /// <param name="jobId">The job id.</param>
    /// <returns>The state.</returns>
    /// <exception cref="McpException">No job has the id.</exception>
    [McpServerTool(Name = "job_status", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Returns a job's state: running, succeeded (with its result), failed or canceled (with the reason), and its " +
        "latest progress message. Jobs come from editor_job_start, build_game, and the editor tools that run as jobs."
    )]
    public FlaxJobState GetStatus([Description("The job id, e.g. job-7.")] string jobId)
    {
        return _jobs.Status(jobId);
    }

    /// <summary>Waits for a job to end.</summary>
    /// <param name="jobId">The job id.</param>
    /// <param name="timeoutSeconds">How long to wait, 0 to 1800 seconds.</param>
    /// <param name="progress">Receives the job's progress messages.</param>
    /// <param name="cancellationToken">Stops the wait, not the job.</param>
    /// <returns>The job's state.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeoutSeconds"/> is out of range.</exception>
    /// <exception cref="McpException">No job has the id.</exception>
    [McpServerTool(Name = "job_wait", ReadOnly = true, UseStructuredContent = true)]
    [Description(
        "Waits for a job to end, up to the timeout, sending its progress messages as progress notifications, and " +
        "returns its state. A timeout is a result (Status running), not an error."
    )]
    public Task<FlaxJobState> WaitAsync(
        [Description("The job id, e.g. job-7.")] string jobId,
        [Description("How long to wait, up to 1800 seconds.")] double timeoutSeconds = 60,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(timeout, MaxWait);
        return _jobs.WaitAsync(jobId, timeout, progress, cancellationToken);
    }
}
