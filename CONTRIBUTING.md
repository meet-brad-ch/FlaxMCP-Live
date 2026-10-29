# Contributing

## Requirements

- **.NET 10 SDK** — verify with `dotnet --list-sdks`
- **[Flax Engine](https://flaxengine.com)** installed locally — required for anything touching `src/FlaxMcpBridge` (it's a Flax plugin project, not a plain .NET SDK project); not required for `src/FlaxMcp`/`tests/FlaxMcp.Tests` alone
- C# IDE: Rider, Visual Studio 2022, or VS Code

```bash
dotnet build FlaxMcp.slnx
dotnet test FlaxMcp.slnx
```

`src/FlaxMcpBridge` is intentionally **not** part of `FlaxMcp.slnx` — it's built and loaded by Flax's own tooling (`FlaxEditor.exe`/`Flax.Build.exe`), not by `dotnet build`. See `AGENTS.md` for how to actually compile and test it.

## Workflow

1. New branch from `main` (e.g. `feat/short-description`, `fix/short-description`).
2. Make changes + commit.
3. Pull request to `main`.
4. Delete the branch after merging.

There's no enforced branch protection on `main` yet — this workflow is a discipline to follow, not (currently) a GitHub ruleset. Don't push directly to `main` anyway.

**Merge PRs by squashing.** Keeps `main`'s history one commit per change, matching the PR-sized-change discipline below.

## Commits

- First line: short summary of the whole change (no leading `#`), imperative mood.
- Rest (if needed): bullet list with details.
- One commit = one coherent change.
- Keep messages short — no walls of text.
- **No self-attribution** — no `Co-Authored-By: Claude` trailers, no "Generated with Claude Code" lines, nothing identifying AI involvement.

## Pull requests

- Title: short summary of the change, same style as a commit's first line.
- Keep PRs small and focused: roughly **≤400 changed lines** and **≤15 files**. Split larger changes.
- Description structure:

```
## Summary
1-3 bullet points describing what changed and why.

## Test plan
Bulleted checklist of how the change was verified (build, tests, what was observed in a running editor if the change touches the bridge).
```

- The same no-self-attribution rule as commits applies to the PR body.

## C# code

- `Directory.Build.props` sets `Nullable`, `ImplicitUsings`, and **`TreatWarningsAsErrors`** for everything under `FlaxMcp.slnx` — a new warning fails the build, not just CI.
- `.editorconfig` at the repo root governs formatting; run `dotnet format --verify-no-changes` before opening a PR.
- `.gitattributes` pins `eol=crlf` for the whole repo — line endings are handled at checkout regardless of a contributor's local `core.autocrlf` setting. Don't fight it by hand.
- No comments in committed code unless the code genuinely cannot express the intent on its own.
- Guard expensive logging: `if (_logger.IsEnabled(LogLevel.X))`.
- Async tests using a `CancellationTokenSource` pass `TestContext.Current.CancellationToken`.
- Never use primary constructors for services with injected dependencies.
- Constructor injection with interfaces; `readonly` fields for injected dependencies.
- `src/FlaxMcpBridge` (a Flax plugin project) is compiled by Flax's own tooling, not `dotnet build` — its own `.gitignore` and `Directory.Build.props` deliberately isolate it from the settings above. See `AGENTS.md`.

## Documentation

`docs/` is listed in `.gitignore` — files there are working documents and are not committed by default.

## CI

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) runs on `ubuntu-latest` for pushes to `main` and pull requests:

1. Restore
2. `dotnet format --verify-no-changes`
3. Build (`FlaxMcp.slnx`, Release)
4. Test

CI must be green before merging. It only covers `FlaxMcp.slnx` — `src/FlaxMcpBridge` needs a real Flax Engine install and isn't (and can't easily be) covered by a hosted runner; verify it manually per `AGENTS.md`.

## Before a PR

- `dotnet build FlaxMcp.slnx` — no new warnings
- `dotnet test FlaxMcp.slnx` — all tests green
- `dotnet format --verify-no-changes` — clean
- If the change touches `src/FlaxMcpBridge`, verify it against a real (ideally GUI, not headless) Flax Editor session — see `AGENTS.md` for what headless mode can't prove
