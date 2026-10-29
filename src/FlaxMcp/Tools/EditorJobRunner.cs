using FlaxMcp.Flax;
using ModelContextProtocol;

namespace FlaxMcp.Tools;

/// <summary>Runs a long editor operation of a tool as an exclusive job and waits for it up to a deadline.</summary>
internal static class EditorJobRunner
{
    /// <summary>How long editor_play_mode waits for the editor to reach the requested state.</summary>
    public static readonly TimeSpan PlayModeDeadline = TimeSpan.FromSeconds(35);

    /// <summary>How long editor_compile waits for the check, the compile and the reload.</summary>
    public static readonly TimeSpan CompileDeadline = TimeSpan.FromSeconds(125);

    /// <summary>Runs an editor operation as an exclusive job and waits for it up to its deadline.</summary>
    /// <typeparam name="T">The operation's result type.</typeparam>
    /// <param name="jobs">The server's job manager.</param>
    /// <param name="kind">The job's kind, also used in the error messages.</param>
    /// <param name="run">The operation; it receives the job's progress.</param>
    /// <param name="deadline">How long to wait for the job.</param>
    /// <param name="progress">Receives the job's progress messages; may be null.</param>
    /// <param name="cancellationToken">Stops the wait, not the job.</param>
    /// <returns>The operation's result.</returns>
    /// <exception cref="McpException">The job is still running after the deadline, failed, or was cancelled.</exception>
    public static async Task<T> RunAsync<T>(
        FlaxJobManager jobs,
        string kind,
        Func<FlaxJobProgress, Task<T>> run,
        TimeSpan deadline,
        IProgress<ProgressNotificationValue>? progress,
        CancellationToken cancellationToken) where T : class
    {
        FlaxJobHandle handle = jobs.Start(kind, exclusive: true, async (jobProgress, _) => await run(jobProgress));
        FlaxJobState state = await jobs.WaitAsync(handle.JobId, deadline, progress, cancellationToken);
        if (state.Status == "succeeded")
        {
            T result = (T)state.Result!;
            return result;
        }

        if (state.Status == "running")
        {
            throw new McpException(
                $"{kind} is still running as {handle.JobId} after {deadline.TotalSeconds:0} s; wait for it with job_wait.");
        }

        string message = state.Error ?? $"{kind} ended as {state.Status}.";
        throw new McpException(message);
    }
}
