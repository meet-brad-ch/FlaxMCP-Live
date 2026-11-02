using System.ComponentModel;
using FlaxMcp.Configuration;
using FlaxMcp.Flax;
using FlaxMcp.Flax.Models;
using ModelContextProtocol.Server;

namespace FlaxMcp.Tools;

[McpServerToolType]
public sealed class BuildTools
{
    private static readonly TimeSpan ShortOperationTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan BuildTimeout = TimeSpan.FromMinutes(30);

    private readonly FlaxHeadlessEditorRunner _runner;
    private readonly FlaxJobManager _jobs;
    private readonly IFlaxScriptChecker _checker;

    public BuildTools(FlaxHeadlessEditorRunner runner, FlaxJobManager jobs, IFlaxScriptChecker checker)
    {
        _runner = runner;
        _jobs = jobs;
        _checker = checker;
    }

    [McpServerTool(Name = "build_generate_projects", ReadOnly = false, UseStructuredContent = true)]
    [Description("Runs FlaxEditor.exe -genprojectfiles to regenerate IDE project/solution files.")]
    public async Task<FlaxBuildOperationResult> GenerateProjects(CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(["-genprojectfiles"], ShortOperationTimeout, cancellationToken);
        return ToOperationResult(result);
    }

    [McpServerTool(Name = "build_compile_scripts", ReadOnly = false, UseStructuredContent = true)]
    [Description("Compiles game scripts headlessly. Returns structured compiler errors (file/line/code/message) parsed from the engine log, not a raw log dump.")]
    public async Task<FlaxBuildOperationResult> CompileScripts(CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync([], ShortOperationTimeout, cancellationToken);
        return ToOperationResult(result);
    }

    [McpServerTool(Name = "build_clear_cache", ReadOnly = false, UseStructuredContent = true)]
    [Description("Runs FlaxEditor.exe -clearcache, and optionally -clearcooker too, to clear build caches.")]
    public async Task<FlaxBuildOperationResult> ClearCache(bool alsoClearCooker = false, CancellationToken cancellationToken = default)
    {
        List<string> arguments = alsoClearCooker ? ["-clearcache", "-clearcooker"] : ["-clearcache"];
        var result = await _runner.RunAsync(arguments, ShortOperationTimeout, cancellationToken);
        return ToOperationResult(result);
    }

    [McpServerTool(Name = "build_check_scripts", ReadOnly = true, UseStructuredContent = true)]
    [Description("Compiles the project's script modules, and those of the projects it references, outside the editor into a scratch folder. It uses the compiler arguments that Flax.Build last used and the current files of the modules. Returns passed, failed or unavailable (never built: Reason tells why), with structured errors and warnings (file, line, column, code) and the outcome of each module. It works while the editor is open, also during play mode. It changes nothing in the project. Use it to find compile errors fast. It does not load the change into the editor: for that, use editor_compile.")]
    public Task<FlaxScriptCheckResult> CheckScriptsAsync(CancellationToken cancellationToken)
    {
        return _checker.CheckAsync(cancellationToken);
    }

    [McpServerTool(Name = "build_game", ReadOnly = false, UseStructuredContent = true)]
    [Description("Starts a game build (-build <preset>.<target>, e.g. preset 'Development', target 'Windows') in the background and returns its job at once -- a real build can take minutes. Follow it with job_status or job_wait; the job's result is the build outcome.")]
    public FlaxJobHandle StartBuild(string preset, string target)
    {
        return _jobs.Start("build_game", exclusive: false, async (progress, cancellationToken) =>
        {
            progress.Report($"building {preset}.{target}");
            return ToOperationResult(await _runner.RunAsync(["-build", $"{preset}.{target}"], BuildTimeout, cancellationToken));
        });
    }

    internal static FlaxBuildOperationResult ToOperationResult(FlaxHeadlessRunResult result)
    {
        var errors = result.Log is not null ?
            FlaxCompilerDiagnosticParser.Parse(result.Log).Where(diagnostic => diagnostic.Severity == "error").Take(ResponseLimits.DefaultListTop).ToArray() :
            [];

        return new FlaxBuildOperationResult(
            Succeeded: result.ExitCode == 0 && !result.TimedOut && result.ErrorCount is null or 0,
            result.ExitCode,
            result.TimedOut,
            result.ErrorCount,
            errors
        );
    }
}
