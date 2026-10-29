using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlaxMcp.Configuration;
using FlaxMcp.Flax.Models;
using Microsoft.Extensions.Options;

namespace FlaxMcp.Flax;

/// <summary>Checks a project's scripts without the editor.</summary>
public interface IFlaxScriptChecker
{
    /// <summary>Compiles every script module of the project (and its referenced projects) to a scratch folder.</summary>
    Task<FlaxScriptCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Compiles the project's C# script modules the way Flax.Build last did, outside the editor: Flax.Build leaves the exact
/// compiler arguments in <c>Cache/Intermediate/&lt;EditorTarget&gt;/&lt;Platform&gt;/&lt;Arch&gt;/&lt;Config&gt;/&lt;Module&gt;.CSharp.response</c>.
/// The checker re-reads each module's source folder (files added or removed since), writes the output to a scratch
/// folder, and has the .NET SDK's compiler write its diagnostics as SARIF, so errors come back structured (file, line,
/// column, code) without the editor or its log. A module that references another module is compiled against that
/// module's fresh output; a module whose dependency failed is skipped.
/// </summary>
public sealed partial class FlaxScriptChecker : IFlaxScriptChecker
{
    /// <summary>How long one module's compile may take.</summary>
    private static readonly TimeSpan CompileTimeout = TimeSpan.FromMinutes(2);

    /// <summary>The project's .flaxproj.</summary>
    private readonly string _projectFile;

    /// <summary>The editor build configuration; null for the newest one.</summary>
    private readonly string? _configuration;

    /// <summary>The folder the check's outputs go to.</summary>
    private readonly string _scratchRoot;

    /// <summary>The .NET installation; null to search for it.</summary>
    private readonly string? _dotnetRoot;

    /// <summary>Creates a checker from the server's options.</summary>
    /// <param name="options">The server's options: the project file and the editor configuration.</param>
    public FlaxScriptChecker(IOptions<FlaxMcpOptions> options)
        : this(options.Value.ResolveProjectFile(), options.Value.EditorConfigSet ? options.Value.EditorConfig : null,
            Path.Combine(Path.GetTempPath(), "FlaxMcp", "check"), null)
    {
    }

    /// <summary>Creates a checker.</summary>
    /// <param name="projectFile">The project's .flaxproj.</param>
    /// <param name="configuration">
    /// The editor build configuration (Development, Debug, Release); null for the one the editor built last (the newest
    /// response files): an editor run in another configuration than the default left the default's arguments stale, and
    /// the check failed on references they lacked.
    /// </param>
    /// <param name="scratchRoot">Where the check's outputs go.</param>
    /// <param name="dotnetRoot">The .NET installation; null to find it on the PATH.</param>
    internal FlaxScriptChecker(string projectFile, string? configuration, string scratchRoot, string? dotnetRoot)
    {
        _projectFile = projectFile;
        _configuration = configuration;
        _scratchRoot = scratchRoot;
        _dotnetRoot = dotnetRoot;
    }

    /// <summary>Compiles every script module of the project in dependency order.</summary>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <returns>The outcome: per module, with the compiler's errors and warnings; unavailable when it cannot run.</returns>
    /// <exception cref="InvalidOperationException">The script modules reference each other in a cycle.</exception>
    public async Task<FlaxScriptCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        Stopwatch watch = Stopwatch.StartNew();
        List<FlaxCscModule> modules = FindModules();
        if (modules.Count == 0)
        {
            return FlaxScriptCheckResult.Unavailable(
                watch.Elapsed.TotalSeconds,
                "No compiler response files under Cache/Intermediate: the scripts have not been built in the editor yet.");
        }

        FlaxCompilerTool? compiler = FindCompiler(modules[0]);
        if (compiler is null)
        {
            return FlaxScriptCheckResult.Unavailable(
                watch.Elapsed.TotalSeconds,
                $"No .NET SDK {modules[0].RuntimeMajor}.x compiler (sdk/<version>/Roslyn/bincore/csc.dll) was found.");
        }

        string scratch = Path.Combine(_scratchRoot, ProjectKey());
        Directory.CreateDirectory(scratch);
        FlaxCheckState state = new();
        // Modules that do not reference each other compile at the same time; a layer waits for the one before.
        foreach (IReadOnlyList<FlaxCscModule> layer in Layers(modules))
        {
            List<FlaxCscModule> ready = state.SkipBlocked(layer);
            FlaxTimedRun[] runs = await CompileLayerAsync(compiler, ready, scratch, state.Outputs, cancellationToken);
            state.Record(runs);
        }

        string status = "passed";
        if (state.Failed.Count > 0)
        {
            status = "failed";
        }

        return new FlaxScriptCheckResult(status, watch.Elapsed.TotalSeconds, null, state.Reports, state.Errors, state.Warnings);
    }

    /// <summary>Compiles the modules of one layer at the same time.</summary>
    /// <param name="compiler">The compiler to run.</param>
    /// <param name="ready">The modules to compile; none of them references another one in the list.</param>
    /// <param name="scratch">The check's scratch folder.</param>
    /// <param name="outputs">The fresh output of each module compiled so far, by the path Flax.Build writes.</param>
    /// <param name="cancellationToken">Stops the compiles.</param>
    /// <returns>Each module's compile with its duration.</returns>
    private async Task<FlaxTimedRun[]> CompileLayerAsync(
        FlaxCompilerTool compiler,
        List<FlaxCscModule> ready,
        string scratch,
        IReadOnlyDictionary<string, string> outputs,
        CancellationToken cancellationToken)
    {
        IEnumerable<Task<FlaxTimedRun>> tasks = ready.Select(module => CompileTimedAsync(compiler, module, scratch, outputs, cancellationToken));
        FlaxTimedRun[] runs = await Task.WhenAll(tasks);
        return runs;
    }

    /// <summary>Compiles one module and measures how long it took.</summary>
    /// <param name="compiler">The compiler to run.</param>
    /// <param name="module">The module.</param>
    /// <param name="scratch">The check's scratch folder.</param>
    /// <param name="outputs">The fresh output of each module compiled so far.</param>
    /// <param name="cancellationToken">Stops the compile.</param>
    /// <returns>The compile with its duration.</returns>
    private async Task<FlaxTimedRun> CompileTimedAsync(
        FlaxCompilerTool compiler,
        FlaxCscModule module,
        string scratch,
        IReadOnlyDictionary<string, string> outputs,
        CancellationToken cancellationToken)
    {
        Stopwatch moduleWatch = Stopwatch.StartNew();
        FlaxModuleRun run = await CompileAsync(compiler, module, scratch, outputs, cancellationToken);
        return new FlaxTimedRun(module, run, moduleWatch.Elapsed.TotalMilliseconds);
    }

    /// <summary>Compiles one module with the response file rewritten for the check.</summary>
    /// <param name="compiler">The compiler to run.</param>
    /// <param name="module">The module.</param>
    /// <param name="scratch">The check's scratch folder.</param>
    /// <param name="outputs">The fresh output of each module compiled so far.</param>
    /// <param name="cancellationToken">Stops the compile.</param>
    /// <returns>The compile's outcome.</returns>
    private async Task<FlaxModuleRun> CompileAsync(
        FlaxCompilerTool compiler,
        FlaxCscModule module,
        string scratch,
        IReadOnlyDictionary<string, string> outputs,
        CancellationToken cancellationToken)
    {
        string folder = Path.Combine(scratch, module.ProjectName, module.Name);
        Directory.CreateDirectory(folder);
        string outPath = Path.Combine(folder, Path.GetFileName(module.OutPath));
        string sarif = Path.Combine(folder, module.Name + ".sarif");
        File.Delete(sarif);
        IReadOnlyList<string> sources = Sources(module);
        string response = BuildResponse(module, sources, outputs, outPath, sarif);
        string responsePath = Path.Combine(folder, module.Name + ".check.response");
        await File.WriteAllTextAsync(responsePath, response, cancellationToken);
        // -shared: the compiler server (VBCSCompiler, as dotnet build uses) keeps the compiler warm between checks.
        string[] arguments = [compiler.CscPath, "-shared", "@" + responsePath];
        FlaxProcessResult process = await FlaxProcessRunner.RunAsync(compiler.DotnetHost, arguments, folder, CompileTimeout, cancellationToken);
        if (process.TimedOut)
        {
            return TimedOutRun(outPath, sources.Count);
        }

        if (!File.Exists(sarif))
        {
            return NoDiagnosticsRun(outPath, sources.Count, process);
        }

        string sarifText = await File.ReadAllTextAsync(sarif, cancellationToken);
        IReadOnlyList<FlaxCompilerDiagnostic> diagnostics = FlaxSarifReader.Read(sarifText);
        FlaxCompilerDiagnostic[] errors = diagnostics.Where(diagnostic => diagnostic.Severity == "error").ToArray();
        FlaxCompilerDiagnostic[] warnings = diagnostics.Where(diagnostic => diagnostic.Severity == "warning").ToArray();
        return new FlaxModuleRun(outPath, process.ExitCode, sources.Count, errors, warnings, null);
    }

    /// <summary>The run of a compile that did not finish in time.</summary>
    /// <param name="outPath">The assembly the compile would write.</param>
    /// <param name="sourceCount">How many sources it compiled.</param>
    /// <returns>A run with a TIMEOUT error.</returns>
    private static FlaxModuleRun TimedOutRun(string outPath, int sourceCount)
    {
        string message = $"The compiler did not finish within {CompileTimeout.TotalSeconds:0} seconds.";
        FlaxCompilerDiagnostic[] errors = [new FlaxCompilerDiagnostic("", 0, 0, "error", "TIMEOUT", message)];
        return new FlaxModuleRun(outPath, null, sourceCount, errors, [], null);
    }

    /// <summary>The run of a compile that wrote no diagnostics file.</summary>
    /// <param name="outPath">The assembly the compile would write.</param>
    /// <param name="sourceCount">How many sources it compiled.</param>
    /// <param name="process">The compiler process; its output says why.</param>
    /// <returns>A run with a CHECK error.</returns>
    private static FlaxModuleRun NoDiagnosticsRun(string outPath, int sourceCount, FlaxProcessResult process)
    {
        // No diagnostics file: the compiler did not start compiling (a bad option, a missing compiler). Its
        // output says why.
        string text = (process.StandardOutput + process.StandardError).Trim();
        string message = "The compiler wrote no diagnostics.";
        if (text.Length > 0)
        {
            message = text;
        }

        FlaxCompilerDiagnostic[] errors = [new FlaxCompilerDiagnostic("", 0, 0, "error", "CHECK", message)];
        return new FlaxModuleRun(outPath, process.ExitCode, sourceCount, errors, [], null);
    }

    /// <summary>Writes the response file text: the module's options with fresh references, the outputs and the sources.</summary>
    /// <param name="module">The module.</param>
    /// <param name="sources">The module's sources now.</param>
    /// <param name="outputs">The fresh output of each module compiled so far.</param>
    /// <param name="outPath">Where the assembly goes.</param>
    /// <param name="sarif">Where the diagnostics go.</param>
    /// <returns>The response file text.</returns>
    private static string BuildResponse(
        FlaxCscModule module,
        IReadOnlyList<string> sources,
        IReadOnlyDictionary<string, string> outputs,
        string outPath,
        string sarif)
    {
        StringBuilder response = new();
        foreach (string option in module.Options)
        {
            if (IsReplacedOption(option))
            {
                continue;
            }

            response.AppendLine(FreshOption(option, outputs));
        }

        response.AppendLine($"/out:\"{outPath}\"");
        response.AppendLine($"/errorlog:\"{sarif}\",version=2.1");
        foreach (string source in sources)
        {
            response.AppendLine($"\"{source}\"");
        }

        return response.ToString();
    }

    /// <summary>Tells whether the check writes its own value for an option.</summary>
    /// <param name="option">The option line.</param>
    /// <returns>True for /out, /doc, /generatedfilesout and /errorlog.</returns>
    private static bool IsReplacedOption(string option)
    {
        bool isOut = option.StartsWith("/out:", StringComparison.Ordinal);
        bool isDoc = option.StartsWith("/doc:", StringComparison.Ordinal);
        bool isGenerated = option.StartsWith("/generatedfilesout:", StringComparison.Ordinal);
        bool isErrorLog = option.StartsWith("/errorlog:", StringComparison.Ordinal);
        return isOut || isDoc || isGenerated || isErrorLog;
    }

    /// <summary>Points a reference to a module at that module's fresh output.</summary>
    /// <param name="option">The option line.</param>
    /// <param name="outputs">The fresh output of each module compiled so far.</param>
    /// <returns>The option, with a fresh /reference line for a module compiled in this check.</returns>
    private static string FreshOption(string option, IReadOnlyDictionary<string, string> outputs)
    {
        string? reference = ReferencePath(option);
        if (reference is not null && outputs.TryGetValue(reference, out string? fresh))
        {
            return $"/reference:\"{fresh}\"";
        }

        return option;
    }

    /// <summary>
    /// The module's sources now: the listed files outside its folder (generated ones) that still exist, and every .cs
    /// file in its folder except Flax.Build scripts and the folders of other modules.
    /// </summary>
    private static IReadOnlyList<string> Sources(FlaxCscModule module)
    {
        if (module.Folder is null)
        {
            return module.Sources.Where(File.Exists).ToArray();
        }

        string folder = Path.GetFullPath(module.Folder) + Path.DirectorySeparatorChar;
        IEnumerable<string> outside = module.Sources.Where(source => IsGeneratedSource(source, folder));
        string[] sources = outside.Concat(ModuleFiles(module.Folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return sources;
    }

    /// <summary>Tells whether a listed source lies outside the module's folder and still exists.</summary>
    /// <param name="source">The listed source file.</param>
    /// <param name="folder">The module's folder, with a trailing separator.</param>
    /// <returns>True for an existing file outside the folder.</returns>
    private static bool IsGeneratedSource(string source, string folder)
    {
        bool isInside = Path.GetFullPath(source).StartsWith(folder, StringComparison.OrdinalIgnoreCase);
        return !isInside && File.Exists(source);
    }

    /// <summary>
    /// The .cs files under a module's folder, except Flax.Build scripts and the folders of other modules. A folder's own
    /// files come before the files of its sub folders.
    /// </summary>
    /// <param name="folder">The module's folder.</param>
    /// <returns>The files, in folder order.</returns>
    private static IEnumerable<string> ModuleFiles(string folder)
    {
        // A stack, not recursion: the sub folders of a folder are pushed in reverse so they pop in folder order.
        Stack<string> pending = new();
        pending.Push(folder);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string file in Directory.EnumerateFiles(current, "*.cs"))
            {
                if (!file.EndsWith(".Build.cs", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }

            string[] children = Directory.EnumerateDirectories(current).Where(IsPlainFolder).ToArray();
            for (int index = children.Length - 1; index >= 0; index--)
            {
                pending.Push(children[index]);
            }
        }
    }

    /// <summary>Tells whether a sub folder belongs to the module (it holds no .Build.cs of its own).</summary>
    /// <param name="child">The sub folder.</param>
    /// <returns>True when it has no .Build.cs file.</returns>
    private static bool IsPlainFolder(string child)
    {
        return !Directory.EnumerateFiles(child, "*.Build.cs").Any();
    }

    /// <summary>Reads the response files of the project and its referenced projects.</summary>
    /// <returns>Every module Flax.Build last compiled.</returns>
    private List<FlaxCscModule> FindModules()
    {
        List<FlaxCscModule> modules = [];
        string? configuration = _configuration;
        foreach (string project in Projects())
        {
            FlaxProjectInfo info = FlaxProjectReader.Read(project);
            if (string.IsNullOrEmpty(info.EditorTarget))
            {
                continue;
            }

            string projectFolder = Path.GetDirectoryName(project)!;
            string target = Path.Combine(projectFolder, "Cache", "Intermediate", info.EditorTarget, PlatformName(), ArchitectureName());
            // the main project decides the configuration; the editor builds its referenced projects in the same one
            configuration ??= NewestConfiguration(target);
            if (configuration is null)
            {
                continue;
            }

            string intermediate = Path.Combine(target, configuration);
            if (Directory.Exists(intermediate))
            {
                modules.AddRange(ReadModules(intermediate, info.Name, Path.Combine(projectFolder, "Source")));
            }
        }

        return modules;
    }

    /// <summary>Reads every response file in a configuration folder.</summary>
    /// <param name="intermediate">The configuration folder.</param>
    /// <param name="projectName">The project's name.</param>
    /// <param name="sourceRoot">The project's Source folder.</param>
    /// <returns>The modules.</returns>
    private static List<FlaxCscModule> ReadModules(string intermediate, string projectName, string sourceRoot)
    {
        List<FlaxCscModule> modules = [];
        foreach (string response in Directory.EnumerateFiles(intermediate, "*.CSharp.response"))
        {
            modules.Add(FlaxCscModule.Read(response, projectName, sourceRoot));
        }

        return modules;
    }

    /// <summary>The configuration folder under <paramref name="target"/> with the newest compiler response file (the
    /// editor's last build); null when none has one.</summary>
    private static string? NewestConfiguration(string target)
    {
        if (!Directory.Exists(target))
        {
            return null;
        }

        string? newest = Directory.EnumerateDirectories(target)
            .Select(folder => new FlaxConfigurationFolder(folder, NewestResponseTime(folder)))
            .Where(candidate => candidate.Newest != default)
            .OrderByDescending(candidate => candidate.Newest)
            .Select(candidate => Path.GetFileName(candidate.Folder))
            .FirstOrDefault();
        return newest;
    }

    /// <summary>The last write time of the newest compiler response file in a folder.</summary>
    /// <param name="folder">A configuration folder.</param>
    /// <returns>The time; the default (zero) time when the folder has none.</returns>
    private static DateTime NewestResponseTime(string folder)
    {
        IEnumerable<DateTime> times = Directory.EnumerateFiles(folder, "*.CSharp.response").Select(File.GetLastWriteTimeUtc);
        return times.DefaultIfEmpty().Max();
    }

    /// <summary>The project and the projects it references, except the engine.</summary>
    /// <returns>The project files, each once, nearest first.</returns>
    private IEnumerable<string> Projects()
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        Queue<string> pending = new([Path.GetFullPath(_projectFile)]);
        while (pending.Count > 0)
        {
            string project = pending.Dequeue();
            if (!seen.Add(project) || !File.Exists(project))
            {
                continue;
            }

            yield return project;
            FlaxProjectInfo info = FlaxProjectReader.Read(project);
            foreach (string reference in info.References)
            {
                if (reference.Contains("$(EnginePath)", StringComparison.Ordinal))
                {
                    continue;
                }

                pending.Enqueue(ReferencedProjectPath(project, reference));
            }
        }
    }

    /// <summary>Resolves a project reference against the project that holds it.</summary>
    /// <param name="project">The project file with the reference.</param>
    /// <param name="reference">The reference, absolute or relative to the project's folder.</param>
    /// <returns>The full path.</returns>
    private static string ReferencedProjectPath(string project, string reference)
    {
        string path = reference;
        if (!Path.IsPathRooted(reference))
        {
            path = Path.Combine(Path.GetDirectoryName(project)!, reference);
        }

        return Path.GetFullPath(path);
    }

    /// <summary>The modules in layers: a module comes after every module whose output it references.</summary>
    /// <param name="modules">Every module.</param>
    /// <returns>The layers, the ones with no dependency first.</returns>
    /// <exception cref="InvalidOperationException">The modules reference each other in a cycle.</exception>
    private static IReadOnlyList<IReadOnlyList<FlaxCscModule>> Layers(IReadOnlyList<FlaxCscModule> modules)
    {
        Dictionary<string, FlaxCscModule> byOutput = modules.ToDictionary(module => module.OutPath, StringComparer.OrdinalIgnoreCase);
        Dictionary<FlaxCscModule, int> depths = [];
        // Each pass settles the modules whose dependencies are settled; a pass that settles none means a cycle.
        while (depths.Count < modules.Count)
        {
            int settled = SettleModules(modules, byOutput, depths);
            if (settled == 0)
            {
                throw new InvalidOperationException(
                    $"The script modules reference each other in a cycle through {CycleModule(modules, byOutput, depths).Name}.");
            }
        }

        IReadOnlyList<FlaxCscModule>[] layers = modules.GroupBy(module => depths[module]).OrderBy(group => group.Key)
            .Select(group => (IReadOnlyList<FlaxCscModule>)group.ToArray()).ToArray();
        return layers;
    }

    /// <summary>Gives a depth to every module whose dependencies all have one.</summary>
    /// <param name="modules">Every module.</param>
    /// <param name="byOutput">The modules by the assembly they write.</param>
    /// <param name="depths">The depths found so far; this adds to it.</param>
    /// <returns>How many modules got a depth.</returns>
    private static int SettleModules(
        IReadOnlyList<FlaxCscModule> modules,
        Dictionary<string, FlaxCscModule> byOutput,
        Dictionary<FlaxCscModule, int> depths)
    {
        Dictionary<FlaxCscModule, int> found = [];
        foreach (FlaxCscModule module in modules)
        {
            if (depths.ContainsKey(module))
            {
                continue;
            }

            int depth = DependencyDepth(module, byOutput, depths);
            if (depth >= 0)
            {
                found[module] = depth;
            }
        }

        foreach (KeyValuePair<FlaxCscModule, int> entry in found)
        {
            depths[entry.Key] = entry.Value;
        }

        return found.Count;
    }

    /// <summary>The depth of a module: one more than its deepest dependency.</summary>
    /// <param name="module">The module.</param>
    /// <param name="byOutput">The modules by the assembly they write.</param>
    /// <param name="depths">The depths found so far.</param>
    /// <returns>The depth; -1 while a dependency has none.</returns>
    private static int DependencyDepth(
        FlaxCscModule module,
        Dictionary<string, FlaxCscModule> byOutput,
        Dictionary<FlaxCscModule, int> depths)
    {
        int depth = 0;
        foreach (string reference in module.References)
        {
            if (!byOutput.TryGetValue(reference, out FlaxCscModule? dependency))
            {
                continue;
            }

            if (!depths.TryGetValue(dependency, out int dependencyDepth))
            {
                return -1;
            }

            depth = Math.Max(depth, dependencyDepth + 1);
        }

        return depth;
    }

    /// <summary>Finds the module that closes a cycle: from the first module left over, follows its first unsettled dependency until a module repeats.</summary>
    /// <param name="modules">Every module.</param>
    /// <param name="byOutput">The modules by the assembly they write.</param>
    /// <param name="depths">The depths found so far.</param>
    /// <returns>The module reached twice.</returns>
    private static FlaxCscModule CycleModule(
        IReadOnlyList<FlaxCscModule> modules,
        Dictionary<string, FlaxCscModule> byOutput,
        Dictionary<FlaxCscModule, int> depths)
    {
        FlaxCscModule current = modules.First(module => !depths.ContainsKey(module));
        HashSet<FlaxCscModule> visited = [];
        // The walk ends: each step visits a new module of a finite list or stops on a repeat.
        for (int step = 0; step <= modules.Count; step++)
        {
            if (!visited.Add(current))
            {
                return current;
            }

            current = FirstUnsettledDependency(current, byOutput, depths);
        }

        throw new InvalidOperationException("The cycle search did not end.");
    }

    /// <summary>The first dependency of a module that has no depth yet.</summary>
    /// <param name="module">The module.</param>
    /// <param name="byOutput">The modules by the assembly they write.</param>
    /// <param name="depths">The depths found so far.</param>
    /// <returns>The dependency.</returns>
    /// <exception cref="InvalidOperationException">Every dependency has a depth.</exception>
    private static FlaxCscModule FirstUnsettledDependency(
        FlaxCscModule module,
        Dictionary<string, FlaxCscModule> byOutput,
        Dictionary<FlaxCscModule, int> depths)
    {
        foreach (string reference in module.References)
        {
            if (byOutput.TryGetValue(reference, out FlaxCscModule? dependency) && !depths.ContainsKey(dependency))
            {
                return dependency;
            }
        }

        throw new InvalidOperationException($"The module {module.Name} has no unsettled dependency.");
    }

    /// <summary>Finds the newest SDK compiler of the runtime's major version.</summary>
    /// <param name="module">The module whose reference pack decides the major version.</param>
    /// <returns>The dotnet host and csc.dll; null when none is installed.</returns>
    private FlaxCompilerTool? FindCompiler(FlaxCscModule module)
    {
        IEnumerable<string> roots = DotnetRoots();
        if (_dotnetRoot is not null)
        {
            roots = [_dotnetRoot];
        }

        foreach (string root in roots)
        {
            string hostName = "dotnet";
            if (OperatingSystem.IsWindows())
            {
                hostName = "dotnet.exe";
            }

            string host = Path.Combine(root, hostName);
            string sdks = Path.Combine(root, "sdk");
            if (!File.Exists(host) || !Directory.Exists(sdks))
            {
                continue;
            }

            string? csc = NewestCsc(sdks, module.RuntimeMajor);
            if (csc is not null)
            {
                return new FlaxCompilerTool(host, csc);
            }
        }

        return null;
    }

    /// <summary>Finds the csc.dll of the newest SDK with a major version.</summary>
    /// <param name="sdks">The installation's sdk folder.</param>
    /// <param name="major">The wanted .NET major version.</param>
    /// <returns>The path; null when no such SDK has a compiler.</returns>
    private static string? NewestCsc(string sdks, int major)
    {
        string? csc = Directory.EnumerateDirectories(sdks)
            .Select(folder => new FlaxSdkFolder(folder, SdkVersion(folder)))
            .Where(sdk => sdk.Version is not null && sdk.Version.Major == major)
            .OrderByDescending(sdk => sdk.Version)
            .Select(sdk => Path.Combine(sdk.Folder, "Roslyn", "bincore", "csc.dll"))
            .FirstOrDefault(File.Exists);
        return csc;
    }

    /// <summary>Reads the version from an SDK folder's name (e.g. 10.0.100 or 10.0.100-preview.1).</summary>
    /// <param name="folder">The SDK folder.</param>
    /// <returns>The version; null when the name is not a version (such a folder is not an SDK).</returns>
    private static Version? SdkVersion(string folder)
    {
        string text = Path.GetFileName(folder).Split('-')[0];
        if (Version.TryParse(text, out Version? parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>
    /// Where .NET may be installed: the installation of the runtime running the server, DOTNET_ROOT, then the folders
    /// on the PATH (a link such as /usr/bin/dotnet is followed to its installation).
    /// </summary>
    private static IEnumerable<string> DotnetRoots()
    {
        // .../shared/Microsoft.NETCore.App/<version>/System.Private.CoreLib.dll
        string? runtime = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (runtime is not null)
        {
            yield return Path.GetFullPath(Path.Combine(runtime, "..", "..", ".."));
        }

        string? dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
        {
            yield return dotnetRoot;
        }

        foreach (string root in PathRoots())
        {
            yield return root;
        }
    }

    /// <summary>The installations of the dotnet executables found on the PATH.</summary>
    /// <returns>The folder of each dotnet executable, after its links are followed.</returns>
    private static IEnumerable<string> PathRoots()
    {
        string name = "dotnet";
        if (OperatingSystem.IsWindows())
        {
            name = "dotnet.exe";
        }

        string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string folder in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(folder.Trim('"'), name);
            if (!File.Exists(candidate))
            {
                continue;
            }

            string target = candidate;
            FileSystemInfo? linkTarget = File.ResolveLinkTarget(candidate, returnFinalTarget: true);
            if (linkTarget is not null)
            {
                target = linkTarget.FullName;
            }

            yield return Path.GetDirectoryName(target)!;
        }
    }

    /// <summary>Reads the assembly path of a /reference option.</summary>
    /// <param name="option">The option line.</param>
    /// <returns>The full path; null when the option is not a /reference.</returns>
    private static string? ReferencePath(string option)
    {
        if (!option.StartsWith("/reference:", StringComparison.Ordinal))
        {
            return null;
        }

        return Path.GetFullPath(option["/reference:".Length..].Trim('"'));
    }

    /// <summary>The platform folder name Flax.Build uses.</summary>
    /// <returns>Windows, Mac or Linux.</returns>
    internal static string PlatformName()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Windows";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "Mac";
        }

        return "Linux";
    }

    /// <summary>The architecture folder name Flax.Build uses.</summary>
    /// <returns>ARM64 or x64.</returns>
    internal static string ArchitectureName()
    {
        if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
        {
            return "ARM64";
        }

        return "x64";
    }

    /// <summary>A short key of the project's path, for its scratch folder.</summary>
    /// <returns>Twelve hex characters.</returns>
    private string ProjectKey()
    {
        string path = Path.GetFullPath(_projectFile).ToLowerInvariant();
        byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(path));
        string hex = Convert.ToHexString(hash);
        return hex[..12];
    }

    /// <summary>The compiler: the dotnet host and the SDK's csc.dll.</summary>
    /// <param name="DotnetHost">The dotnet executable.</param>
    /// <param name="CscPath">The csc.dll.</param>
    private sealed record FlaxCompilerTool(string DotnetHost, string CscPath);

    /// <summary>An installed SDK's folder and version.</summary>
    /// <param name="Folder">The SDK folder.</param>
    /// <param name="Version">The version in its name; null when the name is not a version.</param>
    private sealed record FlaxSdkFolder(string Folder, Version? Version);

    /// <summary>A configuration folder and the time of its newest response file.</summary>
    /// <param name="Folder">The configuration folder.</param>
    /// <param name="Newest">The newest response file's write time; the default time when none.</param>
    private sealed record FlaxConfigurationFolder(string Folder, DateTime Newest);

    /// <summary>A module's compile and how long it took.</summary>
    /// <param name="Module">The module.</param>
    /// <param name="Run">Its compile.</param>
    /// <param name="Milliseconds">How long the compile took.</param>
    private sealed record FlaxTimedRun(FlaxCscModule Module, FlaxModuleRun Run, double Milliseconds);

    /// <summary>One module's compile in the check.</summary>
    /// <param name="OutPath">The assembly the check wrote.</param>
    /// <param name="ExitCode">The compiler's exit code; null when it timed out.</param>
    /// <param name="SourceCount">How many sources were compiled.</param>
    /// <param name="Errors">The compiler errors.</param>
    /// <param name="Warnings">The compiler warnings.</param>
    /// <param name="Note">A note for the report; null for none.</param>
    private sealed record FlaxModuleRun(
        string OutPath,
        int? ExitCode,
        int SourceCount,
        IReadOnlyList<FlaxCompilerDiagnostic> Errors,
        IReadOnlyList<FlaxCompilerDiagnostic> Warnings,
        string? Note);

    /// <summary>What a check has found so far, layer by layer.</summary>
    private sealed class FlaxCheckState
    {
        /// <summary>The fresh output of each module that compiled, by the path Flax.Build writes.</summary>
        public Dictionary<string, string> Outputs { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The Flax.Build output paths of the modules that failed or were skipped.</summary>
        public HashSet<string> Failed { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>One report per module.</summary>
        public List<FlaxScriptCheckModule> Reports { get; } = [];

        /// <summary>Every compiler error.</summary>
        public List<FlaxCompilerDiagnostic> Errors { get; } = [];

        /// <summary>Every compiler warning.</summary>
        public List<FlaxCompilerDiagnostic> Warnings { get; } = [];

        /// <summary>Reports the modules of a layer whose dependency failed as skipped.</summary>
        /// <param name="layer">The modules of one layer.</param>
        /// <returns>The modules that can compile.</returns>
        public List<FlaxCscModule> SkipBlocked(IReadOnlyList<FlaxCscModule> layer)
        {
            List<FlaxCscModule> ready = [];
            foreach (FlaxCscModule module in layer)
            {
                string? blocker = module.References.FirstOrDefault(Failed.Contains);
                if (blocker is null)
                {
                    ready.Add(module);
                    continue;
                }

                Failed.Add(module.OutPath);
                string note = $"It references {Path.GetFileName(blocker)}, which did not compile.";
                Reports.Add(new FlaxScriptCheckModule(module.Name, module.ProjectName, "skipped", 0, 0, note));
            }

            return ready;
        }

        /// <summary>Takes in the compiles of one layer.</summary>
        /// <param name="runs">The compiles.</param>
        public void Record(IReadOnlyList<FlaxTimedRun> runs)
        {
            foreach (FlaxTimedRun timed in runs)
            {
                Errors.AddRange(timed.Run.Errors);
                Warnings.AddRange(timed.Run.Warnings);
                bool passed = timed.Run.Errors.Count == 0 && timed.Run.ExitCode == 0;
                string status = "failed";
                if (passed)
                {
                    status = "passed";
                    Outputs[timed.Module.OutPath] = timed.Run.OutPath;
                }
                else
                {
                    Failed.Add(timed.Module.OutPath);
                }

                Reports.Add(new FlaxScriptCheckModule(
                    timed.Module.Name, timed.Module.ProjectName, status, timed.Run.SourceCount, timed.Milliseconds, timed.Run.Note));
            }
        }
    }
}

/// <summary>One script module as Flax.Build last compiled it.</summary>
/// <param name="Name">The module's name.</param>
/// <param name="ProjectName">The project it belongs to.</param>
/// <param name="Options">The compiler options, one per line as Flax.Build wrote them.</param>
/// <param name="Sources">The source files Flax.Build listed.</param>
/// <param name="OutPath">The assembly Flax.Build writes.</param>
/// <param name="References">The referenced assemblies' paths.</param>
/// <param name="Folder">The module's source folder (where its .Build.cs is), or null when not found.</param>
/// <param name="RuntimeMajor">The .NET major version of the reference pack it compiles against.</param>
public sealed partial record FlaxCscModule(
    string Name,
    string ProjectName,
    IReadOnlyList<string> Options,
    IReadOnlyList<string> Sources,
    string OutPath,
    IReadOnlyList<string> References,
    string? Folder,
    int RuntimeMajor)
{
    /// <summary>Reads a response file Flax.Build wrote.</summary>
    /// <param name="responsePath">The <c>&lt;Module&gt;.CSharp.response</c> file.</param>
    /// <param name="projectName">The project's name.</param>
    /// <param name="sourceRoot">The project's Source folder, searched for the module's .Build.cs.</param>
    /// <returns>The module.</returns>
    /// <exception cref="InvalidOperationException">The file has no /out option.</exception>
    public static FlaxCscModule Read(string responsePath, string projectName, string sourceRoot)
    {
        string name = Path.GetFileName(responsePath)[..^".CSharp.response".Length];
        List<string> options = [];
        List<string> sources = [];
        foreach (string raw in File.ReadAllLines(responsePath))
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] is '/' or '-')
            {
                options.Add(line);
            }
            else
            {
                sources.AddRange(SourcePaths(line));
            }
        }

        string? outOption = options.FirstOrDefault(option => option.StartsWith("/out:", StringComparison.Ordinal));
        if (outOption is null)
        {
            throw new InvalidOperationException($"{responsePath} has no /out option.");
        }

        string[] references = options
            .Where(option => option.StartsWith("/reference:", StringComparison.Ordinal))
            .Select(option => Path.GetFullPath(option["/reference:".Length..].Trim('"')))
            .ToArray();
        string outPath = Path.GetFullPath(outOption["/out:".Length..].Trim('"'));
        string? folder = BuildScriptFolder(sourceRoot, name);
        int runtimeMajor = RuntimeMajorOf(references);
        return new FlaxCscModule(name, projectName, options, sources, outPath, references, folder, runtimeMajor);
    }

    /// <summary>Reads the source paths of one response file line.</summary>
    /// <param name="line">A line that is not an option.</param>
    /// <returns>The quoted paths in the line; the whole line when it has none.</returns>
    private static string[] SourcePaths(string line)
    {
        // A line may hold several quoted paths with nothing between them.
        string[] quoted = QuotedPattern().Matches(line).Select(match => match.Groups[1].Value).ToArray();
        if (quoted.Length > 0)
        {
            return quoted;
        }

        return [line];
    }

    /// <summary>Finds the folder of a module's .Build.cs.</summary>
    /// <param name="sourceRoot">The project's Source folder.</param>
    /// <param name="name">The module's name.</param>
    /// <returns>The folder; null when the Source folder or the script is missing.</returns>
    private static string? BuildScriptFolder(string sourceRoot, string name)
    {
        if (!Directory.Exists(sourceRoot))
        {
            return null;
        }

        string? buildScript = Directory.EnumerateFiles(sourceRoot, name + ".Build.cs", SearchOption.AllDirectories).FirstOrDefault();
        if (buildScript is null)
        {
            return null;
        }

        return Path.GetDirectoryName(buildScript);
    }

    /// <summary>Reads the .NET major version of the reference pack the module compiles against.</summary>
    /// <param name="references">The referenced assemblies' full paths.</param>
    /// <returns>The pack's major version; the running runtime's when no reference is in a pack.</returns>
    private static int RuntimeMajorOf(string[] references)
    {
        Match? pack = references.Select(reference => RefPackPattern().Match(reference)).FirstOrDefault(match => match.Success);
        if (pack is null)
        {
            return Environment.Version.Major;
        }

        return int.Parse(pack.Groups[1].Value);
    }

    /// <summary>Matches a quoted path.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex QuotedPattern();

    /// <summary>Matches a reference inside a Microsoft.NETCore.App.Ref pack and captures its major version.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex(@"Microsoft\.NETCore\.App\.Ref[\\/](\d+)\.")]
    private static partial Regex RefPackPattern();
}

/// <summary>Reads the diagnostics of a SARIF 2.1 log the C# compiler wrote (<c>/errorlog</c>).</summary>
public static class FlaxSarifReader
{
    /// <summary>The diagnostics: errors, warnings, and notes as "info".</summary>
    /// <param name="sarif">The SARIF JSON.</param>
    /// <returns>Every result of every run.</returns>
    public static IReadOnlyList<FlaxCompilerDiagnostic> Read(string sarif)
    {
        using JsonDocument document = JsonDocument.Parse(sarif);
        List<FlaxCompilerDiagnostic> diagnostics = [];
        foreach (JsonElement run in document.RootElement.GetProperty("runs").EnumerateArray())
        {
            if (!run.TryGetProperty("results", out JsonElement results))
            {
                continue;
            }

            foreach (JsonElement result in results.EnumerateArray())
            {
                diagnostics.Add(ReadResult(result));
            }
        }

        return diagnostics;
    }

    /// <summary>Reads one result of a run.</summary>
    /// <param name="result">The SARIF result.</param>
    /// <returns>The diagnostic.</returns>
    private static FlaxCompilerDiagnostic ReadResult(JsonElement result)
    {
        FlaxSarifPlace place = ReadPlace(result);
        string severity = ReadSeverity(result);
        string code = "";
        if (result.TryGetProperty("ruleId", out JsonElement rule))
        {
            code = rule.GetString() ?? "";
        }

        string message = result.GetProperty("message").GetProperty("text").GetString() ?? "";
        return new FlaxCompilerDiagnostic(place.File, place.Line, place.Column, severity, code, message);
    }

    /// <summary>Reads the severity of a result.</summary>
    /// <param name="result">The SARIF result.</param>
    /// <returns>error, warning or info; warning when the result has no level.</returns>
    private static string ReadSeverity(JsonElement result)
    {
        string? level = "warning";
        if (result.TryGetProperty("level", out JsonElement levelElement))
        {
            level = levelElement.GetString();
        }

        if (level == "note")
        {
            return "info";
        }

        return level ?? "warning";
    }

    /// <summary>Reads the first location of a result.</summary>
    /// <param name="result">The SARIF result.</param>
    /// <returns>The place; an empty file and zero line and column when the result has none.</returns>
    private static FlaxSarifPlace ReadPlace(JsonElement result)
    {
        bool hasLocation = result.TryGetProperty("locations", out JsonElement locations) && locations.GetArrayLength() > 0;
        if (!hasLocation)
        {
            return new FlaxSarifPlace("", 0, 0);
        }

        if (!locations[0].TryGetProperty("physicalLocation", out JsonElement physical))
        {
            return new FlaxSarifPlace("", 0, 0);
        }

        string file = ReadFile(physical);
        int line = 0;
        int column = 0;
        if (physical.TryGetProperty("region", out JsonElement region))
        {
            line = ReadNumber(region, "startLine");
            column = ReadNumber(region, "startColumn");
        }

        return new FlaxSarifPlace(file, line, column);
    }

    /// <summary>Reads the file of a physical location.</summary>
    /// <param name="physical">The SARIF physical location.</param>
    /// <returns>The local path for a file URI, the URI text otherwise; empty when none.</returns>
    private static string ReadFile(JsonElement physical)
    {
        if (!physical.TryGetProperty("artifactLocation", out JsonElement artifact))
        {
            return "";
        }

        if (!artifact.TryGetProperty("uri", out JsonElement uri))
        {
            return "";
        }

        string text = uri.GetString() ?? "";
        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? parsed) && parsed.IsFile)
        {
            return parsed.LocalPath;
        }

        return text;
    }

    /// <summary>Reads a whole number of a region.</summary>
    /// <param name="region">The SARIF region.</param>
    /// <param name="name">The property's name.</param>
    /// <returns>The number; 0 when the region lacks the property.</returns>
    private static int ReadNumber(JsonElement region, string name)
    {
        if (region.TryGetProperty(name, out JsonElement number))
        {
            return number.GetInt32();
        }

        return 0;
    }

    /// <summary>Where a diagnostic is.</summary>
    /// <param name="File">The file; empty when unknown.</param>
    /// <param name="Line">The line, 1-based; 0 when unknown.</param>
    /// <param name="Column">The column, 1-based; 0 when unknown.</param>
    private sealed record FlaxSarifPlace(string File, int Line, int Column);
}

/// <summary>The outcome of a script check.</summary>
/// <param name="Status">passed, failed, or unavailable (the check could not run; Reason says why).</param>
/// <param name="Seconds">How long it took.</param>
/// <param name="Reason">Why it was unavailable.</param>
/// <param name="Modules">Each module's outcome.</param>
/// <param name="Errors">The compiler errors, structured.</param>
/// <param name="Warnings">The compiler warnings.</param>
public sealed record FlaxScriptCheckResult(
    string Status,
    double Seconds,
    string? Reason,
    IReadOnlyList<FlaxScriptCheckModule> Modules,
    IReadOnlyList<FlaxCompilerDiagnostic> Errors,
    IReadOnlyList<FlaxCompilerDiagnostic> Warnings)
{
    /// <summary>A check that could not run.</summary>
    public static FlaxScriptCheckResult Unavailable(double seconds, string reason) => new("unavailable", seconds, reason, [], [], []);
}

/// <summary>One module's outcome in a script check.</summary>
/// <param name="Module">The module's name.</param>
/// <param name="Project">Its project.</param>
/// <param name="Status">passed, failed, or skipped (a module it references did not compile).</param>
/// <param name="Sources">How many source files were compiled.</param>
/// <param name="Milliseconds">How long its compile took.</param>
/// <param name="Note">Why it was skipped.</param>
public sealed record FlaxScriptCheckModule(string Module, string Project, string Status, int Sources, double Milliseconds, string? Note);
