# Changelog

All notable changes to FlaxMCP-Live. The format follows [Keep a Changelog](https://keepachangelog.com), and the
versions follow [Semantic Versioning](https://semver.org). Before 1.0 a minor version may change tools; a patch
version never does.

## [Unreleased]

## [0.2.0-preview.1]

First release of the independent fork of [eXoz00rd/FlaxMCP](https://github.com/eXoz00rd/FlaxMCP) 0.1.0.

### Added
- Bridge host that outlives script reloads, with `editor_host_status`; requests interrupted by a reload answer
  `interrupted_by_reload`, and read-only requests are sent again.
- Long operations as jobs with progress notifications: `editor_job_start`, `job_status`, `job_wait`. They replace
  `build_status` and `build_result`.
- Editor events and waiting: `editor_events`, `editor_wait_for`; `editor_compile` waits until the editor has settled.
- Game commands: `game_commands`, `game_command`, registered with `FlaxMcpBridge.GameCommands`.
- `build_check_scripts` compiles the script modules outside the editor and returns SARIF diagnostics.
- Live object tools: `editor_get_member`, `editor_set_member`, `editor_find_actors`, `editor_add_script`,
  `editor_open_scene`, and the gated `editor_invoke_method`.
- `editor_execute_csharp` snippets compile in the bridge host.
- Large-world engine support: actor translations and vectors are read as double.
- Minimum engine version 1.13.7012; the binary asset header reader is checked on Flax 1.13.
- `tools/publish.sh`, `tools/mcp_roundtrip.py`, `tools/set_version.sh`, `tools/check_version.sh`.

### Changed
- Package id `FlaxMcp-Live`, tool command `flax-mcp-live`.
- `server_info` reports the version with its label.
- A type lookup in an assembly whose types did not all load answers `type_load_failed` with the loader errors,
  instead of `unknown_type`.
- The fork releases with `.github/workflows/release-live.yml`; upstream's `release.yml` runs only in upstream's repository.

### Fixed
- `editor_invoke_method` without arguments no longer keeps a pooled JSON buffer after the call.
- Culture-independent number formatting and parsing in the script checker.
