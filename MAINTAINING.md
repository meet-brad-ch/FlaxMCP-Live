# Maintaining FlaxMCP-Live

This file is for the maintainers of this fork: versions and releases, and how to take in upstream changes.
The general rules for changes (branches, commits, code style, CI) are in [CONTRIBUTING.md](CONTRIBUTING.md).

## Versions and releases

FlaxMCP-Live uses [Semantic Versioning](https://semver.org). Before 1.0 a minor version may change tools; a patch version never does.

- The release version is in three files: `src/FlaxMcp/FlaxMcp.csproj`, `src/FlaxMcp/.mcp/server.json` and `src/FlaxMcpBridge/FlaxMcpBridge.flaxproj`. Do not edit them by hand. `tools/set_version.sh x.y.z[-label]` writes all three, and `tools/check_version.sh` (also a CI step) fails if they differ.
- The bridge protocol has its own number, `CurrentProtocolVersion` in `src/FlaxMcp/Flax/FlaxBridgeClient.cs`. Raise it in every commit that changes a pipe message. The server and the bridge plugin must come from the same release.
- To release: add a section `## [x.y.z]` to `CHANGELOG.md`, then run `tools/release.sh x.y.z` on a clean `main`. It makes the commit "Release x.y.z" and the tag `vx.y.z`, and pushes nothing. Push the commit and the tag. The release workflow checks the tag against the project version, builds, tests, packs, and creates the GitHub release with the changelog section as notes and the `.nupkg` as its only asset. A version with a label (for example `-preview.1`) becomes a pre-release.

## Staying in sync with upstream

This repository is a fork of [eXoz00rd/FlaxMCP](https://github.com/eXoz00rd/FlaxMCP). The code keeps the upstream names (`FlaxMcp` namespaces, the `FlaxMcpBridge` plugin), so upstream changes can be taken in.

```bash
git fetch upstream --no-tags
git switch -c sync-upstream main
git merge upstream/main
```

Merge instead of rebasing: a rebase replays every commit and needs a force-push. For one upstream fix, use `git cherry-pick`. Raise `CurrentProtocolVersion` if the merge changes a pipe message.

### Keep the seams small

Our code lives in files that upstream does not have. An upstream file holds only the hook lines that join our code to it, so a merge conflicts only where upstream changes one of those lines. The hooks are:

| Upstream file | Hook |
|---|---|
| `src/FlaxMcp/Flax/FlaxBridgeClient.cs` | `partial` on `IFlaxBridgeClient` and `FlaxBridgeClient`; the editor-running checks in `GetStatusAsync` and `RequireHandshake`; `CallAsync` renamed to `SendAsync` (our retrying `CallAsync` wraps it); the handshake and status fields of the bridge host |
| `src/FlaxMcpBridge/Source/FlaxMcpBridgeEditor/PipeBridgeServer.cs` | `partial`; the dispatcher's default arm `_ => await DispatchLiveAsync(method, root)`; the main-thread, save, play-mode, material and snippet calls into `PipeBridgeServer.Live.cs`. Upstream's own pipe loop is removed, because the bridge host owns the pipe |
| `src/FlaxMcp/Tools/EditorTools.cs` | `editor_play_mode` runs as a job with progress; the second constructor that takes the shared job manager |
| `src/FlaxMcp/Configuration/Toolsets.cs` | the `events`, `jobs` and `game` toolsets; `ToolsetCompanions.WithCompanions` adds our tool classes to a toolset |

Rules for new work:

- Put a new feature in a new file: a `partial` class part named `<Class>.Live.cs`, a new tool class, or a new test class.
- Join it to upstream code with one hook line. Do not reformat or reorder upstream code.
- Keep the line endings that the upstream file has in git (`git ls-files --eol <file>`).
- `tools/upstream_footprint.sh` prints our lines in upstream files. `tools/merge_probe.sh HEAD upstream/main` counts the conflicts a merge would cause, without a checkout. `python tools/quality.py` checks only the files that this fork added.
