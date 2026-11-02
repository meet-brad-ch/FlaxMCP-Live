# Changelog

All notable changes to FlaxMCP-Live. The format follows [Keep a Changelog](https://keepachangelog.com), and the
versions follow [Semantic Versioning](https://semver.org). From 1.0, only a major version changes or removes a tool,
a minor version adds tools, and a patch version never changes tools. Before 1.0, a minor version could change tools.

## [Unreleased]

## [1.1.3]

### Fixed
- `editor_execute_csharp`: when the code leaves work running that holds its assembly (a Task, a thread, an event
  subscription), the error now says that the code ran, gives its result and names what holds the assembly. Before,
  it said only "The dynamic C# assembly could not be unloaded", which read as if the code had not run.

## [1.1.2]

### Changed
- Ten tool descriptions tell an agent when to use the tool instead of a similar tool: `scene_find_actor` and
  `editor_find_actors`, `scene_outline` and `editor_scene_graph`, `build_compile_scripts`, `build_check_scripts` and
  `editor_compile`, and `editor_job_start`. `content_create_material` and `editor_duplicate_actor` also tell what
  they change: no overwrite, the sceneId or parentId rule, the world transform, undo and save.
- The fork keeps its descriptions of upstream tools in `ToolDescriptions.cs`, so the upstream files do not change. No
  tool, parameter or result changes.

## [1.1.1]

### Fixed
- The tool list has no boolean schema `true` (any value) now. The server writes the empty schema `{}`, which has the
  same meaning. A client that checks the list with the MCP TypeScript SDK (Glama does) refused the whole list,
  because of `editor_get_member`, `editor_set_member`, `job_status`, `job_wait` and other tools that return any JSON.

## [1.1.0]

One new tool, for glass and water.

### Added
- `content_create_material_transparent`: a Lit material with blend mode Transparent and screen-space reflections,
  for glass, water and film, which `content_create_material` cannot make. Its base color, opacity, roughness and
  Refraction input are public parameters (`BaseColor`, `Opacity`, `Roughness`, `Refraction`), so
  `content_create_material_instance` makes variants of one material. Its description warns that Flax's Refraction
  input distorts strongly: use 1.0 for glass with something inside.

### Fixed
- `build_game` and the other headless editor runs refuse an engine older than the project's `MinEngineVersion`,
  with both versions in the message. Before, a launcher install older than the project ran, exited with -1 and
  wrote no project log, so the result said only `exitCode: -1`.
- `content_material_details` reads an input that a material parameter drives (a Get Parameter node) as that
  parameter's value. It refused such materials before, as "an unsupported base color expression".

### Changed
- The bridge protocol is version 22 (the `create_transparent_material` request). A server at 22 cannot ask an older
  plugin to compile: restart the editor after the update.

## [1.0.0]

The first official release. The version has no pre-release label.

### Added
- `editor_camera_screenshot`: a camera renders into its own render target, and the PNG is saved. It works with the
  editor covered, small or minimised, in play mode and in edit mode, at 16 to 8192 pixels per side. Camera: a Camera
  actor, the main camera in play mode, or the editor viewport's view.

### Changed
- The bridge protocol is version 20 (the `camera_screenshot` request). Publish the server and reconnect the client
  together with the plugin update.

### Fixed
- The MCP handshake gives the same name and version as `server_info`: FlaxMCP-Live and the release version. Before,
  it gave the SDK default, FlaxMcp 0.2.0.0. The tools reference header takes both from the handshake.

## [0.2.0-preview.3]

### Fixed
- The bridge host compiles also when the engine folder has old `<guid>_FlaxEngine.CSharp.dll` copies that a
  rebuild renamed aside (CS1704). The host now uses one reference for each assembly name, and the loaded assembly wins.

## [0.2.0-preview.2]

### Added
- The agent skill `skills/flaxmcp-live`: how an AI agent uses the server, workflows, Flax traps and a generated
  reference of all 57 tools. The README tells how to install it.

## [0.2.0-preview.1]

First release of the independent fork of [eXoz00rd/FlaxMCP](https://github.com/eXoz00rd/FlaxMCP) 0.1.0.

### Added
- A bridge host that stays when the editor reloads the scripts, with `editor_host_status`. When a reload interrupts a
  request, the host answers `interrupted_by_reload`. The server sends read-only requests again.
- Long operations as jobs with progress notifications: `editor_job_start`, `job_status`, `job_wait`. They replace
  `build_status` and `build_result`.
- Editor events and waiting: `editor_events`, `editor_wait_for`. `editor_compile` waits until the editor is stable.
- Game commands: `game_commands`, `game_command`, registered with `FlaxMcpBridge.GameCommands`.
- `build_check_scripts` compiles the script modules outside the editor and returns SARIF diagnostics.
- Live object tools: `editor_get_member`, `editor_set_member`, `editor_find_actors`, `editor_add_script`,
  `editor_open_scene`, and the gated `editor_invoke_method`.
- `editor_execute_csharp` snippets compile in the bridge host.
- Large-world engine support: the bridge reads actor translations and vectors as double.
- Minimum engine version 1.13.7012. The binary asset header reader works with Flax 1.13.
- Tools: `publish.sh`, `mcp_roundtrip.py`, `set_version.sh`, `check_version.sh`, `release.sh`, `quality.py`,
  `upstream_footprint.sh`, `merge_probe.sh`.
- `MAINTAINING.md`: versions, releases, upstream sync and the hook lines in upstream files.
- CI: a version check in `ci.yml`, and the workflows `quality.yml` and `release-live.yml`.

### Changed
- Package id `FlaxMcp-Live`, tool command `flax-mcp-live`.
- `server_info` reports the version with its label, and the name `FlaxMCP-Live`.
- The bridge plugin shows as "FlaxMCP-Live Bridge" in the Flax plugin list, with this repository's URL.
- A type lookup in an assembly whose types did not all load answers `type_load_failed` with the loader errors,
  instead of `unknown_type`.
- The fork makes releases with `.github/workflows/release-live.yml`. Upstream's `release.yml` runs only in upstream's
  repository.

### Removed
- The `backlog-task` agent skills (`.claude/skills`, `.agents/skills`). They work on the upstream project board.

### Fixed
- `editor_invoke_method` without arguments releases its JSON buffer at the end of the call.
- The script checker reads and writes numbers in the same format in all cultures.
