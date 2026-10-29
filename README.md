# FlaxMCP-Live

[![CI](https://github.com/meet-brad-ch/FlaxMCP-Live/actions/workflows/ci.yml/badge.svg)](https://github.com/meet-brad-ch/FlaxMCP-Live/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

MCP (Model Context Protocol) server for **[Flax Engine](https://flaxengine.com)** projects, built with **.NET 10 / C#**
on the official [ModelContextProtocol C# SDK](https://www.nuget.org/packages/ModelContextProtocol).
It gives an AI client a complete, unattended loop through the live editor: check, compile, play, drive the game, read the result.

FlaxMCP-Live is an independent fork of [eXoz00rd/FlaxMCP](https://github.com/eXoz00rd/FlaxMCP) by Sebastian Rokicki (MIT).
It keeps all upstream tools and adds 16 more. The code keeps the upstream names (`FlaxMcp` namespaces, the
`FlaxMcpBridge` plugin), so upstream changes can still be merged. See [Staying in sync with upstream](#staying-in-sync-with-upstream).

> **Status:** preview (see [CHANGELOG](CHANGELOG.md)). You install from source. No NuGet package is published yet.

## What you get

| From upstream FlaxMCP | Added in FlaxMCP-Live |
|---|---|
| Offline project, content and scene inspection | A bridge host that survives script reloads |
| Headless script compilation, game builds, structured logs | Long operations as jobs with progress notifications |
| Live editor tools: actors, selection, transforms, colliders, static models, save, play mode, screenshots | Editor events and waiting: no polling, no sleeps |
| Material creation and material instance parameters | Game commands: your game registers named commands, the client calls them |
| An optional gated C# execution tool | Compile checks in under a second, also while the editor plays |
| Read-only mode and toolset selection | Read and write any member of an actor or script; find actors by name, type or script |
| | Large-world engine support (double-precision vectors), Flax 1.13 |

C# navigation and refactoring remain the job of an IDE-integrated MCP such as Rider. FlaxMCP-Live focuses on engine
operations and editor state that a conventional code model cannot see.

## An unattended loop

The headline use case: an AI client changes game code and checks the result in the running game, with no manual step.

```text
build_check_scripts {}                                   # compile errors in under a second, as SARIF
editor_compile {}                                        # reload scripts; returns when the editor has settled
editor_open_scene { "path": "Scenes/Main.scene" }
editor_play_mode { "action": "start" }
editor_wait_for { "kind": "play_begin", "timeoutSeconds": 60 }
game_command { "name": "spawn", "args": "wave=1" }        # a command your game registered
editor_wait_for { "kind": "log", "textContains": "wave cleared", "timeoutSeconds": 300 }
editor_get_member { "actorId": "…", "member": "Score", "scriptType": "PlayerController" }
editor_play_mode { "action": "stop" }
logs_errors {}                                           # anything that went wrong on the way
```

Every step returns when its work is done. A script reload in the middle does not drop the connection.

## Tools

The default configuration exposes 55 tools across ten toolsets. `server` is always enabled; every other toolset can be
selected with `FLAX_TOOLSETS`. Tools added in FlaxMCP-Live are in **bold**.

| Toolset | Tools |
|---|---|
| `server` | `server_info`, `flax_status` |
| `project` | `project_info`, `project_targets`, `project_settings` |
| `content` | `content_search`, `content_asset_info`, `content_resolve_guid`, `content_material_details`, `content_create_material`, `content_create_material_instance`, `content_set_material_instance_parameter` |
| `scene` | `scene_list`, `scene_outline`, `scene_find_actor` |
| `build` | **`build_check_scripts`**, `build_generate_projects`, `build_compile_scripts`, `build_clear_cache`, `build_game` |
| `logs` | `logs_tail`, `logs_errors` |
| `editor` | `editor_scene_graph`, `editor_get_selection`, `editor_set_selection`, `editor_actor_details`, `editor_modify_actor`, `editor_create_actor`, `editor_duplicate_actor`, `editor_rename_actor`, `editor_reparent_actor`, `editor_delete_actor`, `editor_static_model_details`, `editor_set_static_model`, `editor_set_static_model_material`, `editor_box_collider_details`, `editor_create_box_collider`, `editor_set_box_collider`, `editor_save`, `editor_play_mode`, `editor_screenshot`, **`editor_compile`**, **`editor_open_scene`**, **`editor_host_status`**, **`editor_find_actors`**, **`editor_get_member`**, **`editor_set_member`**, **`editor_add_script`** |
| `events` | **`editor_events`**, **`editor_wait_for`** |
| `jobs` | **`editor_job_start`**, **`job_status`**, **`job_wait`** |
| `game` | **`game_commands`**, **`game_command`** |

`editor_execute_csharp` and **`editor_invoke_method`** are additional editor tools that are absent by default. They appear
only when `FLAX_ALLOW_CODE_EXECUTION=true` and read-only mode is disabled.

Upstream polls a build with `build_status` and `build_result`. FlaxMCP-Live replaces that pair with the job tools, which
also cover compile, play mode and scene opening.

### Offline inspection

`project_info` reads the `.flaxproj`; `project_targets` parses declarations from `*.Build.cs`; and `project_settings`
returns structured settings assets. The content tools maintain an in-memory index under `Content/`, including GUID and
type metadata read from binary asset headers (Storage format version 9, Flax 1.12 and 1.13). Unrecognized binary assets
remain searchable by path without guessed metadata.

The scene tools parse `.scene` and `.prefab` files without starting the editor. Actor trees use `ParentID` relationships,
attach scripts to their owners, and report when response limits truncate a result.

### Builds, checks and logs

`build_generate_projects`, `build_compile_scripts`, and `build_clear_cache` run the editor headlessly and wait for the
operation. Script compilation extracts structured diagnostics rather than returning a raw log.

**`build_check_scripts`** compiles the script modules outside the editor, with the compiler arguments Flax.Build last
wrote (`Cache/Intermediate/<EditorTarget>/<Platform>/<Arch>/<Config>/<Module>.CSharp.response`) and the modules' current
files, through the .NET SDK's compiler server into a scratch folder. Errors come back as SARIF, structured (file, line,
column, code), in well under a second, also while the editor is open or playing. `editor_compile` runs this check
first and only a clean check reaches the editor.

Build tools refuse to start a second editor process while the same project has a live editor session. Close the GUI editor
before using them. `logs_tail` and `logs_errors` only read the newest project log and remain available while it is running.

The `diagnose_build_failure` prompt combines `build_compile_scripts` with `logs_errors` to distinguish compiler failures
from engine or project-configuration errors.

### Jobs

`build_game` can take minutes, so it starts a background job and immediately returns its handle:

1. Call `build_game` with a preset and target, for example `Development` and `Windows`.
2. Call **`job_wait`** with the returned job ID. It sends progress notifications and returns the state when the job ends or
   the wait times out. **`job_status`** reads the state without waiting.
3. The ended job's `result` is the build outcome; a failed job carries its `error`.

**`editor_job_start`** runs long editor operations the same way: `compile`, `play_start`, `play_stop`, `play_pause`,
`play_resume` or `open_scene`. It returns at once. Editor jobs run one at a time.

### Live editor bridge

The `editor` tools communicate with the companion plugin over a local named pipe. `flax_status` reports whether the
configured project has a reachable, protocol-compatible session. Live calls fail clearly when the editor or plugin is not
running, or when the editor process that opened the session has exited.

Live scene reads include unsaved state. Write tools change selection and transforms, save modified scenes and assets, and
control play mode with `start`, `pause`, `resume`, or `stop`. `editor_screenshot` writes a PNG of the visible scene viewport;
it requires a GUI editor with a rendered viewport and does not work headlessly. Its destination directory must exist.

#### It survives script reloads

Every script change makes the editor reload its plugins, and a plain plugin would lose its pipe at that moment. In
FlaxMCP-Live the pipe belongs to a bridge host (`src/FlaxMcpBridge/Host`). The plugin compiles the host once per editor
process into the Default load context, so reloads do not stop it. Each reloaded plugin attaches to it again. Requests that
arrive during a reload wait for it. A request that a reload interrupts is answered with `interrupted_by_reload`, and
read-only requests are sent again. A changed host takes the pipe over from the old one.

**`editor_host_status`** reports the host, the plugin generation, the editor's memory and how many old plugin load
contexts are still alive. It is the first tool to call when memory grows over many reloads.

#### Events and waiting

The host records the editor's events in a ring: managed log lines, compiles, reloads, play mode changes, scene loads and
saves. **`editor_events`** reads them after a sequence number. **`editor_wait_for`** blocks until an event of a given kind
(for example `play_begin`, `reload_end` or `log`) arrives, optionally with matching text. A client waits for what it
needs and does not sleep or poll. **`editor_compile`** waits until the editor has reloaded and settled; the editor also
compiles changed sources on its own.

#### Game commands

A game registers named commands. A client lists them with **`game_commands`** and calls them with **`game_command`**. The
registration needs the `FlaxMcpBridge` module as a dependency of the game module
(`options.PrivateDependencies.Add("FlaxMcpBridge")` in the `.Build.cs` file).

```csharp
using FlaxMcpBridge;

var registration = GameCommands.Register(
    "spawn", "Spawns a wave. Argument: wave=<n>.", arg => SpawnWave(arg));
// Dispose the registration when the script is destroyed.
```

The handler gets the argument string and returns the result text. The tool names the command's owner by its declaring type.

#### Members, actors and snippets

**`editor_find_actors`** finds actors by name, type or script type. **`editor_get_member`** and **`editor_set_member`**
read and write any member path of an actor or of one of its scripts, for example `Settings.Speed`. **`editor_add_script`**
attaches a script and **`editor_open_scene`** opens a scene. Tool results are checked against their output schemas.

`editor_execute_csharp` compiles a snippet in the bridge host, with a compiler that stays loaded across reloads, and runs
it on the editor main thread. **`editor_invoke_method`** calls one method by name. Both need `FLAX_ALLOW_CODE_EXECUTION`.

#### Large worlds and Flax 1.13

Actor translations and vectors are read as double precision, and the bridge builds against engines with large worlds
enabled. The minimum engine version is 1.13.7012.

## Requirements

- .NET 10 SDK
- Flax Engine 1.13.7012 or newer installed locally
- a Flax project (`.flaxproj`)
- the companion plugin installed in that project for live editor tools

## Setup from source

### 1. Get and validate the server

```bash
git clone https://github.com/meet-brad-ch/FlaxMCP-Live.git
cd FlaxMCP-Live
dotnet build FlaxMcp.slnx
dotnet test FlaxMcp.slnx
dotnet format --verify-no-changes
```

### 2. Install the editor bridge

Add the bridge project to the consumer project's `References` array, using an absolute path or one relative to the
consumer `.flaxproj`:

```json
{
  "References": [
    { "Name": "$(EnginePath)/Flax.flaxproj" },
    { "Name": "../FlaxMCP-Live/src/FlaxMcpBridge/FlaxMcpBridge.flaxproj" }
  ]
}
```

Keep existing references and adjust only the bridge path. Flax discovers the `EditorPlugin` from this reference; no
consumer-side `PrivateDependencies` or `Modules.Add` entry is required. Start or reload the GUI editor and call
`flax_status` to confirm `connected: true`.

### 3. Configure an MCP client

Publish the server with `tools/publish.sh` (it writes `.publish/FlaxMcp.exe`, which starts in under a second and does
not lock `bin/` while you build) and use the executable as the process target. `dotnet run --project src/FlaxMcp` works
too. This JSON shape works for clients that accept an `mcpServers` map, including Claude Code configuration files:

```json
{
  "mcpServers": {
    "flax": {
      "command": "D:/Tools/FlaxMCP-Live/.publish/FlaxMcp.exe",
      "env": {
        "FLAX_PROJECT_PATH": "D:/Projects/YourFlaxProject"
      }
    }
  }
}
```

In Rider's MCP server settings, create a stdio server with the same command and environment variables.
`tools/mcp_roundtrip.py` runs tool calls against a server build over stdio from the command line. Prefer absolute paths because GUI clients do not necessarily inherit the shell working directory.

Call `server_info`, `project_info`, and `flax_status` as a basic verification.

## Configuration

| Variable | Required | Description |
|---|---|---|
| `FLAX_PROJECT_PATH` | yes | Path to a `.flaxproj`, or a directory containing exactly one top-level `.flaxproj` |
| `FLAX_ENGINE_PATH` | no | Flax Engine root. When omitted, uses the Flax Launcher's `%APPDATA%/Flax/Versions.txt` registrations |
| `FLAX_EDITOR_CONFIG` | no | Editor binary configuration: `Development` (default), `Debug`, or `Release` |
| `FLAX_TOOLSETS` | no | Comma-separated selection from `project`, `content`, `scene`, `build`, `logs`, `editor`, `events`, `jobs`, and `game`; `server` is always included. Omit for all toolsets |
| `FLAX_READ_ONLY` | no | `true`, `1`, or `yes` removes all tools that can alter files or editor state |
| `FLAX_BRIDGE` | no | Bridge mode placeholder; defaults to `auto`. It is parsed but does not currently change connection behavior |
| `FLAX_ALLOW_CODE_EXECUTION` | no | Enables `editor_execute_csharp` and `editor_invoke_method` only when true and `FLAX_READ_ONLY` is false |
| `FLAX_LOG_LEVEL` | no | Minimum server log level written to stderr; defaults to `Warning` |

Unknown toolset names fail at startup and list valid values. With `FLAX_READ_ONLY=true`, the remaining tools are
`server_info`, `flax_status`, the project and scene tools, `build_check_scripts`, `content_search`, `content_asset_info`,
`content_resolve_guid`, `content_material_details`, both log tools, `editor_scene_graph`, `editor_get_selection`,
`editor_actor_details`, `editor_static_model_details`, `editor_box_collider_details`, `editor_host_status`,
`editor_find_actors`, `editor_get_member`, `editor_events`, `editor_wait_for`, `job_status`, `job_wait`, and
`game_commands`.

## Security

- The bridge accepts local named-pipe connections and records discovery data under `%APPDATA%/FlaxMcp/sessions`; it does
  not require network credentials.
- Build and editor write tools run with the current user's filesystem and process permissions. Use
  `FLAX_READ_ONLY=true` for inspection-only clients.
- `editor_execute_csharp` compiles and runs arbitrary C# on the editor main thread with the Flax Editor process's full
  machine permissions, including access to files, processes, credentials, and the network. Enable it only for trusted
  clients and prompts.

## Troubleshooting

### `flax_status` reports `connected: false`

Confirm the configured project matches the project open in Flax, the bridge reference is present in its `.flaxproj`, and
the editor has finished compiling scripts. Reload scripts or restart the editor after adding the plugin. Check the latest
project log for `[FlaxMcpBridge] Pipe server started` or a plugin compilation error.

### Protocol mismatch

The server and editor plugin came from incompatible revisions. Update both from the same checkout, let Flax recompile the
plugin, and reload scripts. A mismatch is rejected explicitly rather than sent through an incompatible protocol.

### A build says the editor is already running

Headless build operations intentionally refuse to race a GUI editor using the same project and `Cache/` directory. Close
that editor session normally, then retry. Log-reading and live-editor tools do not require this step.

### Screenshot capture fails

Open a scene in the GUI editor and ensure its scene viewport is visible. Capture requires a rendered viewport, cannot run
headlessly, only accepts a `.png` path, and does not create the destination directory.

## Staying in sync with upstream

```bash
git fetch upstream --no-tags
git switch -c sync-upstream main
git merge upstream/main
```

Expect conflicts only where both sides changed the same code. See [CONTRIBUTING](CONTRIBUTING.md#staying-in-sync-with-upstream).

## Versions

FlaxMCP-Live uses [Semantic Versioning](https://semver.org). The [CHANGELOG](CHANGELOG.md) lists every release.
The server and the bridge plugin must come from the same release.

## Contributing

- **[Contributing Guide](CONTRIBUTING.md)** — branch flow, commits, code style, CI, releases
- **[Task Conventions](CONVENTIONS.md)** — writing issues and definitions of done
- **[Agent Instructions](AGENTS.md)** — single source of truth for AI tools working on this repository

## License

[MIT](LICENSE). Copyright (c) 2026 Sebastian Rokicki and Brad Chamberlain.
