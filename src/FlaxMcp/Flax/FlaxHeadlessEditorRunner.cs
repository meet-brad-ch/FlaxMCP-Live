using FlaxMcp.Configuration;
using FlaxMcp.Flax.Models;
using Microsoft.Extensions.Options;

namespace FlaxMcp.Flax;

/// <summary>
/// Runs <c>FlaxEditor.exe -headless -exit</c> against the configured project and returns its exit
/// code plus decoded <c>Logs/*.txt</c> output, guarded by <see cref="FlaxEditorSessionGuard"/> against
/// a concurrent live session. Requires a real Flax Engine install to exercise end to end, so it's
/// verified manually against a real project rather than by an automated test (see the Bridge spike
/// for the same trade-off).
/// </summary>
public sealed class FlaxHeadlessEditorRunner
{
    private readonly IOptions<FlaxMcpOptions> _options;
    private readonly FlaxEditorSessionGuard _sessionGuard;

    public FlaxHeadlessEditorRunner(IOptions<FlaxMcpOptions> options, FlaxEditorSessionGuard sessionGuard)
    {
        _options = options;
        _sessionGuard = sessionGuard;
    }

    public async Task<FlaxHeadlessRunResult> RunAsync(IReadOnlyList<string> extraArguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var projectFile = options.ResolveProjectFile();
        var projectFolder = Path.GetDirectoryName(projectFile)!;
        var enginePath = EngineLocator.Resolve(options.EnginePath);
        var editorExecutable = EngineLocator.ResolveEditorExecutable(enginePath, options.EditorConfig);
        EngineVersionCheck.Require(enginePath, projectFile);

        using var lease = _sessionGuard.Acquire(projectFolder);

        List<string> arguments = ["-project", projectFile, "-headless", "-exit", .. extraArguments];
        var startedUtc = DateTime.UtcNow;

        var processResult = await FlaxProcessRunner.RunAsync(
            editorExecutable,
            arguments,
            Path.GetDirectoryName(editorExecutable)!,
            timeout,
            cancellationToken
        );

        var logFile = FlaxLogReader.FindLatestLogFile(projectFolder, startedUtc);
        var log = logFile is not null ? FlaxLogReader.ReadLog(logFile.FullName) : null;
        var errorCount = log is not null ? FlaxLogReader.TryParseTotalErrors(log) : null;

        return new FlaxHeadlessRunResult(processResult.ExitCode, processResult.TimedOut, log, errorCount);
    }
}
