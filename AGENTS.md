# Agent instructions — FlaxMCP-Live

**This file is the single source of truth for AI tools working on this repository.** It applies to Claude Code, Codex, Junie, and any other assistant.

| Document | Scope |
|---|---|
| [`CONVENTIONS.md`](CONVENTIONS.md) | Writing tasks/issues |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Branch/PR flow, commits, CI |
| [`MAINTAINING.md`](MAINTAINING.md) | Versions, releases, upstream sync, the hook lines in upstream files |
| [`CHANGELOG.md`](CHANGELOG.md) | The changes in each release |
| [`README.md`](README.md) | Project pitch and usage — kept honest about current status, not aspirational |

---

## 1. Verify before you trust

FlaxMCP-Live is a preview release. Two things follow from that:

- **The tool list in `README.md` is the current list.** The server has 57 tools (55 by default, 2 more with `FLAX_ALLOW_CODE_EXECUTION`). Before you use a tool, find its `[McpServerTool]` in `src/FlaxMcp/Tools/`.
- **The bridge has two parts.** The editor plugin (`src/FlaxMcpBridge/Source`) attaches to a bridge host (`src/FlaxMcpBridge/Host`). The plugin compiles the host one time for each editor process, and the host owns the named pipe. Thus the bridge stays connected when the editor reloads the scripts. The pipe protocol has a version number (`CurrentProtocolVersion` in `src/FlaxMcp/Flax/FlaxBridgeClient.cs`). Increase it when you change a pipe message.

The local Flax Engine source checkout (wherever `FLAX_ENGINE_PATH`/`Source/` points) contains the C++ engine headers but **not** the C# `FlaxEditor` source — the editor's C# tooling (`FlaxEditor.Modules`, `FlaxEditor.Windows`, etc.) isn't available to read locally. When you need to know an exact API shape there, two things actually work:

1. **Reflection over the real assembly** — load `FlaxEngine.CSharp.dll` (from `Binaries/Editor/Win64/<Config>/`) with `System.Reflection` and inspect it directly. This is how every API surface used in `src/FlaxMcpBridge` was confirmed, not guessed.
2. **The official docs and [`FlaxEngine/ExamplePlugin`](https://github.com/FlaxEngine/ExamplePlugin)** — for plugin project structure and lifecycle conventions specifically.

Don't guess an API shape from memory or general engine familiarity — verify it one of those two ways first.

## 2. Build and test

- Never ask the user to perform manual verification.
- Never perform manual verification or control the user's computer.
- Use automated tests and MCP tools only. If they cannot verify a requirement, report the limitation without asking for or attempting a manual check.

```bash
dotnet build FlaxMcp.slnx
dotnet test FlaxMcp.slnx
dotnet format --verify-no-changes
```

Also run `python tools/quality.py` (analyzers, complexity and coverage on the files that this fork added) and `tools/check_version.sh`.

`src/FlaxMcpBridge` is a **Flax plugin project**, not a plain .NET SDK project — `dotnet build` on `FlaxMcp.slnx` doesn't touch it, and `dotnet build` on its Flax-generated `.csproj` (under `Cache/Projects/`) silently does nothing (its `CustomAfterMicrosoftCommonTargets` short-circuits the `Build` target). The test project compiles some bridge files (`BridgeHostServer`, `BridgeEventRing`, `SnippetCache`, `GameCommands`). To compile the plugin modules, call the MCP tool `build_check_scripts` on a project that references the plugin. It compiles all script modules in less than a second, also while the editor is open. The bridge host compiles only when the editor starts the plugin. Check it with `flax_status` and `editor_host_status`. Without a running server, launch the engine:

```bash
"<engine>/Binaries/Editor/Win64/Development/FlaxEditor.exe" -project "<some-project-referencing-the-plugin>" -headless
```

then reading that project's `Logs/*.txt` (UTF-16LE, no BOM — decode explicitly, e.g. `iconv -f UTF-16LE -t UTF-8` for a manual check). `-std` does **not** reliably redirect that log to a captured stdout when the process is launched detached — don't rely on it.

Two other things learned building the Spike S1 bridge and worth not re-discovering:

- Flax's game/editor scripting modules have a trimmed .NET reference set. Anything beyond `FlaxEngine.CSharp`/`Newtonsoft.Json` (e.g. `System.IO.Pipes`, `System.Text.Json`) needs an explicit `options.ScriptingAPI.SystemReferences.Add("...")` in the module's `*.Build.cs`, or compilation fails with `CS1069`/`CS0234`.
- A game project picks up a plugin's `EditorPlugin` automatically just by listing the plugin's `.flaxproj` in its own `References` array — no `PrivateDependencies`/`Modules.Add` needed on the consumer side. Confirmed, not assumed.

## 3. Layout

```
src/FlaxMcp/           the MCP server — stdio transport, tools, configuration
  Configuration/        options, validation, toolset registration
  Flax/                 domain: reading .flaxproj, engine auto-detection
  Tools/                [McpServerToolType] classes
src/FlaxMcpBridge/      Flax plugin project (own .flaxproj) — the live-editor bridge
  Source/FlaxMcpBridge/        runtime module: GamePlugin, GameCommands
  Source/FlaxMcpBridgeEditor/  editor module: the EditorPlugin, the request handler, the host bootstrap
  Host/                        bridge host, compiled at runtime: pipe, event ring, snippet compiler
tests/FlaxMcp.Tests/    xunit.v3, mirrors src/FlaxMcp/ structure
tools/                  publish, MCP round trip, version, release, quality and upstream-merge scripts
```

A file with the name `<Class>.Live.cs` is a `partial` part that has this fork's members of an upstream class. See `MAINTAINING.md`.

Rider MCP tools are preferred over raw console commands for `.NET`/C# work when available; fall back to the console when they fail.

## 4. Code rules

- No new build warnings — `TreatWarningsAsErrors` in `Directory.Build.props` means this fails the build locally, not just CI.
- Guard expensive logging: `if (_logger.IsEnabled(LogLevel.X))`.
- Async tests using a `CancellationTokenSource` pass `TestContext.Current.CancellationToken`.
- Constructor injection with interfaces; `readonly` fields for injected dependencies.
- **Never use primary constructors for services with injected dependencies**, even if tooling suggests it or reports it as a warning.
- Don't add EF Core migrations, feature flags, or backwards-compatibility shims — none of that applies here, and if it ever does, ask first.
- In the files that this fork added, write a `///` doc comment on each class, method and member. Inside a method, add a comment only when the *why* is not clear from the code (a hidden constraint, a workaround for an engine quirk). In upstream files, follow the upstream style.

**Never add self-attribution** to commits or PR bodies — no `Co-Authored-By: Claude` trailers, no "Generated with Claude Code", nothing identifying AI involvement.

## 5. Wiring new tools

A tool is done when it's **registered and reachable through a real MCP `tools/call`** — not when its class exists and has a green unit test. Verify with an actual stdio round-trip (`initialize` → `tools/list` → `tools/call`) against a real Flax project, the same way every tool in `src/FlaxMcp/Tools/` was checked before being committed.

- Put a new tool in a new tool class. Register the class in `Toolsets`, or add it to an existing toolset in `ToolsetCompanions` (see `CONVENTIONS.md`'s `[System]` table) — a tool that exists but isn't in the registry isn't reachable.
- Respect `FLAX_READ_ONLY`: give write-capable tools `ReadOnly = false` (or omit it) on `[McpServerTool]` so the filter actually excludes them.
- A tool that depends on the live bridge (anything in the `editor` toolset) should fail with a clear "bridge not connected" error when no editor session is running, not throw an unhandled exception.

## 6. Scope discipline

- One task at a time; finish it before starting another.
- Keep pull requests small: roughly ≤400 changed lines and ≤15 files.
- A request to create or open a pull request explicitly authorizes pushing the required branch to this repository's configured GitHub remote. Do not ask for separate push approval.
- Do not commit until the work has been reviewed and you get an explicit go-ahead.
- Ask when a requirement or expected behaviour is unclear rather than assuming.

## 7. Writing documents

Code, comments, commits, pull requests and issues are in English (`CONVENTIONS.md`). Write documentation in the style of Simplified Technical English: short sentences, active voice, one word for one meaning, no semicolons.
