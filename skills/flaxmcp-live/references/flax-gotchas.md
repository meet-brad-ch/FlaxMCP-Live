# Flax gotchas — learned on a real port (Flax 1.12.6912, 2026-10-14)

Each item tells what happens, the evidence, and what to do. The measurements come from the port of a large game
project to Flax.

## Compiling and reloading

- **Stop play before you compile.** Scripts reload only in edit mode.
- **`ScriptsBuilder.Compile()` is asynchronous.** Wait for `Scripts reloading end` in the log, or listen to
  `ScriptsBuilder.CompilationSuccess` / `CompilationFailed` / `ScriptsReloadEnd` and read
  `ScriptsBuilder.LastCompilationFailed` / `IsCompiling`.
- **The file watcher compiles and reloads on its own** when a source file changes (0.14 s after the save, measured).
  A compile that you request in addition reloads a second time (2 reloads per edit, measured).
  `ScriptsBuilder.IsSourceDirty` tells that a change is pending. `CheckForCompile()` compiles it through the editor's
  own path. An error line in the log may come from that automatic build. Read only the lines written since your
  request.
- **Compiler errors are only in the editor log** (native lines). The editor runs Flax.Build with an empty `-logfile=`.
  Thus `Cache/Intermediate/Log.txt` holds project generation runs, not builds. Also, the editor's own Output Log
  drains the native log buffer (`Editor.Internal_ReadOutputLogs`). Thus a second reader would take lines from it.
  What works: Flax.Build keeps each module's exact compiler arguments in
  `Cache/Intermediate/<EditorTarget>/<Platform>/<Arch>/<Config>/<Module>.CSharp.response`. If you compile them again
  (current files, scratch output, `-shared`, `/errorlog:x.sarif,version=2.1`), you get structured errors in 0.3 s for
  four modules. That is FlaxMCP's `build_check_scripts`.
- **A failed build looks like a slow one** unless you watch for `Failed to run build tool` and `Sharing violation`,
  and also for `error CS`.
- **`Sharing violation` on every build:** a referenced DLL (FileReferences) was rewritten while the editor ran. The
  editor holds the old copy open (see stale assemblies). Never re-sync referenced DLLs while the editor runs. Restart
  the editor. Another form of the problem (2026-10-14, ParaNature.Core.dll re-synced with a new method): the build
  writes every module's DLL, but `editor_compile` reports `failed` with no errors and no reload. Snippets that call
  the new method fail with "Method not found". A restart loads it.
- **A `*.Build.cs` change (a new reference) makes the script check report false errors** (CS0234 for the new
  namespace). The check replays the old response files. Compile in the editor: call
  `FlaxEditor.ScriptsBuilder.Compile()` through `editor_execute_csharp`, then `editor_wait_for reload_end`. The next
  check passes.
- **Everything in the game's and plugins' assemblies reloads**, native modules too (`Scripting::Reload` destroys every
  binary module except Corlib and FlaxEngine). What survives is the Default AssemblyLoadContext, which holds
  FlaxEngine.CSharp with the editor's code. An assembly loaded there (FlaxMCP's bridge host) keeps its threads, pipes
  and event subscriptions across reloads. Measured: 0 lost answers over 6 reloads, attach/detach exactly once per
  reload, and the editor still exits in 5 s. The host cannot be unloaded. A changed host is loaded under a new name,
  and the old one stays.
- **What keeps old scripts alive after a reload:** a thread that still runs their code, and the runtime's own
  reflection caches in the Default context. Heap dumps found both (`dotnet-dump collect --type Full`, then `gcroot` of
  a `LoaderAllocator`). `dotnet-gcdump` misses most of a large heap. An example of the first cause is a game's
  background workers. Give such threads a stop and call it at play end. An example of the second cause:
  System.Text.Json's member-accessor cache held a game type. Clear those caches before the reload, the way .NET hot
  reload does. For each `AssemblyLoadContext.Default` assembly's `MetadataUpdateHandlerAttribute`, call the handler's
  static `ClearCache(Type[])` with null (CoreLib, ComponentModel and System.Text.Json have one). Measured: 6.18 GB
  kept after play and two reloads before, 48 MB after. `editor_host_status collect=true` shows how many old plugin
  load contexts are alive.
- **`Scripting.InvokeOnUpdate`'s queue survives a reload**: work that the old plugin queued runs against the reloaded
  editor. That work keeps the old load context until it runs. Guard queued work with a "detached" flag.
- **Managed log handlers see managed lines only**: `Debug.Logger.LogHandler.SendLog` (LogType, string, Object, string)
  does not receive the engine's native lines (compiler output, "Scripts reloading start").
- Flax's "Scripting AssemblyLoadContext was not unloaded" warning fires when Flax checks right after the reload. The
  old contexts were collected about a second later (counted with weak references). Thus the warning alone does not
  prove a leak.

## Stale assemblies and the editor's memory

- **A reload does not unload the old game assemblies** when anything still runs code from them. Here, a library's
  static background threads blocked in `Monitor.Wait`. The log says `Scripting AssemblyLoadContext was not unloaded`.
  After a day: 6 copies of the game types in the AppDomain and 59.5 GB of editor memory.
- **Consequence for snippets:** `AppDomain.GetAssemblies()` returns every old copy. A snippet that picked "the last"
  type found the stale one (a method added minutes earlier "did not exist"). Pick the type that has the member, or
  resolve types from an assembly the current editor module references (`typeof(SomeGameType).Assembly.GetType(...)`).
- **Restart the editor when it uses more than ~20 GB.** A fresh editor opens with no scene. Open the scene yourself.
- Memory also grows in play without any reload (8.5 to 14.3 GB over one 15-minute play session). Measure the memory
  in a long play.

## `editor_execute_csharp` and `editor_play_mode`

- Up to protocol 14, a snippet that threw came back only as `An error occurred invoking 'editor_execute_csharp'`. The
  client threw a plain exception, which the MCP SDK hides. From protocol 15, every bridge error carries its code and
  message (compiler errors with line and column).
- Up to protocol 14, a snippet could not name a game type (CS0234). The game's assemblies are loaded from memory and
  were left out of the references. From protocol 15, they are referenced through their built files (the newest copy
  of each assembly).
- Up to protocol 14, `editor_play_mode start` timed out on a heavy scene, although play began. The bridge waited 4 s
  on the main thread, which was loading the scene. From protocol 15, the request is queued and the server polls the
  state (up to 35 s from protocol 19, as a job).
- `editor_screenshot` needs a selected tab that has rendered. An unselected or idle viewport gives an error. A
  fallback to "any viewport with output" was tried and removed. It returned blank white images as success.

## Scenes

- **`editor_save` does not save scene changes made through the API.** Use
  `Editor.Instance.Scene.MarkSceneEdited(scene); Editor.Instance.Scene.SaveScenes();`.
- **Never save during play.** A save during play writes the play-time scene, with every actor that scripts created at
  run time (25 extra actors once). Give run-time actors `HideFlags = HideFlags.DontSave`.
- `Level.FindScript` fails in `OnAwake`. Walk the actor's scene instead.
- After an editor restart, `Level.ScenesCount` is 0. Open the scene with `Editor.Instance.Scene.OpenScene(assetItem.ID)`.

## Content

- **`Editor.Import(src, dst)` with the default options imports a PNG as ColorRGB and drops the alpha** (a black
  silhouette on transparent pixels rendered as a black rectangle). Pass options:
  `var o = FlaxEngine.Tools.TextureTool.Options.Default; o.Type = TextureFormatType.ColorRGBA; Editor.Import(src, dst, o);`
- **Cut-out (alpha-tested) textures need coverage-preserving mipmaps:** set `o.PreserveAlphaCoverage = true;
  o.PreserveAlphaCoverageReference = 0.5f;` (the material's mask threshold, Unity's `mipMapsPreserveCoverage`). Plain
  mipmaps average the alpha with the gaps. A spruce's needle texture with 15 % coverage fell to 6 % at mip 6 and 3 %
  at mip 7. Thus conifers looked bushy close up and as impostors, but burnt out at mid distance (2026-10-15). It
  looked like a LOD bug. Re-importing over the existing asset with `Editor.Import` keeps its ID, so materials keep the
  texture.
- **`new TextureTool.Options { … }` imports every texture as 1 x 1:** a C# struct initializer leaves `Scale` and
  `MaxSize` at 0 (the C++ defaults 1.0 and 8192 do not apply). Always start from `TextureTool.Options.Default` and set
  fields on the copy. Found 2026-10-14: 16 terrain textures of 1 KB each, and the rock showed only the shader's noise.
- **Load assets only by the registry's path spelling (`D:\Work/Project/Content/…`), or by ID.** A path from
  `Directory.GetFiles` changed to `D:/Work/…` (or with `Path.Combine`'s `\`) registers the same file a second time
  ("Founded duplicated asset", "Cannot modify duplicated asset ID"). Flax then gave the files **new asset IDs** on
  disk. The scene's terrain tiles became "Invalid or missing asset" ("Missing heightmap"). Committed fonts and images
  changed ID (2026-10-14, a read-only texture survey with `Content.GetAssetInfo`/`Content.Load` over `GetFiles`). For a
  survey, use `Editor.Instance.ContentDatabase` items (their `Path`/`ID`) instead. Recovery: kill the editor without
  saving, `git checkout` the committed assets, and rebuild generated content (scenes' SceneData).
- **Never delete and re-create a loaded asset at the same path:** the delete is deferred, and it removed the new file
  ("Cannot remove file … Given ID is invalid", then the file was gone). Rebuild in place instead: load the asset,
  clear its graph, write the new one.
- **Materials from code:** `new MaterialProxy().Create(path, null)`, then a `MaterialSurface` over an
  `IVisjectSurfaceOwner` you implement, nodes by `(group, type)` ids read from `FlaxEditor.Surface.NodeFactory.DefaultGroups`
  (e.g. Material 1/1 output with box 3 Emissive and box 9 Opacity, 1/12 Vertex Color, 4/32 Unpack Float4), `material.SaveSurface`
  with a `MaterialInfo`, then `material.Reload()`. `Box.CreateConnection` returns nothing. Check `CanConnectWith` first.
  `Material.Info` reads the old values until the asset reloads.
- **A material whose shader fails to compile does not load at all:** `Content.Load` returns null ("Cannot load …
  shader cache", "AssetLoadError"). Thus a builder that loads the existing asset to rebuild it fails too. The failure
  stays in the editor's memory. To restore a good file (`git checkout`) is not enough. Restart the editor. Seen
  2026-10-14, when a shader include lost a function that the saved material still called.
- **Terrain splatmaps are sampled per vertex** (point sampling in the vertex shader) and interpolated linearly over each
  triangle. A 0/1 weight per vertex draws its cells as blurred saw-teeth. Store a continuous value (e.g. a margin to a
  threshold) and threshold it per pixel instead.
- Editor API types are in `FlaxEngine.CSharp.dll` in the editor build (there is no separate FlaxEditor assembly to
  reference). The editor's C# sources are not shipped. Read names by reflection.

## Rendering from scripts

- **Dynamic textures:** a virtual `Texture`'s `SetPixels` each second made the panel blink at that period. A
  `GPUTexture` created with only `GPUTextureFlags.ShaderResource` silently ignored `GPUContext.UpdateTexture`. What
  works: a `GPUTexture` with the default `GPUTextureDescription.New2D` flags, updated from
  `MainRenderTask.Instance.Begin`'s `GPUContext`.
- **Dynamic meshes:** use a virtual `Model` (`SetupLODs(new[]{1})`, `SetupMaterialSlots(1)`) on a `StaticModel`.
  `Mesh.UpdateMesh` has `List<>` overloads, so per-frame updates can reuse lists.
- The engine's screenshots hold the 3D render without the UI. Capture the window with PrintWindow. PrintWindow works
  when the window is covered. A screen copy captures whatever covers the window.

## Time and input

- **Flax's physics tick scales its step with the time scale** (at 2x: 50 ticks per second of 0.04 s, at 0: ticks of
  0 s). A simulation that needs Unity's fixed step (more steps of the same length) drives its own clock from
  `Time.DeltaTime` in `OnUpdate`.
- You can send keys without taking the focus. `Input.GetKeyDown` reads a `PostMessage(WM_KEYDOWN/WM_KEYUP)` sent to
  the editor window. Send one key per call (two in one call lost one once).
- The editor log is UTF-16. Decode it (`iconv -f UTF-16LE -t UTF-8`) before grep.
- **The log file stops at about 2 MB** (a long session's log ends mid-day). After that point, native lines, shader
  compile errors included, reach nowhere. `editor_events` keeps only managed lines. Before you hunt a shader error,
  restart the editor for a fresh log (2026-10-19: a post-process "did nothing", and its compile error was in no log).
- **`editor_execute_csharp` can bind an old copy of the game's assembly after reloads:** `Level.FindScript<T>` then
  finds nothing although the scene has the script. Read live state with `editor_get_member` (it resolves the
  instance's own type) or a game command. Restart the editor if the problem persists.
- **Post-process materials have no `LargeWorldsChunkIndex`** (surface materials have one). A large-world absolute
  position needs the eye passed in (ParaNature's `HazeEye` global, metres) plus the view ray to the depth.
- **A material whose shader failed to compile does not load.** A builder that opens the existing asset to update it
  then fails too ("could not be loaded"). Close the editor, delete the asset, reopen the editor, and build.
- **A `GPUTexture` works as a material parameter** (`graph.Parameter<GPUTexture>`, `instance.SetParameterValue`).
  Update it in place through `MainRenderTask.Begin` and `context.UpdateTexture`. It does not blink, unlike a virtual
  `Texture`'s `SetPixels`.
- **The sun's shadow map ends at the light's `ShadowsDistance`** (600 m in the measured project). Effects that must
  reach the horizon (cloud shadows) need their own pass, not shadow casters.

## The fast loop

The FlaxMcp tools carry the whole loop: `editor_compile` (reloaded and settled), `editor_play_mode`, `game_command`
(the game's test driver registers its commands), `editor_wait_for`, `editor_get_member`, `editor_screenshot` or a
window capture, `editor_play_mode stop`. A project script can run several calls in one server process with
`tools/mcp_roundtrip.py`. Give the JSON arguments in files, because the Windows command line changes `\"`.
Measured on a large scene: compile 1.9-2.1 s, play 6.7-7.1 s (the scene load), a game command under 0.1 s, the
first snippet after a reload 0.2 s (1.4 s before the warm compiler). One feature cycle took 11.3 s in 3 calls. A
game's command handler and the bridge's event recording make no garbage per frame. A per-frame file check would make
garbage.
