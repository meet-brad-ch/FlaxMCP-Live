using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEditor.Content;
using FlaxEngine;
#if USE_LARGE_WORLDS
using Real = System.Double;
#else
using Real = System.Single;
#endif

namespace FlaxMcpBridge.Editor;

/// <summary>
/// The live-editor part of the bridge: this plugin generation attaches its request handler to the bridge host
/// (Host/), which owns the named pipe and outlives script reloads. This part also holds the requests that upstream does
/// not have (compile, open scene, game commands) and the hooks that the upstream requests call.
/// </summary>
internal sealed partial class PipeBridgeServer
{
    /// <summary>How many times <see cref="OpenSceneAsync"/> checks whether the scene is loaded.</summary>
    private const int OpenSceneAttempts = 30;

    /// <summary>The time between two checks of <see cref="OpenSceneAsync"/>, in milliseconds.</summary>
    private const int OpenSceneDelayMilliseconds = 100;

    /// <summary>The load time of this plugin generation, in UTC ticks.</summary>
    private static readonly long LoadedUtcTicks = DateTime.UtcNow.Ticks;

    /// <summary>
    /// The built file of each in-memory assembly, by name and module version: found once per plugin generation.
    /// </summary>
    private static readonly ConcurrentDictionary<string, string?> ReferencePaths = new(StringComparer.Ordinal);

    /// <summary>True when this plugin generation is detached: work it queued for the main thread is skipped.</summary>
    private static volatile bool _detached;

    /// <summary>This plugin generation: its load time in UTC ticks.</summary>
    public static long Generation => LoadedUtcTicks;

    /// <summary>The bridge protocol this plugin speaks.</summary>
    public static int Protocol => ProtocolVersion;

    /// <summary>
    /// Attaches this plugin generation to the bridge host (the host starts when needed) and stops the host when the
    /// editor exits.
    /// </summary>
    /// <exception cref="InvalidOperationException">The host sources were not found or did not compile.</exception>
    public void Start()
    {
        HostBootstrap.Attach(Generation, Protocol, HandleRequestAsync);
        Scripting.Exit += OnExit;
    }

    /// <summary>Detaches this generation from the bridge host and marks it detached: work it queued is skipped.</summary>
    public void Dispose()
    {
        Scripting.Exit -= OnExit;
        HostBootstrap.Detach(Generation);
        _detached = true;
    }

    /// <summary>Stops the bridge host when the editor exits.</summary>
    private static void OnExit()
    {
        HostBootstrap.Shutdown();
    }

    /// <summary>Answers one request line within the request timeout; failures become error responses.</summary>
    /// <param name="line">The request JSON.</param>
    /// <returns>The response JSON.</returns>
    public async Task<string> HandleRequestAsync(string line)
    {
        try
        {
            Task<string> dispatch = DispatchAsync(line);
            Task finished = await Task.WhenAny(dispatch, Task.Delay(RequestTimeout));
            if (finished != dispatch)
            {
                ObserveFault(dispatch);
                return SerializeError(
                    ReadRequestId(line),
                    "request_timeout",
                    $"The editor action did not complete within {RequestTimeout.TotalSeconds:0} seconds."
                );
            }

            return await dispatch;
        }
        catch (BridgeRequestException ex)
        {
            return SerializeError(ReadRequestId(line), ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            return SerializeError(ReadRequestId(line), "request_failed", ex.Message);
        }
    }

    /// <summary>
    /// Queues work for the main thread unless this plugin generation has detached: the engine keeps its queue across
    /// a script reload, and work queued by the old generation must not act on the reloaded editor.
    /// </summary>
    /// <param name="action">The work.</param>
    private static void OnMainThread(Action action)
    {
        Scripting.InvokeOnUpdate(() =>
        {
            if (!_detached)
            {
                action();
            }
        });
    }

    /// <summary>Answers the requests that upstream's dispatcher does not know.</summary>
    /// <param name="method">The request method.</param>
    /// <param name="root">The request.</param>
    /// <returns>The task of the answer.</returns>
    /// <exception cref="InvalidOperationException">The method is unknown.</exception>
    private static async Task<object> DispatchLiveAsync(string method, JsonElement root)
    {
        object result = method switch
        {
            "play_mode_state" => await GetPlayModeStateAsync(),
            "compile" => await CompileAsync(),
            "compile_status" => await GetCompileStatusAsync(),
            "open_scene" => await OpenSceneAsync(RequireParam(root, "path")),
            "game_commands" => ListGameCommands(),
            "find_actors" => await FindActorsAsync(
                OptionalParam(root, "nameContains"), OptionalParam(root, "actorType"), OptionalParam(root, "scriptType"),
                OptionalIntParam(root, "limit") ?? 50
            ),
            "get_member" => await GetMemberAsync(RequireParam(root, "actorId"), OptionalParam(root, "scriptType"), RequireParam(root, "member")),
            "set_member" => await SetMemberAsync(
                RequireParam(root, "actorId"), OptionalParam(root, "scriptType"), RequireParam(root, "member"), RequireValueParam(root, "value")
            ),
            "invoke_method" => await InvokeMethodAsync(
                OptionalParam(root, "typeName"), RequireParam(root, "method"), OptionalParam(root, "actorId"),
                OptionalParam(root, "scriptType"), RequireValueParam(root, "args")
            ),
            "add_script" => await AddScriptAsync(
                RequireParam(root, "actorId"), RequireParam(root, "scriptType"), OptionalIntParam(root, "orderInParent")
            ),
            "game_command" => await RunGameCommandAsync(RequireParam(root, "name"), OptionalParam(root, "args") ?? ""),
            _ => throw new InvalidOperationException($"Unknown method '{method}'"),
        };
        return result;
    }

    /// <summary>Reads a vector component: float, or double in an engine built with large worlds.</summary>
    /// <param name="value">The JSON number.</param>
    /// <returns>The number, read as double and cast to the engine's component type.</returns>
    private static Real ReadReal(JsonElement value)
    {
        double number = value.GetDouble();
        return (Real)number;
    }

    /// <summary>Tells whether a vector component is finite; it is float, or double with large worlds.</summary>
    /// <param name="value">The component.</param>
    /// <returns>True when the component is neither infinite nor NaN.</returns>
    private static bool IsFiniteReal(Real value)
    {
        return double.IsFinite(value);
    }

    /// <summary>Builds a vector object from double components: Vector3's components in an engine built with large worlds.</summary>
    /// <param name="x">The X component.</param>
    /// <param name="y">The Y component.</param>
    /// <param name="z">The Z component.</param>
    /// <returns>An object with the fields x, y and z.</returns>
    private static object BuildVector3(double x, double y, double z)
    {
        return new { x, y, z };
    }

    /// <summary>Saves a material graph as a lit material and reloads the material.</summary>
    /// <remarks>
    /// MaterialInfo.Default has ShadingModel.Unlit, which makes Flax render only the Emissive input and ignore
    /// BaseColor, Roughness and Metallic. Without the reload, actors that bind the new material right after it was
    /// created render it black: its GPU-side compiled form is not picked up.
    /// </remarks>
    /// <param name="material">The material.</param>
    /// <param name="surfaceData">The serialized graph.</param>
    /// <returns>True when the save failed (as Material.SaveSurface); the material is then not reloaded.</returns>
    private static bool SaveLitSurface(Material material, byte[] surfaceData)
    {
        MaterialInfo info = MaterialInfo.Default;
        info.ShadingModel = MaterialShadingModel.Lit;
        bool failed = material.SaveSurface(surfaceData, info);
        if (!failed)
        {
            material.Reload();
        }

        return failed;
    }

    /// <summary>Saves every loaded scene and the other modified assets. Main thread only.</summary>
    /// <returns>The answer: the main thread id, saved, and the number of scenes saved.</returns>
    /// <exception cref="InvalidOperationException">The editor is in play mode.</exception>
    private static object SaveAllScenes()
    {
        FlaxEditor.Editor editor = FlaxEditor.Editor.Instance;
        if (FlaxEditor.Editor.IsPlayMode)
        {
            throw new InvalidOperationException(
                "The editor is in play mode: stop it before saving, or the play-time scene is written.");
        }

        // Changes made through the API do not mark a scene as modified, and SaveAll skips unmodified scenes.
        foreach (Scene scene in Level.Scenes)
        {
            editor.Scene.MarkSceneEdited(scene);
        }

        editor.SaveAll();
        return new { mainThreadId = Globals.MainThreadID, saved = true, savedScenes = Level.ScenesCount };
    }

    /// <summary>
    /// Reads the play mode state, then queues a play mode request without waiting for it: starting play loads the
    /// scene on the main thread, which can take longer than one request may wait. The server polls play_mode_state
    /// until the state is reached.
    /// </summary>
    /// <param name="request">The request; it runs on the main thread, its return value is not used.</param>
    /// <returns>The task of the state before the request.</returns>
    private static async Task<PlayModeState> QueuePlayModeRequestAsync(Func<object> request)
    {
        PlayModeState state = await ReadPlayModeStateAsync();
        OnMainThread(() =>
        {
            request();
        });
        return state;
    }

    /// <summary>Builds a play mode answer.</summary>
    /// <param name="state">The play mode state.</param>
    /// <param name="requestedAction">The requested action, or "state" for a state query.</param>
    /// <returns>The answer: the main thread id, the requested action, and whether play mode is on and paused.</returns>
    private static object PlayModeResponse(PlayModeState state, string requestedAction)
    {
        return new
        {
            mainThreadId = state.MainThreadId,
            requestedAction,
            isPlayMode = state.IsPlayMode,
            isPaused = state.IsPaused,
        };
    }

    /// <summary>Reads the play mode state.</summary>
    /// <returns>The task of the answer, with the requested action "state".</returns>
    private static async Task<object> GetPlayModeStateAsync()
    {
        PlayModeState state = await ReadPlayModeStateAsync();
        return PlayModeResponse(state, "state");
    }

    /// <summary>Reads the script compile state. Main thread only.</summary>
    /// <returns>The compile state, and the load time of this plugin generation.</returns>
    private static object CompileStatus()
    {
        return new
        {
            isCompiling = FlaxEditor.ScriptsBuilder.IsCompiling,
            lastCompilationFailed = FlaxEditor.ScriptsBuilder.LastCompilationFailed,
            compilationsCount = FlaxEditor.ScriptsBuilder.CompilationsCount,
            loadedUtcTicks = LoadedUtcTicks,
            // The editor's file watcher marks changed sources; while set, the editor compiles (and reloads) again on its own.
            isSourceDirty = FlaxEditor.ScriptsBuilder.IsSourceDirty,
        };
    }

    /// <summary>Starts a script compile, unless one runs already.</summary>
    /// <returns>The task of the compile state before the request.</returns>
    /// <exception cref="InvalidOperationException">The editor is in play mode.</exception>
    private static Task<object> CompileAsync()
    {
        return InvokeOnUpdateAsync(RequestCompile);
    }

    /// <summary>Starts a script compile, unless one runs already. Main thread only.</summary>
    /// <remarks>
    /// The editor compiles changed sources on its own (its file watcher marks them, then it compiles and reloads): a
    /// compile on top of that reloads a second time. Changed sources go through the editor's own check; only unchanged
    /// ones are compiled explicitly.
    /// </remarks>
    /// <returns>The compile state before the request.</returns>
    /// <exception cref="InvalidOperationException">The editor is in play mode.</exception>
    private static object RequestCompile()
    {
        if (FlaxEditor.Editor.IsPlayMode)
        {
            throw new InvalidOperationException("The editor is in play mode: stop it before compiling.");
        }

        object before = CompileStatus();
        if (FlaxEditor.ScriptsBuilder.IsCompiling)
        {
            return before;
        }

        if (FlaxEditor.ScriptsBuilder.IsSourceDirty)
        {
            FlaxEditor.ScriptsBuilder.CheckForCompile();
        }
        else
        {
            FlaxEditor.ScriptsBuilder.Compile();
        }

        return before;
    }

    /// <summary>Reads the script compile state.</summary>
    /// <returns>The task of the compile state.</returns>
    private static Task<object> GetCompileStatusAsync()
    {
        return InvokeOnUpdateAsync(CompileStatus);
    }

    /// <summary>Opens a scene and waits until it is loaded.</summary>
    /// <param name="path">The scene asset path, absolute or relative to the project's Content folder.</param>
    /// <returns>The task of the answer: the scene id and name, and the names of the loaded scenes.</returns>
    /// <exception cref="InvalidOperationException">The editor is in play mode.</exception>
    /// <exception cref="FileNotFoundException">No scene asset is at the path.</exception>
    /// <exception cref="TimeoutException">The scene did not load within 3 seconds.</exception>
    private static async Task<object> OpenSceneAsync(string path)
    {
        var item = (AssetItem)await InvokeOnUpdateAsync(() => RequestOpenScene(path));
        for (int attempt = 0; attempt < OpenSceneAttempts; attempt++)
        {
            object? opened = await InvokeOnUpdateAsync(() => OpenedScene(item.ID));
            if (opened is not null)
            {
                return opened;
            }

            await Task.Delay(OpenSceneDelayMilliseconds);
        }

        throw new TimeoutException($"The scene '{path}' did not finish loading within 3 seconds.");
    }

    /// <summary>Asks the editor to open a scene. Main thread only.</summary>
    /// <param name="path">The scene asset path, absolute or relative to the project's Content folder.</param>
    /// <returns>The scene asset item.</returns>
    /// <exception cref="InvalidOperationException">The editor is in play mode.</exception>
    /// <exception cref="FileNotFoundException">No scene asset is at the path.</exception>
    private static AssetItem RequestOpenScene(string path)
    {
        if (FlaxEditor.Editor.IsPlayMode)
        {
            throw new InvalidOperationException("The editor is in play mode: stop it before opening a scene.");
        }

        string full = Path.IsPathRooted(path) ? path : Path.Combine(Globals.ProjectContentFolder, path);
        AssetItem found = FlaxEditor.Editor.Instance.ContentDatabase.Find(full) as AssetItem ??
            throw new FileNotFoundException($"No scene asset at '{full}'.");
        FlaxEditor.Editor.Instance.Scene.OpenScene(found.ID);
        return found;
    }

    /// <summary>Describes a scene when it is loaded. Main thread only.</summary>
    /// <param name="sceneId">The scene id.</param>
    /// <returns>The scene id and name and the names of the loaded scenes; null when the scene is not loaded yet.</returns>
    private static object OpenedScene(Guid sceneId)
    {
        Scene? scene = Level.Scenes.FirstOrDefault(candidate => candidate.ID == sceneId);
        if (scene is null)
        {
            return null!;
        }

        return new
        {
            mainThreadId = Globals.MainThreadID,
            sceneId = scene.ID.ToString(),
            sceneName = scene.Name,
            scenes = Level.Scenes.Select(candidate => candidate.Name).ToArray(),
        };
    }

    /// <summary>Lists the registered game commands.</summary>
    /// <returns>The answer: each command's name, description and owner.</returns>
    private static object ListGameCommands()
    {
        return new
        {
            commands = GameCommands.List()
                .Select(command => new { name = command.Name, description = command.Description, owner = command.Owner })
                .ToArray(),
        };
    }

    /// <summary>Runs a registered game command on the main thread.</summary>
    /// <param name="name">The command name.</param>
    /// <param name="args">The argument string.</param>
    /// <returns>The task of the answer: the command's result text and how long it ran.</returns>
    /// <exception cref="BridgeRequestException">
    /// No command has the name (code <c>unknown_command</c>), or the command threw (code <c>command_failed</c>).
    /// </exception>
    private static Task<object> RunGameCommandAsync(string name, string args)
    {
        GameCommand? command = GameCommands.Find(name);
        if (command is null)
        {
            IEnumerable<string> names = GameCommands.List().Select(info => info.Name).DefaultIfEmpty("none");
            throw new BridgeRequestException(
                "unknown_command", $"No game command '{name}' is registered. Registered: {string.Join(", ", names)}.");
        }

        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnMainThread(() =>
        {
            RunGameCommand(command, name, args, tcs);
        });
        return tcs.Task;
    }

    /// <summary>Runs a game command and completes the answer. Main thread only.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The command name.</param>
    /// <param name="args">The argument string.</param>
    /// <param name="tcs">Completes with the answer, or with the error (code <c>command_failed</c>).</param>
    private static void RunGameCommand(GameCommand command, string name, string args, TaskCompletionSource<object> tcs)
    {
        System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            string result = command.Handler(args);
            tcs.TrySetResult(new
            {
                mainThreadId = Globals.MainThreadID,
                name,
                result,
                milliseconds = watch.Elapsed.TotalMilliseconds,
                isPlayMode = FlaxEditor.Editor.IsPlayMode,
            });
        }
        catch (Exception ex)
        {
            tcs.TrySetException(new BridgeRequestException("command_failed", $"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    /// <summary>
    /// Lists the newest loaded copy of each assembly: a script reload that cannot unload the old assemblies leaves
    /// several copies of the game's assemblies loaded, and a snippet or a type name that sees all of them is ambiguous.
    /// </summary>
    /// <returns>The last loaded non-dynamic assembly of each name.</returns>
    private static Assembly[] NewestAssemblies()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic)
            .GroupBy(assembly => assembly.GetName().Name)
            .Select(group => group.Last())
            .ToArray();
    }

    /// <summary>A snippet compiled by the host.</summary>
    /// <param name="AssemblyName">The name of the load context the snippet runs in.</param>
    /// <param name="CacheHit">Whether the host had the image already.</param>
    /// <param name="CompileMilliseconds">The time the host spent.</param>
    /// <param name="ReferenceCount">How many references the compile used.</param>
    private sealed record CompiledSnippet(string AssemblyName, bool CacheHit, double CompileMilliseconds, int ReferenceCount);

    /// <summary>
    /// Compiles a snippet in the bridge host, whose compiler stays loaded across script reloads and which keeps the
    /// images of recent snippets.
    /// </summary>
    /// <param name="source">The whole compilation unit.</param>
    /// <param name="loadedAssemblies">The assemblies the snippet may use.</param>
    /// <param name="assemblyStream">Receives the compiled image.</param>
    /// <returns>The name for the snippet's load context, and the compile figures.</returns>
    /// <exception cref="InvalidOperationException">No host is attached, or the source does not compile.</exception>
    private static CompiledSnippet CompileInHost(string source, Assembly[] loadedAssemblies, MemoryStream assemblyStream)
    {
        // The game's script assemblies are loaded from memory (no Location): they are referenced through their newest
        // built file, so a snippet can name a game type.
        string[] references = loadedAssemblies
            .Select(CachedReferencePath)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        byte[] image = HostBootstrap.CompileSnippet(
            source, references, out bool cacheHit, out double compileMilliseconds, out int referenceCount);
        assemblyStream.Write(image, 0, image.Length);
        string assemblyName = "FlaxMcpDynamic_" + Guid.NewGuid().ToString("N");
        return new CompiledSnippet(assemblyName, cacheHit, compileMilliseconds, referenceCount);
    }

    /// <summary>Adds the compile figures to a snippet's answer.</summary>
    /// <param name="snippetResult">The answer of the snippet: main thread id, result type name and result.</param>
    /// <param name="compilation">The compile figures.</param>
    /// <returns>The answer with compileMilliseconds, cacheHit and referenceCount added.</returns>
    private static Dictionary<string, object?> SnippetResponse(object snippetResult, CompiledSnippet compilation)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var response = (Dictionary<string, object?>)NormalizeDynamicResult(snippetResult, visited, 0)!;
        response["compileMilliseconds"] = compilation.CompileMilliseconds;
        response["cacheHit"] = compilation.CacheHit;
        response["referenceCount"] = compilation.ReferenceCount;
        return response;
    }

    /// <summary>Finds the file to reference for an assembly; the search runs once per assembly and module version.</summary>
    /// <param name="assembly">The assembly.</param>
    /// <returns>The file path; null when no file was found.</returns>
    private static string? CachedReferencePath(Assembly assembly)
    {
        if (!string.IsNullOrEmpty(assembly.Location))
        {
            return assembly.Location;
        }

        string key = assembly.GetName().Name + "|" + assembly.ManifestModule.ModuleVersionId;
        return ReferencePaths.GetOrAdd(key, _ => ReferencePath(assembly));
    }

    /// <summary>
    /// Finds the file to reference for an assembly: its location, or for an assembly loaded from memory the newest
    /// file of its name in a project's Binaries folder. Each project builds into its own Binaries folder (a plugin such
    /// as this bridge too); the engine's folder is skipped.
    /// </summary>
    /// <param name="assembly">The assembly.</param>
    /// <returns>The file path; null when no file was found.</returns>
    private static string? ReferencePath(Assembly assembly)
    {
        if (!string.IsNullOrEmpty(assembly.Location))
        {
            return assembly.Location;
        }

        string? name = assembly.GetName().Name;
        if (name is null)
        {
            return null;
        }

        string engineFolder = Path.GetFullPath(Globals.StartupFolder);
        return FlaxEditor.Editor.Instance.GameProject.GetAllProjects()
            .Select(project => Path.Combine(project.ProjectFolderPath, "Binaries"))
            .Where(binaries => Directory.Exists(binaries) &&
                               !Path.GetFullPath(binaries).StartsWith(engineFolder, StringComparison.OrdinalIgnoreCase))
            .SelectMany(binaries => Directory.EnumerateFiles(binaries, name + ".dll", SearchOption.AllDirectories))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
