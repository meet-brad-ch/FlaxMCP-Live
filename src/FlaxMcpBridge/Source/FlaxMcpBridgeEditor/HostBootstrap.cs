using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace FlaxMcpBridge.Editor;

/// <summary>
/// Finds or starts the bridge host (Host/*.cs) and attaches this plugin generation to it. The host is compiled once
/// per editor process and loaded into the Default AssemblyLoadContext, so script reloads do not stop it. When its
/// sources change, the new version is compiled under its own name and takes the pipe over from the old one.
/// </summary>
internal static class HostBootstrap
{
    /// <summary>Full name of the host's entry type.</summary>
    private const string EntryTypeName = "FlaxMcpBridge.Host.BridgeHostEntry";

    /// <summary>Full name of the host type that feeds the editor's events into the host's ring.</summary>
    private const string EventsTypeName = "FlaxMcpBridge.Host.Flax.BridgeHostFlaxEvents";

    /// <summary>Full name of the host's snippet compiler type.</summary>
    private const string SnippetCompilerTypeName = "FlaxMcpBridge.Host.Roslyn.SnippetCompiler";

    /// <summary>Name prefix of every compiled host assembly; the host version follows it.</summary>
    private const string AssemblyPrefix = "FlaxMcpBridgeHost-";

    /// <summary>The host source file that marks a project's Host folder as the bridge host.</summary>
    private const string MarkerFile = "BridgeHostEntry.cs";

    /// <summary>How long a new host waits for a plugin generation to attach, in seconds.</summary>
    private const double AttachWaitSeconds = 10;

    /// <summary>The host's snippet compile method; set by <see cref="Attach"/>, null before the first attach.</summary>
    private static MethodInfo? _compileSnippet;

    /// <summary>The engine's Roslyn assemblies, which the host loads into the Default load context.</summary>
    private static string RoslynFolder => Path.Combine(Globals.StartupFolder, "Binaries", "Tools");

    /// <summary>Starts the host when needed and attaches the given dispatcher as the current generation.</summary>
    /// <param name="generation">This plugin generation.</param>
    /// <param name="protocolVersion">The bridge protocol the plugin speaks.</param>
    /// <param name="dispatch">Answers a request line with a response line.</param>
    /// <exception cref="InvalidOperationException">The host sources were not found or did not compile.</exception>
    public static void Attach(long generation, int protocolVersion, Func<string, Task<string>> dispatch)
    {
        string folder = SourceFolder();
        string[] files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();
        string[] sources = files.Select(File.ReadAllText).ToArray();
        string version = Version(sources);
        Type? loadedEntry = LoadedEntry(version);
        Type entry = loadedEntry ?? Compile(version, files, sources);
        ShutDownOtherVersions(version);
        StartHostIfNeeded(entry, version);

        // The editor's events feed the host's ring from the first attach on (later calls do nothing).
        Type eventsType = entry.Assembly.GetType(EventsTypeName, throwOnError: true)!;
        Invoke(eventsType, "Start");
        Type snippetCompiler = entry.Assembly.GetType(SnippetCompilerTypeName, throwOnError: true)!;
        Invoke(snippetCompiler, "Configure", RoslynFolder);
        _compileSnippet = snippetCompiler.GetMethod("Compile", BindingFlags.Public | BindingFlags.Static)!;
        string pluginVersion = typeof(HostBootstrap).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var attachArguments = new Dictionary<string, object>
        {
            ["generation"] = generation,
            ["protocolVersion"] = protocolVersion,
            ["pluginVersion"] = pluginVersion,
            ["engineBuild"] = Globals.EngineBuildNumber,
        };
        string attachJson = JsonSerializer.Serialize(attachArguments);
        Invoke(entry, "Attach", attachJson, dispatch);
    }

    /// <summary>Starts the host of this version when it is not running yet.</summary>
    /// <param name="entry">The host's entry type.</param>
    /// <param name="version">The host version, as the hash of its sources.</param>
    private static void StartHostIfNeeded(Type entry, string version)
    {
        bool isStarted = (bool)entry.GetProperty("IsStarted")!.GetValue(null)!;
        if (isStarted)
        {
            return;
        }

        string applicationData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var startArguments = new Dictionary<string, object>
        {
            ["projectFolder"] = Globals.ProjectFolder,
            ["sessionsDirectory"] = Path.Combine(applicationData, "FlaxMcp", "sessions"),
            ["hostVersion"] = version,
            ["attachWaitSeconds"] = AttachWaitSeconds,
        };
        string startJson = JsonSerializer.Serialize(startArguments);
        Invoke(entry, "Start", startJson);
    }

    /// <summary>Compiles an editor_execute_csharp snippet in the host, whose compiler stays warm across reloads.</summary>
    /// <param name="source">The whole compilation unit.</param>
    /// <param name="referencePaths">The assemblies it may use, as files.</param>
    /// <param name="cacheHit">Whether the host had the image already.</param>
    /// <param name="milliseconds">The time the host spent.</param>
    /// <param name="referenceCount">How many references the compile used.</param>
    /// <returns>The compiled assembly.</returns>
    /// <exception cref="InvalidOperationException">No host is attached, or the source does not compile.</exception>
    public static byte[] CompileSnippet(
        string source, string[] referencePaths, out bool cacheHit, out double milliseconds, out int referenceCount)
    {
        var compile = _compileSnippet ?? throw new InvalidOperationException("The bridge host is not attached.");
        var arguments = new object?[] { source, referencePaths, null, null, null };
        var image = (byte[])compile.Invoke(null, BindingFlags.DoNotWrapExceptions, null, arguments, null)!;
        cacheHit = (bool)arguments[2]!;
        milliseconds = (double)arguments[3]!;
        referenceCount = (int)arguments[4]!;
        return image;
    }

    /// <summary>Detaches a plugin generation from every running host (there is one).</summary>
    /// <param name="generation">The generation going away.</param>
    public static void Detach(long generation)
    {
        foreach (var entry in RunningEntries())
        {
            Invoke(entry, "Detach", generation);
        }
    }

    /// <summary>Stops every running host and removes its handshake file (the editor is exiting).</summary>
    public static void Shutdown()
    {
        foreach (var entry in RunningEntries())
        {
            Invoke(entry, "Shutdown");
        }
    }

    /// <summary>Finds the one referenced project folder that holds the bridge host sources.</summary>
    /// <returns>The path of its Host folder.</returns>
    /// <exception cref="InvalidOperationException">No project or several projects have a Host folder with the marker file.</exception>
    private static string SourceFolder()
    {
        string[] folders = FlaxEditor.Editor.Instance.GameProject.GetAllProjects()
            .Select(project => Path.Combine(project.ProjectFolderPath, "Host"))
            .Where(folder => File.Exists(Path.Combine(folder, MarkerFile)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (folders.Length != 1)
        {
            throw new InvalidOperationException(
                $"Expected one referenced project with Host/{MarkerFile} (the FlaxMcp bridge), found {folders.Length}.");
        }

        return folders[0];
    }

    /// <summary>Hashes the host sources into the version that names the compiled assembly.</summary>
    /// <param name="sources">The text of every host source file, in file order.</param>
    /// <returns>The first 16 hex digits of the SHA-256 hash, in lower case.</returns>
    private static string Version(IEnumerable<string> sources)
    {
        string joined = string.Join("\n// next file\n", sources);
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    /// <summary>Lists the host assemblies loaded in the Default load context, of every version.</summary>
    /// <returns>The assemblies whose name starts with the host prefix.</returns>
    private static IEnumerable<Assembly> HostAssemblies() =>
        AssemblyLoadContext.Default.Assemblies.Where(assembly =>
            assembly.GetName().Name?.StartsWith(AssemblyPrefix, StringComparison.Ordinal) == true);

    /// <summary>Finds the entry type of a host version that is already loaded.</summary>
    /// <param name="version">The host version.</param>
    /// <returns>The entry type, or null when that version is not loaded.</returns>
    private static Type? LoadedEntry(string version)
    {
        string assemblyName = AssemblyPrefix + version;
        Assembly? loaded = HostAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == assemblyName);
        return loaded?.GetType(EntryTypeName, throwOnError: true);
    }

    /// <summary>Lists the entry types of the hosts that are started.</summary>
    /// <returns>A snapshot of the started hosts.</returns>
    private static IEnumerable<Type> RunningEntries() =>
        HostAssemblies()
            .Select(assembly => assembly.GetType(EntryTypeName, throwOnError: true)!)
            .Where(entry => (bool)entry.GetProperty("IsStarted")!.GetValue(null)!)
            .ToArray();

    /// <summary>Stops every started host that is not the given version and logs a warning for each.</summary>
    /// <param name="version">The host version that stays.</param>
    private static void ShutDownOtherVersions(string version)
    {
        string keptName = AssemblyPrefix + version;
        foreach (Type entry in RunningEntries().Where(entry => entry.Assembly.GetName().Name != keptName))
        {
            Invoke(entry, "Shutdown");
            string stoppedName = entry.Assembly.GetName().Name!;
            Debug.LogWarning(
                $"[FlaxMcpBridge] The bridge host changed: {stoppedName} stopped, {keptName} " +
                "takes over. The old host stays loaded until the editor restarts.");
        }
    }

    /// <summary>Compiles the host sources into an assembly and loads it into the Default load context.</summary>
    /// <param name="version">The host version; it names the assembly.</param>
    /// <param name="files">The paths of the source files.</param>
    /// <param name="sources">The text of each file, in the order of <paramref name="files"/>.</param>
    /// <returns>The host's entry type.</returns>
    /// <exception cref="InvalidOperationException">The sources did not compile.</exception>
    private static Type Compile(string version, IReadOnlyList<string> files, IReadOnlyList<string> sources)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
        var trees = files.Select((file, index) => CSharpSyntaxTree.ParseText(sources[index], parseOptions, file, Encoding.UTF8));
        IEnumerable<MetadataReference> references = CompileReferences();
        var options = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Enable);
        CSharpCompilation compilation = CSharpCompilation.Create(AssemblyPrefix + version, trees, references, options);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        if (!emitted.Success)
        {
            IEnumerable<Diagnostic> errors = emitted.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            throw new InvalidOperationException("The bridge host did not compile: " + string.Join("; ", errors));
        }

        image.Position = 0;
        Assembly assembly = AssemblyLoadContext.Default.LoadFromStream(image);
        return assembly.GetType(EntryTypeName, throwOnError: true)!;
    }

    /// <summary>
    /// Lists the references the host is compiled against: the runtime, the engine and Roslyn, one file for each
    /// assembly name. The trusted platform list names every managed file next to the editor, also old engine copies
    /// that a rebuild renamed aside (<c>&lt;guid&gt;_FlaxEngine.CSharp.dll</c>). Two files with one name stop the
    /// compile (CS1704), so the file of the assembly that the Default context loaded wins, else the first file.
    /// </summary>
    /// <returns>One metadata reference per assembly name.</returns>
    private static IEnumerable<MetadataReference> CompileReferences()
    {
        string trustedList = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        string[] platform = trustedList.Split(Path.PathSeparator);
        Dictionary<string, string> loaded = LoadedAssemblyFiles();
        var fileByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in platform)
        {
            string? name = ManagedAssemblyName(path);
            if (name is null)
            {
                continue;
            }

            if (fileByName.ContainsKey(name))
            {
                continue;
            }

            string file = path;
            if (loaded.TryGetValue(name, out string? loadedFile))
            {
                file = loadedFile;
            }

            fileByName[name] = file;
        }

        // The engine's managed assembly is in the Default load context too: the host may use it.
        Assembly engine = typeof(Debug).Assembly;
        string engineName = engine.GetName().Name!;
        fileByName[engineName] = engine.Location;
        string roslynFile = Path.Combine(RoslynFolder, "Microsoft.CodeAnalysis.dll");
        fileByName["Microsoft.CodeAnalysis"] = roslynFile;
        string roslynCSharpFile = Path.Combine(RoslynFolder, "Microsoft.CodeAnalysis.CSharp.dll");
        fileByName["Microsoft.CodeAnalysis.CSharp"] = roslynCSharpFile;

        var references = new List<MetadataReference>(fileByName.Count);
        foreach (string file in fileByName.Values)
        {
            MetadataReference reference = MetadataReference.CreateFromFile(file);
            references.Add(reference);
        }

        return references;
    }

    /// <summary>Lists the files of the assemblies that the Default load context has loaded, by assembly name.</summary>
    /// <returns>The file of each loaded assembly that has one; the first one for a name that is loaded twice.</returns>
    private static Dictionary<string, string> LoadedAssemblyFiles()
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Assembly assembly in AssemblyLoadContext.Default.Assemblies)
        {
            // A dynamic or in-memory assembly (the host itself, a snippet) has no file to reference.
            if (assembly.IsDynamic)
            {
                continue;
            }

            string location = assembly.Location;
            if (location.Length == 0)
            {
                continue;
            }

            string? name = assembly.GetName().Name;
            if (name is null)
            {
                continue;
            }

            if (files.ContainsKey(name))
            {
                continue;
            }

            files[name] = location;
        }

        return files;
    }

    /// <summary>Reads the assembly name of a file.</summary>
    /// <param name="path">The file path.</param>
    /// <returns>The simple assembly name; null for a native library.</returns>
    private static string? ManagedAssemblyName(string path)
    {
        try
        {
            return AssemblyName.GetAssemblyName(path).Name;
        }
        catch (BadImageFormatException)
        {
            // The trusted platform list also names the native libraries next to the runtime.
            return null;
        }
    }

    /// <summary>Calls a public static method of a host type; an exception of the method is not wrapped.</summary>
    /// <param name="entry">The type that holds the method.</param>
    /// <param name="method">The method name.</param>
    /// <param name="arguments">The arguments of the call.</param>
    private static void Invoke(Type entry, string method, params object[] arguments)
    {
        MethodInfo info = entry.GetMethod(method, BindingFlags.Public | BindingFlags.Static)!;
        info.Invoke(null, BindingFlags.DoNotWrapExceptions, null, arguments, null);
    }
}
