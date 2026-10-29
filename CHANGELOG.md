# Changelog

All notable changes to FlaxMCP-Live. The format follows [Keep a Changelog](https://keepachangelog.com), and the
versions follow [Semantic Versioning](https://semver.org). Before 1.0, a minor version can change tools. A patch
version never changes tools.

## [Unreleased]

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
