# Maintaining FlaxMCP-Live

This file is for the maintainers of this fork. It tells how to make versions and releases, and how to merge upstream
changes.
The general rules for changes (branches, commits, code style, CI) are in [CONTRIBUTING.md](CONTRIBUTING.md).

## Versions and releases

FlaxMCP-Live uses [Semantic Versioning](https://semver.org). Before 1.0, a minor version can change tools. A patch version
never changes tools.

- The release version is in four files: `src/FlaxMcp/FlaxMcp.csproj`, `src/FlaxMcp/.mcp/server.json`,
  `src/FlaxMcpBridge/FlaxMcpBridge.flaxproj` and `src/FlaxMcpBridge/Source/FlaxMcpBridge/BridgeGamePlugin.cs`. Do not
  edit them by hand. `tools/set_version.sh x.y.z[-label]` writes all four. `tools/check_version.sh` fails if they are
  different. CI runs this check.
- The bridge protocol has its own number, `CurrentProtocolVersion` in `src/FlaxMcp/Flax/FlaxBridgeClient.cs`. Increase
  it in each commit that changes a pipe message. The server and the bridge plugin must come from the same release.
- To make a release:
  1. Add a section `## [x.y.z]` to `CHANGELOG.md`.
  2. Run `tools/release.sh x.y.z` on a clean `main`. It makes the commit "Release x.y.z" and the tag `vx.y.z`. It does
     not push.
  3. Push the commit and the tag.
  4. The workflow `release-live.yml` compares the tag with the project version. Then it builds, tests and packs. It
     makes the GitHub release, with the changelog section as the notes and the `.nupkg` as the only asset. A version
     with a label, for example `-preview.1`, becomes a pre-release.

## Staying in sync with upstream

This repository is a fork of [eXoz00rd/FlaxMCP](https://github.com/eXoz00rd/FlaxMCP). The code keeps the upstream
names (`FlaxMcp` namespaces, the `FlaxMcpBridge` plugin). Thus you can merge upstream changes.

```bash
git fetch upstream --no-tags
git switch -c sync-upstream main
git merge upstream/main
```

Use a merge, not a rebase. A rebase applies each commit again and needs a force-push. For one upstream fix, use
`git cherry-pick`. Increase `CurrentProtocolVersion` if the merge changes a pipe message.

### Keep the seams small

The code of this fork is in files that upstream does not have. An upstream file has only the hook lines that connect
the fork's code to it. A merge has a conflict only where upstream changes one of these lines. The hooks are:

| Upstream file | Hook |
|---|---|
| `src/FlaxMcp/Flax/FlaxBridgeClient.cs` | `partial` on `IFlaxBridgeClient` and `FlaxBridgeClient`. The editor-running checks in `GetStatusAsync` and `RequireHandshake`. Upstream's `CallAsync` has the new name `SendAsync`, and the fork's `CallAsync` calls it and tries again after a reload. The handshake and status fields of the bridge host |
| `src/FlaxMcpBridge/Source/FlaxMcpBridgeEditor/PipeBridgeServer.cs` | `partial`. The dispatcher's default arm `_ => await DispatchLiveAsync(method, root)`. The main-thread, save, play-mode, material and snippet calls into `PipeBridgeServer.Live.cs`. Upstream's pipe loop is removed, because the bridge host owns the pipe |
| `src/FlaxMcp/Tools/EditorTools.cs` | `editor_play_mode` runs as a job with progress. The second constructor gets the shared job manager |
| `src/FlaxMcp/Configuration/Toolsets.cs` | The `events`, `jobs` and `game` toolsets. `ToolsetCompanions.WithCompanions` adds the fork's tool classes to a toolset |

Rules for new work:

- Put a new feature in a new file: a `partial` class part with the name `<Class>.Live.cs`, a new tool class, or a new
  test class.
- Connect it to upstream code with one hook line. Do not change the format or the order of upstream code.
- Keep the line endings that the upstream file has in git (`git ls-files --eol <file>`).
- `tools/upstream_footprint.sh` shows the fork's lines in upstream files.
- `tools/merge_probe.sh HEAD upstream/main` counts the conflicts that a merge would cause. It does not do a checkout.
- `python tools/quality.py` checks only the files that this fork added.
