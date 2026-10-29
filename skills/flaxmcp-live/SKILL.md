---
name: flaxmcp-live
description: Drive a Flax Engine project and its live editor through the FlaxMCP-Live server. Use when working on a Flax project over MCP - inspecting scenes and assets, creating or assigning materials, moving and creating actors, reading or writing a script's members, entering play mode, waiting for editor events, running game commands, capturing editor screenshots, compiling scripts, or diagnosing why a change did not render. Triggers - Flax editor, .flaxproj, .flax asset, FlaxMcpBridge, flax_status, editor_scene_graph, editor_compile, game_command, editor_wait_for, Flax material, Flax play mode.
---

# FlaxMCP-Live Operator Guide

When you drive Flax over MCP, you work in **two worlds**. Offline tools parse project files on disk. Live tools
connect to a running Flax Editor through the `FlaxMcpBridge` plugin. Most mistakes have one of two causes: you confuse
the two worlds, or you trust a success payload and do not examine the result.

## Requirements

The registered `flax` MCP server is the only supported mechanism. If a requirement below is missing, **say
which one and stop**. Do not edit project files by hand to approximate the requirement. Do not drive the server's
stdio protocol yourself.

| Requirement | Needed for | How to check |
|---|---|---|
| `flax` MCP server connected | everything | its tools are in your tool list, or run `claude mcp get flax` |
| Flax Editor GUI open on the bound project, with `FlaxMcpBridge.flaxproj` in that project's `References` | every `editor_*` tool | `flax_status()` → `connected: true` |
| `FLAX_ALLOW_CODE_EXECUTION=true`, `FLAX_READ_ONLY` off | `editor_execute_csharp`, `editor_invoke_method` | the tools are absent if the server does not grant them |
| No GUI editor running | every `build_*` tool | they hold an exclusive per-project session lock |

The server binds **one project** through `FLAX_PROJECT_PATH`. The server reads this variable one time, at startup.
To use a different Flax project, run `claude mcp remove flax -s user`. Then add the server again with the new path.
A plain `add` refuses while the name exists. A newly registered server appears only in a **new** session, or after
`/mcp` reconnects it.

The server is FlaxMCP-Live (https://github.com/meet-brad-ch/FlaxMCP-Live). Register the published
`<checkout>/.publish/FlaxMcp.exe`. `tools/publish.sh` writes it. The running session holds the file. Thus, after a
publish, reconnect with `/mcp`. The server's protocol must be equal to the bridge plugin's protocol. After you change
one of them, publish the server and let the editor reload.

## Files of this skill

Read only the files that the current task needs.

| File | Read it when |
|---|---|
| `references/tools-reference.md` | you need exact parameters. It has all 57 tools, generated from a live `tools/list` |
| `references/workflows.md` | you do one of the six common jobs, or you write an `editor_execute_csharp` snippet |
| `scripts/dump_tools.py` | the server rejects a tool name as unknown. Regenerate the reference with this script |
| `references/flax-gotchas.md` | before real work in a Flax project. It covers compile/reload traps, the bridge host, stale assemblies and memory, scene saves, content import, materials and dynamic textures from code, time and input, the fast loop |

## Quick start

```python
flax_status()          # 1. ALWAYS first. connected:false -> no editor_* tools, stop and say so
project_info()         # 2. orient: targets, references, default scene
editor_scene_graph()   # 3. discover: live actor tree with GUIDs (or content_search for assets)
# 4. act, using IDs copied verbatim from step 3
editor_screenshot(path="C:/existing/dir/shot.png")   # 5. verify -- then Read the PNG
editor_save()          # 6. persist; without this the change is memory-only
```

## Critical rules

### 1. `flax_status` before any `editor_*` call

Offline tools (`project_*`, `scene_*`, `content_search`, `build_*`, `logs_*`) work from files and need no
editor. Every `editor_*` tool needs a running GUI editor with the bridge plugin loaded.

**Why:** `connected: false` means the editor is closed or the project never referenced
`FlaxMcpBridge.flaxproj`. In that state, you cannot know anything about the live scene. If you guess from the
`.scene` file on disk, you silently ignore the user's unsaved work.

### 2. Never construct or convert an ID

There are two incompatible ID encodings:

- **Asset/content GUIDs** (materials, models, textures): 32 lowercase hex chars. This is **not** a .NET `Guid`.
  It is four little-endian `uint32` groups. Source: `content_search`, `content_resolve_guid`, creation results.
- **Actor / scene / script IDs**: standard dashed GUIDs. Source: `editor_scene_graph`, `editor_get_selection`.

Copy the IDs verbatim between calls. Inside `editor_execute_csharp`, load assets **by path**.

**Why:** if you insert dashes into a content GUID by hand, you get a *valid-looking* but wrong identifier. It
resolves to nothing, or worse, to a different asset. Also, address actors by GUID, never by name. Actor names are
not unique.

### 3. Live edits are memory-only until `editor_save`, and there is no undo tool

Changes go through the editor's Undo history, so a human can undo them with Ctrl+Z. But **no MCP tool can undo
them**. Your only rollback is this: do *not* save, and reload the scene.

**Why:** killing the editor discards unsaved changes (observed: an assignment made without saving was gone
after a restart). Conversely, `editor_save` writes the user's whole dirty scene, not only your change. Ask the user
before you call `editor_save` over work that you did not make. Also ask before `editor_delete_actor` or
`build_clear_cache`.

### 4. Examine the screenshot

`editor_screenshot` returns a **path**, not an image. Read the PNG.

**Why:** tools returned `success` with correct-looking colour/roughness payloads, but the cube rendered
solid black. A payload verifies the call, not the pixels. A visual change is not verified until you see it.

### 5. Screenshots need a selected viewport with a live render target

The capture comes from the viewport tab that is **selected**. The Game tab has no render output while play mode
is stopped. A tab that is not selected stops ticking and gives a **stale** frame. Select the Editor viewport tab
(the snippet is in `references/workflows.md`). Let a frame render. Then capture. The output directory must already
exist. The path must end in `.png`.

**Why:** the two failure modes look different. One throws `"Screenshot capture requires a visible scene
viewport with allocated render output."`. The other silently gives you a picture of the past.

### 6. `editor_execute_csharp` is a static method body

Do not use `using` directives, type declarations or `await`. `return` an object. Only `System`,
`System.Collections.Generic`, `System.Linq`, `FlaxEngine`, `FlaxEditor` are in scope. Fully qualify all other
names. You can name game types. The snippet must finish within the 5 s bridge timeout. The full contract is in
`references/workflows.md`.

**Why:** it is the escape hatch for tasks that the typed tools do not cover (selecting a viewport tab, opening an
asset editor). But it runs arbitrary C# with the editor's full permissions. Prefer the typed tools: `editor_find_actors`,
`editor_get_member` / `editor_set_member` (dotted paths, private members), `editor_invoke_method`,
`editor_add_script`. The snippet compiler stays warm across reloads. The result has compileMilliseconds and cacheHit.

### 7. Budget your round-trips

The connect timeout is 1 s. The request timeout is 5 s. The bridge host serves clients concurrently and survives
script reloads. A request during a reload waits for the reloaded plugin. A request that the reload interrupts answers
`interrupted_by_reload`. Read-only requests are retried. Asset loads time out at 3500 ms.

**Why:** discovery loops with many calls stall. Get the scene graph one time and work from it. Long operations wait
until they end. `editor_play_mode` waits up to 35 s, because a heavy scene loads. `editor_compile` waits until the
editor has reloaded **and settled**. The editor also compiles changed sources on its own. `reloads` counts the
reloads seen. Both tools run as jobs. After their deadline, they give the name of the job, so that you can call
`job_wait` for it. To wait for a later event, use `editor_wait_for` (an event kind and text). Do not poll.

### 8. The logs know things the payloads do not

Use `logs_errors()` for warnings and errors. Use `logs_tail(lines=N)` for context. Shader compilation, asset import
and compiler output show **only** in the logs. The engine's native log lines never reach managed code. Thus
`editor_events` holds managed log lines (Debug.Log, a game's `[Test]` lines), exceptions, compiles, reloads, play
mode, and scene loads and saves. It does not hold compiler errors. `editor_compile` reads those from the log file.

**Why:** when a material is created without errors but renders wrong, the log line about its shader compile is the
first real evidence. Both log tools read only the newest log file.

## Tool categories

The parameters are in [tools-reference.md](references/tools-reference.md). Do not guess them.

| Toolset | Key tools | Live editor? |
|---|---|---|
| `server` | `server_info`, `flax_status` | no (but reports bridge state) |
| `project` | `project_info`, `project_targets`, `project_settings` | no |
| `content` | `content_search`, `content_asset_info`, `content_resolve_guid`, `content_material_details` | no |
| `content` (writes) | `content_create_material`, `content_create_material_instance`, `content_set_material_instance_parameter` | **yes** |
| `scene` | `scene_list`, `scene_outline`, `scene_find_actor` | no. They parse `.scene`/`.prefab` on disk |
| `build` | `build_check_scripts` (any time, editor open or not) | no |
| `build` | `build_compile_scripts`, `build_generate_projects`, `build_game` (returns a job), `build_clear_cache` | no. They **refuse while the editor is open** |
| `logs` | `logs_tail`, `logs_errors` | no |
| `editor` (read) | `editor_scene_graph`, `editor_get_selection`, `editor_actor_details`, `editor_static_model_details`, `editor_box_collider_details`, `editor_host_status`, `editor_find_actors`, `editor_get_member` | **yes** |
| `editor` (write) | `editor_create_actor`, `editor_duplicate_actor`, `editor_modify_actor`, `editor_rename_actor`, `editor_reparent_actor`, `editor_delete_actor`, `editor_set_selection`, `editor_set_static_model`, `editor_set_static_model_material`, `editor_create_box_collider`, `editor_set_box_collider`, `editor_save` | **yes** |
| `editor` (session) | `editor_compile`, `editor_open_scene`, `editor_play_mode`, `editor_screenshot`, `editor_execute_csharp` | **yes** |
| `editor` (objects) | `editor_set_member`, `editor_invoke_method`, `editor_add_script` | **yes** |
| `events` | `editor_events`, `editor_wait_for` | **yes** (the bridge host's ring, which also answers during reloads) |
| `jobs` | `editor_job_start`, `job_status`, `job_wait` (progress notifications) | **yes** for editor jobs |
| `game` | `game_commands`, `game_command` (commands the game registered with `FlaxMcpBridge.GameCommands`) | **yes** |

`FLAX_TOOLSETS` can restrict which of these toolsets the server registers. The server always includes `server`.
`FLAX_READ_ONLY` **hides** every mutating tool and gives no error message. If an expected tool is absent, check the
server's environment before you decide that the tool does not exist.

## Common workflows

The full sequences are in [workflows.md](references/workflows.md).

1. **Preflight and orient** — `flax_status` → `project_info` → `scene_list` / `editor_scene_graph`.
2. **Recolour an actor** — `editor_scene_graph` → actor GUID → `content_search` or create → 
   `editor_set_static_model_material` → screenshot → `editor_save`.
3. **Create a material and verify it** — `content_create_material` (path under `Content/`, ends `.flax`,
   values finite and 0–1) → assign → screenshot. Verify the material in the asset editor, **not** through
   `Material.Info`. `Material.Info` misreports the shading model.
4. **Compile scripts with the editor open** — `build_compile_scripts` is locked out. Use `editor_compile`. It first
   checks the scripts outside the editor with `build_check_scripts`. That check uses the compiler arguments that
   Flax.Build last wrote and the current files. It gives structured errors in ~0.3 s. If the check fails,
   `editor_compile` does not touch the editor. Then `editor_compile` stops play mode, compiles, waits through the
   reload, and returns `reloaded`, `unchanged` or `failed`. For only a quick type check, call `build_check_scripts`.
5. **Build a small scene** — `editor_create_actor` (only `EmptyActor` / `StaticModel`) →
   `editor_set_static_model` → `editor_create_box_collider` → `editor_save`.
6. **Diagnose a failure** — `logs_errors` → `logs_tail` → `editor_find_actors` / `editor_get_member`. Use a snippet
   last.
7. **Drive the game in play** — `editor_play_mode start` → `game_commands` → `game_command` (e.g. a test driver's
   `status`) → `editor_wait_for kind=log textContains=...` for what it logs later → `editor_play_mode stop`.

## Error recovery

| Symptom | Cause | Solution |
|---|---|---|
| `flax_status` → `connected: false` | The editor is closed, or the project does not reference the bridge. | Open the editor on the bound project. Check that `FlaxMcpBridge.flaxproj` is in its `References`. |
| `"... already has a live Flax Editor session (pid N)"` | `build_*` needs an exclusive session lock. | Close the GUI editor, or compile in place with `ScriptsBuilder.Compile()`. |
| `"Screenshot capture requires a visible scene viewport with allocated render output."` | The selected tab has no render target (e.g. Game tab, play mode stopped). | Select the Editor viewport. Let a frame render. Do the capture again. |
| Screenshot succeeds but shows the old state | The unselected viewport stopped ticking. | Select the Editor tab first. Then capture. |
| `"Screenshot output directory does not exist"` | The parent folder is missing. | Create the folder. The path must end in `.png`. |
| Expected tool is absent | `FLAX_READ_ONLY` hides mutating tools. `FLAX_TOOLSETS` filters toolsets. | Examine the server env in `claude mcp get flax`. |
| `"protocol mismatch ... requires version N"` | The server and the bridge plugin are built from different revisions. | Publish the server (`tools/publish.sh`, `/mcp`). Let the editor reload the plugin. The editor compiles changed sources on its own. |
| `"did not respond within 5 seconds"` | The snippet or action is too slow. | Make the snippet smaller. Check the state again before you try the call again. |
| `interrupted_by_reload` | Scripts reloaded while a command ran. The effects of the command are unknown. | Read the state (`editor_get_member`, `editor_events`) before you repeat the command. |
| `plugin_not_attached` | No plugin attached within 4 s of the request (the reload failed, or the plugin threw). | Run `logs_errors`. Fix the compile error. Run `editor_compile`. |
| `job_conflict: job-N ...` | Another editor job runs. | Call `job_wait` for job-N. |
| `"... (process N) is not running"` | The editor that opened the session exited. | Start the editor. |
| log: "The bridge host changed: ... takes over" | The host's sources changed. | This is expected. The old host stays loaded until the editor restarts. |
| `"Actor type 'X' is not allowed"` | The `editor_create_actor` allowlist refuses the type. | Use only `EmptyActor` and `StaticModel`. Add components separately. |
| `"A content item already exists at ..."` | You create over an existing asset. | Choose a new path, or delete the existing item first. |
| Unknown tool name | The reference is different from the server. | Regenerate the reference with `scripts/dump_tools.py`. Never improvise a tool name. |

The FlaxMCP-Live `README.md` lists the tools by toolset (55 by default, 57 with code execution). The generated
reference and the live `tools/list` are the authority for parameters.

## Reference files

- **[references/tools-reference.md](references/tools-reference.md)** — all 57 tools with parameter tables.
- **[references/workflows.md](references/workflows.md)** — worked sequences, the `editor_execute_csharp`
  contract, and verified snippets.
- **`scripts/dump_tools.py`** — regenerates the tool reference from a live server.

To *change* FlaxMCP-Live itself, read `CONTRIBUTING.md` and `MAINTAINING.md` in the repository. Use
`dotnet test FlaxMcp.slnx --artifacts-path <scratch>` while the session's server holds `bin/`. Check bridge changes in
the real editor with `tools/mcp_roundtrip.py`. Increase the protocol version on both sides in the same commit. This
skill is about the use of the server.
