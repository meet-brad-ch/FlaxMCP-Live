# FlaxMCP

[![CI](https://github.com/meet-brad-ch/FlaxMCP/actions/workflows/ci.yml/badge.svg)](https://github.com/meet-brad-ch/FlaxMCP/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

MCP (Model Context Protocol) server for **[Flax Engine](https://flaxengine.com)** projects, built with **.NET 10 / C#**
on the official [ModelContextProtocol C# SDK](https://www.nuget.org/packages/ModelContextProtocol).

> **Status:** source-only alpha. The server and bridge work from a checkout, but no public package or MCP registry entry
> has been released yet.

This repository is a private working copy of [eXoz00rd/FlaxMCP](https://github.com/eXoz00rd/FlaxMCP) (git remote
`upstream`). Changes are committed to `main` here and are not sent upstream.

## Why

Flax projects combine JSON project and scene files, binary content assets, a native build pipeline, and state that exists
only inside the running editor. FlaxMCP gives an MCP client one interface to all of them:

- offline project, content, and scene inspection;
- headless script compilation, game builds, and structured log diagnostics;
- a local editor plugin for live scenes, selection, transforms, saving, play mode, and viewport screenshots.

C# navigation and refactoring remain the job of an IDE-integrated MCP such as Rider. FlaxMCP focuses on engine operations
and editor state that a conventional code model cannot see.

## Tools

The default configuration exposes 55 tools across ten toolsets. `server` is always enabled; every other toolset can be
selected with `FLAX_TOOLSETS`.

| Toolset | Tools |
|---|---|
| `server` | `server_info`, `flax_status` |
| `project` | `project_info`, `project_targets`, `project_settings` |
| `content` | `content_search`, `content_asset_info`, `content_resolve_guid`, `content_material_details`, `content_create_material`, `content_create_material_instance`, `content_set_material_instance_parameter` |
| `scene` | `scene_list`, `scene_outline`, `scene_find_actor` |
| `build` | `build_check_scripts`, `build_generate_projects`, `build_compile_scripts`, `build_clear_cache`, `build_game` |
| `logs` | `logs_tail`, `logs_errors` |
| `editor` | `editor_scene_graph`, `editor_get_selection`, `editor_set_selection`, `editor_actor_details`, `editor_modify_actor`, `editor_create_actor`, `editor_duplicate_actor`, `editor_rename_actor`, `editor_reparent_actor`, `editor_delete_actor`, `editor_static_model_details`, `editor_set_static_model`, `editor_set_static_model_material`, `editor_box_collider_details`, `editor_create_box_collider`, `editor_set_box_collider`, `editor_save`, `editor_play_mode`, `editor_screenshot`, `editor_compile`, `editor_open_scene`, `editor_host_status`, `editor_find_actors`, `editor_get_member`, `editor_set_member`, `editor_add_script` |
| `events` | `editor_events`, `editor_wait_for` |
| `jobs` | `editor_job_start`, `job_status`, `job_wait` |
| `game` | `game_commands`, `game_command` |

`editor_execute_csharp` and `editor_invoke_method` are additional editor tools that are absent by default. They appear
only when `FLAX_ALLOW_CODE_EXECUTION=true` and read-only mode is disabled.

### Offline inspection

`project_info` reads the `.flaxproj`; `project_targets` parses declarations from `*.Build.cs`; and `project_settings`
returns structured settings assets. The content tools maintain an in-memory index under `Content/`, including GUID and
type metadata read from Flax 1.12 binary asset headers. Unrecognized binary assets remain searchable by path without
guessed metadata.

The scene tools parse `.scene` and `.prefab` files without starting the editor. Actor trees use `ParentID` relationships,
attach scripts to their owners, and report when response limits truncate a result.

### Builds and logs

`build_generate_projects`, `build_compile_scripts`, and `build_clear_cache` run the editor headlessly and wait for the
operation. Script compilation extracts structured diagnostics rather than returning a raw log.

`build_check_scripts` compiles the script modules outside the editor, with the compiler arguments Flax.Build last
wrote (`Cache/Intermediate/<EditorTarget>/<Platform>/<Arch>/<Config>/<Module>.CSharp.response`) and the modules' current
files, through the .NET SDK's compiler server into a scratch folder. Errors come back as SARIF, structured (file, line,
column, code), in well under a second, also while the editor is open or playing. `editor_compile` runs this check
first and only a clean check reaches the editor.

`build_game` can take minutes, so it starts a background job and immediately returns its handle:

1. Call `build_game` with a preset and target, for example `Development` and `Windows`.
2. Call `job_wait` with the returned job ID (it sends progress notifications and returns the state when the job ends or
   the wait times out), or read `job_status`.
3. The ended job's `result` is the build outcome; a failed job carries its `error`.

Long editor operations run as jobs too: `editor_job_start` starts compile, play mode or scene opening and returns at
once; editor jobs run one at a time.

Build tools refuse to start a second editor process while the same project has a live editor session. Close the GUI editor
before using them. `logs_tail` and `logs_errors` only read the newest project log and remain available while it is running.

The `diagnose_build_failure` prompt combines `build_compile_scripts` with `logs_errors` to distinguish compiler failures
from engine or project-configuration errors.

### Live editor bridge

The `editor` tools communicate with the companion plugin over a local named pipe. `flax_status` reports whether the
configured project has a reachable, protocol-compatible session. Live calls fail clearly when the editor or plugin is not
running, or when the editor process that opened the session has exited.

The pipe belongs to the bridge host (`src/FlaxMcpBridge/Host`), which the plugin compiles once per editor process into
the Default load context, so script reloads do not stop it: each reloaded plugin attaches to it again, requests that
arrive meanwhile wait for it, and a request interrupted by a reload is answered with `interrupted_by_reload` (read-only
requests are sent again). A changed host takes the pipe over from the old one. `editor_host_status` reports the host,
the plugin generation, the editor's memory and how many old plugin load contexts are still alive.

The host records the editor's events (managed log lines, compiles, reloads, play mode, scene loads and saves) in a ring
that `editor_events` and `editor_wait_for` read, and compiles `editor_execute_csharp` snippets with a compiler that stays
loaded across reloads. `editor_compile` waits until the editor has reloaded and settled (the editor also compiles changed
sources on its own). Games register named commands with `FlaxMcpBridge.GameCommands` for `game_command`.

Live scene reads include unsaved state. Write tools change selection and transforms, save modified scenes and assets, and
control play mode with `start`, `pause`, `resume`, or `stop`. `editor_screenshot` writes a PNG of the visible scene viewport;
it requires a GUI editor with a rendered viewport and does not work headlessly. Its destination directory must exist.

## Requirements

- .NET 10 SDK
- Flax Engine 1.12.6912 or newer installed locally
- a Flax project (`.flaxproj`)
- the companion plugin installed in that project for live editor tools

## Setup from source

### 1. Get and validate the server

```bash
git clone https://github.com/meet-brad-ch/FlaxMCP.git
cd FlaxMCP
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
    { "Name": "../FlaxMCP/src/FlaxMcpBridge/FlaxMcpBridge.flaxproj" }
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
      "command": "D:/Tools/FlaxMCP/.publish/FlaxMcp.exe",
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

## Roadmap

The source alpha covers offline inspection, build/log tooling, and the live editor bridge. Remaining release work includes
clean installation testing, NuGet publication, MCP registry metadata, and final documentation validation. See the
[project board](https://github.com/users/eXoz00rd/projects/3) for current status.

## Contributing

- **[Contributing Guide](CONTRIBUTING.md)** — branch flow, commits, code style, CI
- **[Task Conventions](CONVENTIONS.md)** — writing issues and definitions of done
- **[Agent Instructions](AGENTS.md)** — single source of truth for AI tools working on this repository

## License

[MIT](LICENSE)
