# FlaxMCP Tool Reference

Generated from a live `tools/list` on FlaxMcp 0.2.0.0 (57 tools).
`editor_execute_csharp` is only present when `FLAX_ALLOW_CODE_EXECUTION=true`.

Regenerate with `scripts/dump_tools.py` after changing the server.

## Server

### `server_info`

Returns the FlaxMcp server name and version.

_No parameters._

### `flax_status`

Reports whether the configured project has a reachable Flax Editor bridge and returns its session metadata.

_No parameters._

## Project

### `project_info`

Reads the .flaxproj file: name, version, targets, references, and default scene.

_No parameters._

### `project_targets`

Parses the GameTarget/EditorTarget *.Build.cs files referenced from the .flaxproj: target class names and referenced modules.

_No parameters._

### `project_settings`

Reads Content/*.json and Content/Settings/*.json settings assets (Game, Graphics, Input, Physics, ...) as structured JSON.

_No parameters._

## Content

### `content_search`

Searches the Content/ index by partial path match, TypeName, and/or extension. Id/TypeName are null for files whose format isn't recognized or a .flax header that doesn't match the reverse-engineered layout (verified against Flax 1.12 and 1.13).

| Param | Type | Req | Notes |
|---|---|---|---|
| `query` | string? | - |  |
| `typeName` | string? | - |  |
| `extension` | string? | - |  |

### `content_asset_info`

Returns metadata (Id, TypeName, RelativePath, Extension) for the content asset with the given GUID.

| Param | Type | Req | Notes |
|---|---|---|---|
| `id` | string | yes |  |

### `content_resolve_guid`

Resolves a content asset GUID to its path relative to Content/.

| Param | Type | Req | Notes |
|---|---|---|---|
| `id` | string | yes |  |

### `content_material_details`

Returns typed values, texture references, UV tiling, and exposed parameters for a live surface Material asset.

| Param | Type | Req | Notes |
|---|---|---|---|
| `materialId` | string | yes | FlaxEngine.Material asset GUID from content_search. |

### `content_create_material`

Creates a surface Material asset through the live Flax Editor. Paths are relative to Content/ and must end in .flax.

| Param | Type | Req | Notes |
|---|---|---|---|
| `relativePath` | string | yes | Destination path relative to Content/, ending in .flax. |
| `baseColor` | object{r, g, b, a} | yes | Uniform RGBA base color, with every component in the 0-1 range. |
| `roughness` | number | yes | Surface roughness in the 0-1 range. |
| `metallic` | number | yes | Surface metallic value in the 0-1 range. |
| `emissiveColor` | object? | - | Optional emissive RGBA color, with every component in the 0-1 range. |
| `baseColorTextureId` | string? | - | Optional FlaxEngine.Texture GUID used for base color. |
| `normalTextureId` | string? | - | Optional normal-map FlaxEngine.Texture GUID. |
| `uvTiling` | object? | - | Optional positive UV tiling applied to supplied textures. |

### `content_create_material_instance`

Creates a MaterialInstance asset from a live surface material and applies validated parameter overrides atomically.

| Param | Type | Req | Notes |
|---|---|---|---|
| `relativePath` | string | yes | Destination path relative to Content/, ending in .flax. |
| `baseMaterialId` | string | yes | Base FlaxEngine.Material GUID from content_search. |
| `parameters` | object | yes | Parameter overrides keyed by public parameter name. |

### `content_set_material_instance_parameter`

Sets one validated parameter override on an existing MaterialInstance and saves the asset.

| Param | Type | Req | Notes |
|---|---|---|---|
| `materialInstanceId` | string | yes | FlaxEngine.MaterialInstance asset GUID from content_search. |
| `parameterName` | string | yes | Existing public parameter name on the base material. |
| `value` | any | yes | Value matching the parameter's declared type. |

## Scene (offline file parsing)

### `scene_list`

Lists scenes (.scene) and prefabs (.prefab) in the project's Content/ directory.

_No parameters._

### `scene_outline`

Reads a .scene/.prefab file's actor tree, built from ParentID linkage (not file order). Scripts attached to an actor are listed on that actor's Scripts, not as child nodes. Truncated is true if the configured depth/node-count limit was hit.

| Param | Type | Req | Notes |
|---|---|---|---|
| `path` | string | yes |  |

### `scene_find_actor`

Searches a .scene/.prefab file's actors by partial Name match and/or exact TypeName.

| Param | Type | Req | Notes |
|---|---|---|---|
| `path` | string | yes |  |
| `name` | string? | - |  |
| `typeName` | string? | - |  |

## Build (headless - refuses to run while the GUI editor is open; build_check_scripts works any time)

### `build_check_scripts`

Compiles the project's script modules (and those of the projects it references) outside the editor, with the compiler arguments Flax.Build last used and the modules' current files, into a scratch folder. Returns passed, failed or unavailable (never built yet: Reason says why) with structured errors and warnings (file, line, column, code) and each module's outcome. Works while the editor is open, also during play mode; changes nothing in the project.

_No parameters._

### `build_compile_scripts`

Compiles game scripts headlessly. Returns structured compiler errors (file/line/code/message) parsed from the engine log, not a raw log dump.

_No parameters._

### `build_generate_projects`

Runs FlaxEditor.exe -genprojectfiles to regenerate IDE project/solution files.

_No parameters._

### `build_game`

Starts a game build (-build <preset>.<target>, e.g. preset 'Development', target 'Windows') in the background and returns its job at once -- a real build can take minutes. Follow it with job_status or job_wait; the job's result is the build outcome.

| Param | Type | Req | Notes |
|---|---|---|---|
| `preset` | string | yes |  |
| `target` | string | yes |  |

### `build_clear_cache`

Runs FlaxEditor.exe -clearcache, and optionally -clearcooker too, to clear build caches.

| Param | Type | Req | Notes |
|---|---|---|---|
| `alsoClearCooker` | boolean | - |  (default: false) |

## Logs

### `logs_tail`

Returns the last N lines (default 200) of the project's most recent Logs/*.txt file, without running the editor.

| Param | Type | Req | Notes |
|---|---|---|---|
| `lines` | integer | - |  (default: 200) |

### `logs_errors`

Extracts lines tagged [Error] or [Warning] from the project's most recent Logs/*.txt file, without running the editor.

_No parameters._

## Editor - inspection (live)

### `editor_scene_graph`

Returns the live actor tree from every scene loaded in the Flax Editor, including unsaved runtime state. Truncated is true when the 500-node or 32-level response limit is reached.

_No parameters._

### `editor_get_selection`

Returns the actors currently selected in the live Flax Editor.

_No parameters._

### `editor_actor_details`

Returns live identity, hierarchy, activation, layer, tags, world/local transforms, and scripts for one actor loaded in the Flax Editor.

| Param | Type | Req | Notes |
|---|---|---|---|
| `actorId` | string | yes | Actor GUID from editor_scene_graph or editor_get_selection. |

### `editor_static_model_details`

Returns the assigned model reference and live actor details for a loaded StaticModel actor.

| Param | Type | Req | Notes |
|---|---|---|---|
| `actorId` | string | yes | StaticModel actor GUID from editor_scene_graph. |

### `editor_box_collider_details`

Returns the local size, center, trigger mode, and actor details for a loaded BoxCollider.

| Param | Type | Req | Notes |
|---|---|---|---|
| `actorId` | string | yes | BoxCollider actor GUID from editor_scene_graph. |

### `editor_host_status`

Returns the state of the bridge host, the pipe server that outlives script reloads: its version and start time, the attached plugin generation, attach and detach counts, the editor's memory and garbage collections, and how many old plugin load contexts are still alive (reloads that leak). collect=true runs a full garbage collection first so that count is a measurement; it takes long on a large heap.

| Param | Type | Req | Notes |
|---|---|---|---|
| `collect` | boolean | - | Run a full garbage collection before counting the old plugin load contexts. (default: false) |

## Editor - mutation (live)

### `editor_set_selection`

Replaces the live Flax Editor actor selection with the actors identified by GUID. Pass an empty list to clear the selection.

| Param | Type | Req | Notes |
|---|---|---|---|
| `actorIds` | array<string> | yes | Actor GUIDs from editor_scene_graph. |

### `editor_create_actor`

Creates an allowlisted EmptyActor or StaticModel in a loaded scene and returns its live details.

| Param | Type | Req | Notes |
|---|---|---|---|
| `actorType` | string | yes | Allowlisted actor type: EmptyActor or StaticModel. |
| `name` | string | yes |  |
| `sceneId` | string? | yes |  |
| `parentId` | string? | yes |  |
| `translation` | object{x, y, z} | yes |  |
| `orientation` | object{x, y, z, w} | yes |  |
| `scale` | object{x, y, z} | yes |  |

### `editor_duplicate_actor`

Duplicates a loaded actor into a loaded scene or parent and returns the duplicate's live details.

| Param | Type | Req | Notes |
|---|---|---|---|
| `actorId` | string | yes |  |
| `name` | string | yes |  |
| `sceneId` | string? | yes |  |
| `parentId` | string? | yes |  |
| `translation` | object{x, y, z} | yes |  |
| `orientation` | object{x, y, z, w} | yes |  |
| `scale` | object{x, y, z} | yes |  |

### `editor_modify_actor`

Replaces an existing live actor's world transform and returns its updated details. Use editor_actor_details first to preserve transform values that should not change.

| Param | Type | Req | Notes |
|---|---|---|---|
| `actorId` | string | yes | Actor GUID from editor_scene_graph or editor_get_selection. |
| `translation` | object{x, y, z} | yes | Replacement world-space translation. |
| `orientation` | object{x, y, z, w} | yes | Replacement world-space orientation quaternion. |
| `scale` | object{x, y, z} | yes | Replacement world-space scale. |

### `editor_rename_actor`

Renames a loaded actor through the editor Undo/Redo history and returns its updated details.

| Param | Type | Req | Notes |
|---|---|---|---|
| `actorId` | string | yes | Actor GUID from editor_scene_graph. |
| `name` | string | yes | New non-empty actor name. |

### `editor_reparent_actor`

Moves a loaded actor to another loaded parent or to a loaded scene root through Undo/Redo history. Set exactly one of sceneId or parentId and explicitly choose whether to preserve the world transform.

| Param | Type | Req | Notes |
|---|---|---|---|
| `actorId` | string | yes | Actor GUID from editor_scene_graph. |
| `sceneId` | string? | yes |  |
| `parentId` | string? | yes |  |
| `preserveWorldTransform` | boolean | yes |  |

### `editor_delete_actor`

Deletes a loaded actor through the editor Undo/Redo history. Set deleteDescendants to true to explicitly delete its complete hierarchy; false rejects actors with children.

| Param | Type | Req | Notes |
|---|---|---|---|
| `actorId` | string | yes | Actor GUID from editor_scene_graph. |
| `deleteDescendants` | boolean | yes |  |

### `editor_set_static_model`

Assigns an existing FlaxEngine.Model asset to a loaded StaticModel actor through Undo/Redo history. The bridge validates that modelId resolves to a compatible model before changing the scene.

| Param | Type | Req | Notes |
|---|---|---|---|
| `actorId` | string | yes | StaticModel actor GUID from editor_scene_graph. |
| `modelId` | string | yes | FlaxEngine.Model asset GUID from content_search. |

### `editor_set_static_model_material`

Assigns an existing FlaxEngine.MaterialBase asset to one slot of a loaded StaticModel through Undo/Redo history. The bridge validates the actor, slot, and material before changing the scene.

| Param | Type | Req | Notes |
|---|---|---|---|
| `actorId` | string | yes | StaticModel actor GUID from editor_scene_graph. |
| `slotIndex` | integer | yes | Zero-based material slot index from the assigned model. |
| `materialId` | string | yes | FlaxEngine.Material or MaterialInstance asset GUID from content_search. |

### `editor_create_box_collider`

Creates a BoxCollider under a loaded parent actor and returns its effective properties.

| Param | Type | Req | Notes |
|---|---|---|---|
| `parentId` | string | yes | Loaded parent actor GUID from editor_scene_graph. |
| `name` | string | yes |  |
| `size` | object{x, y, z} | yes | Positive collider dimensions in local space. |
| `center` | object{x, y, z} | yes | Collider center offset in local space. |
| `isTrigger` | boolean | yes |  |

### `editor_set_box_collider`

Updates a loaded BoxCollider's local size, center, and trigger mode through Undo/Redo history.

| Param | Type | Req | Notes |
|---|---|---|---|
| `actorId` | string | yes | BoxCollider actor GUID from editor_scene_graph. |
| `size` | object{x, y, z} | yes | Positive collider dimensions in local space. |
| `center` | object{x, y, z} | yes | Collider center offset in local space. |
| `isTrigger` | boolean | yes |  |

### `editor_save`

Saves every open scene (including changes made through the API, which the editor does not mark as modified) and all modified content assets in the live Flax Editor. Refused during play mode: a save then would write the play-time scene, with every actor scripts created at run time.

_No parameters._

## Editor - session (live)

### `editor_compile`

Compiles the project's scripts and returns when the editor has reloaded and settled. The scripts are first compiled outside the editor with the compiler arguments Flax.Build last used (build_check_scripts): errors come back at once, structured (file, line, column, code), and the editor is not touched. A clean check goes on to the editor's compile; its outcome is reloaded, unchanged or failed (build tool lines in BuildErrors). Check says how the check went (passed, failed, or unavailable with the reason). Stops play mode first.

_No parameters._

### `editor_open_scene`

Opens a scene in the live Flax Editor and returns when it has loaded. The path is absolute or relative to the project's Content folder (for example Scenes/Main.scene). Refused during play mode.

| Param | Type | Req | Notes |
|---|---|---|---|
| `path` | string | yes | Scene asset path, absolute or relative to Content/, ending in .scene. |

### `editor_play_mode`

Controls play mode in the live Flax Editor. Action must be start, stop, pause, or resume. Returns when the editor has reached the requested state (up to 30 seconds, so a heavy scene can load).

| Param | Type | Req | Notes |
|---|---|---|---|
| `action` | string | yes | Play mode action: start, stop, pause, or resume. |

### `editor_screenshot`

Captures the visible Flax Editor scene viewport to a PNG file. The output directory must already exist, and capture is unavailable in headless mode.

| Param | Type | Req | Notes |
|---|---|---|---|
| `path` | string | yes | Absolute or current-working-directory-relative output path ending in .png. |

### `editor_execute_csharp`

DANGER: Compiles and executes arbitrary C# with the Flax Editor process's full machine permissions. The code is the body of a static method, runs on the editor main thread, and must return a JSON-serializable value.

| Param | Type | Req | Notes |
|---|---|---|---|
| `code` | string | yes | C# method body, for example: return FlaxEngine.Globals.EngineBuildNumber; |

## Editor - live objects (typed, instead of snippets)

### `editor_find_actors`

Finds actors in the loaded scenes (the play-time scene during play) by name, actor type and script type, all optional and combined. Types match by full or short name. Returns id, name, type, path from the scene and the scripts' types; Truncated is true when more than the limit matched.

| Param | Type | Req | Notes |
|---|---|---|---|
| `nameContains` | string? | - | Part of the actor's name, case-insensitive. |
| `actorType` | string? | - | The actor's type, e.g. StaticModel or FlaxEngine.Camera. |
| `scriptType` | string? | - | A script type the actor has, e.g. PlayerController. |
| `limit` | integer | - | At most this many, 1 to 500. (default: 50) |

### `editor_get_member`

Reads a field or property of an actor, or of one of its scripts, by a dotted path (e.g. Clock.Steps). Private members count. Unknown names list the members that exist.

| Param | Type | Req | Notes |
|---|---|---|---|
| `actorId` | string | yes | The actor's id (editor_find_actors, editor_scene_graph). |
| `member` | string | yes | The member path, e.g. Score or Clock.Steps. |
| `scriptType` | string? | - | Read from the actor's script of this type instead of the actor. |

### `editor_set_member`

Writes a field or property of an actor or of one of its scripts by a dotted path; a struct on the way is written back. The value is JSON in the member's shape (a number, true, "text", an enum name, {"X":1,"Y":2,"Z":3}). In edit mode it goes through the editor's undo and marks the scene edited (editor_save keeps it); in play mode it lasts until play ends. Returns the value read back.

| Param | Type | Req | Notes |
|---|---|---|---|
| `actorId` | string | yes | The actor's id. |
| `member` | string | yes | The member path, e.g. Enabled or Settings.Speed. |
| `value` | any | yes | The new value as JSON. |
| `scriptType` | string? | - | Write to the actor's script of this type instead of the actor. |

### `editor_invoke_method`

Calls a method on the editor's main thread: an instance method of an actor or of one of its scripts (actorId, scriptType), or a static method of a type (typeName). Arguments are a JSON array converted to the parameter types; the overload with that many parameters that accepts them is called. Runs any code the method runs, with the editor's permissions: present only when code execution is allowed.

| Param | Type | Req | Notes |
|---|---|---|---|
| `method` | string | yes | The method's name. |
| `args` | any | - | The arguments as a JSON array, e.g. [] or [2, "fast"]. |
| `actorId` | string? | - | The actor whose (or whose script's) instance method to call. |
| `scriptType` | string? | - | Call the method on the actor's script of this type. |
| `typeName` | string? | - | For a static method: the type's full or short name. |

### `editor_add_script`

Adds a script of a type to an actor (nothing when it has one already) and, in edit mode, marks the scene edited so editor_save keeps it. The type is resolved by full or short name in the loaded assemblies.

| Param | Type | Req | Notes |
|---|---|---|---|
| `actorId` | string | yes | The actor's id. |
| `scriptType` | string | yes | The script type, e.g. Game.Testing.TestDriver. |
| `orderInParent` | integer? | - | The script's place among the actor's scripts. |

## Events (the editor's recorded events)

### `editor_events`

Returns the editor's recorded events after a sequence number, oldest first. Kinds: log (managed log lines only; the engine's native lines such as compiler output stay in the log file), exception, compile_begin, compile_succeeded, compile_failed, reload_begin, reload_end, play_begin, play_end, scene_loaded, scene_load_error, scene_saved, scene_save_error, scene_unloaded. The bridge host keeps the last 4096 events across script reloads; Truncated is true when some after sinceSeq were overwritten. A new HostStartedUtc means the editor restarted and the sequence began again.

| Param | Type | Req | Notes |
|---|---|---|---|
| `sinceSeq` | integer | - | Only events after this sequence number (LastSeq of an earlier call); 0 for all held. (default: 0) |
| `kinds` | array? | - | Only these kinds; omit for all. |
| `limit` | integer | - | At most this many events, 1 to 500. (default: 100) |
| `waitSeconds` | number | - | When none match yet, wait up to this long for one, 0 to 2 seconds. (default: 0) |

### `editor_wait_for`

Waits for the first editor event of a kind (see editor_events), optionally with a text, and returns it. Without sinceSeq it waits for an event after the call; with it, an event already recorded after that number counts. A timeout is a result (TimedOut true), not an error. Example: start play with editor_play_mode, then editor_wait_for kind=log textContains="[Test]" to read a game's answer.

| Param | Type | Req | Notes |
|---|---|---|---|
| `kind` | string | yes | The event kind, e.g. play_begin, reload_end, log. |
| `textContains` | string? | - | Only an event whose text contains this (ordinal). |
| `sinceSeq` | integer? | - | Count events after this sequence number; omit to wait for a new one. |
| `timeoutSeconds` | number | - | How long to wait, up to 600 seconds. (default: 30) |

## Jobs (long operations)

### `editor_job_start`

Starts a long editor operation as a job and returns at once: compile (compile scripts and wait until the editor has reloaded and settled), play_start, play_stop, play_pause, play_resume, open_scene (path). Editor jobs run one at a time: another one while one runs fails with job_conflict naming it. Follow the job with job_wait or job_status; its result is what editor_compile, editor_play_mode or editor_open_scene return.

| Param | Type | Req | Notes |
|---|---|---|---|
| `kind` | string | yes | compile, play_start, play_stop, play_pause, play_resume or open_scene. |
| `path` | string? | - | For open_scene: the scene path, absolute or relative to Content/. |

### `job_status`

Returns a job's state: running, succeeded (with its result), failed or canceled (with the reason), and its latest progress message. Jobs come from editor_job_start, build_game, and the editor tools that run as jobs.

| Param | Type | Req | Notes |
|---|---|---|---|
| `jobId` | string | yes | The job id, e.g. job-7. |

### `job_wait`

Waits for a job to end, up to the timeout, sending its progress messages as progress notifications, and returns its state. A timeout is a result (Status running), not an error.

| Param | Type | Req | Notes |
|---|---|---|---|
| `jobId` | string | yes | The job id, e.g. job-7. |
| `timeoutSeconds` | number | - | How long to wait, up to 1800 seconds. (default: 60) |

## Game commands (registered by the game)

### `game_commands`

Lists the commands the game registered with the bridge (FlaxMcpBridge.GameCommands.Register): name, description and the type that registered it. Game scripts usually register while enabled, so the list in play mode can differ from the one in edit mode.

_No parameters._

### `game_command`

Runs a registered game command on the editor's main thread and returns its text result. Errors: unknown_command (lists the registered names), command_failed (the handler's exception type and message).

| Param | Type | Req | Notes |
|---|---|---|---|
| `name` | string | yes | The command name, as game_commands lists it. |
| `args` | string | - | The argument string the command's description asks for. (default: "") |
