# Task-writing conventions — FlaxMCP

Applies to tasks/issues on this repo. This document is the source of truth — AI tools (Claude, Codex, others) and humans should refer to it instead of copying these rules elsewhere. Adapted from [HEngine's `CONVENTIONS.md`](https://github.com/eXoz00rd/HEngine/blob/master/CONVENTIONS.md).

This is a living, working document — meant to be extended as the convention matures.

## Language

Tasks/issues are written in **English** — title, description, and comments. Reason: the repo, its code and its public-facing docs are in English, and English keeps the issue tracker readable for any tool or collaborator regardless of their native language.

Note: the working plan document (`docs/plan-startu.md`, not committed — see `.gitignore`) is written in Polish. That's fine for an internal working document — the convention above applies to the issue tracker, commits and pull requests.

## Task title

Format: `[System] Description of the problem/topic`

By default, **no verb in the title** — describe the problem or topic, not a ready-made action to perform. Reason: a title with a verb assumes the solution is already decided, which often isn't the case — whoever picks up the task should be able to judge for themselves how to solve it, instead of receiving a ready-made instruction that might not be the best approach.

**Bugs** — describe the symptom, not the fix:
- ✅ `[Editor] Screenshot capture untested outside headless mode`
- ❌ `[Editor] Fix screenshot capture`

**Feature / design** — describe the topic, not the finished solution:
- ✅ `[Bridge] Dispatcher lacks protocol versioning and reconnect handling`
- ❌ `[Bridge] Add protocolVersion field`

### Exception: simple, unambiguous technical tasks

When the solution is obvious and unambiguous (no design decision or "how to do it" discussion needed), a verb form is fine — the problem and the action then coincide:
- `[Build] Bump ModelContextProtocol to 2.2.0`
- `[CI] Add a coverage upload step`

**Rule of thumb:** if a title with a verb doesn't assume any still-undecided solution (config, version, simple fix), the verb form is fine. If it requires research, a design decision, or a "how to do it" discussion — go back to the verb-less form (problem/topic description). When in doubt: the verb-less form is the safer choice.

The bracketed prefix `[System]` = module/area. Use the module names as they appear in `src/`:

| Tag | Covers |
|---|---|
| `Server` | Host, DI wiring, configuration, toolset registration — `src/FlaxMcp` |
| `Project` | `.flaxproj`/`*.Build.cs`/`Content/*Settings.json` introspection tools |
| `Content` | Content indexing, GUID↔path resolution |
| `Scene` | Scene/prefab reading (offline, file-based) |
| `Build` | Process runner, compiler error parsing, `Logs/` reading |
| `Bridge` | `src/FlaxMcpBridge` — the `EditorPlugin`, the bridge host and its named-pipe transport |
| `Editor` | Live editor tools that go through the bridge (scene graph, selection, screenshot, play mode, members, compile) |
| `Events` | The editor event ring and the waiting tools |
| `Jobs` | Long operations as jobs, progress notifications |
| `Game` | Game commands that a game registers with `GameCommands` |
| `Packaging` | NuGet packaging, release workflow, `.mcp/server.json` |

Plus the cross-cutting ones: `Architecture`, `Build` (CI/tooling sense — context disambiguates from the Flax build toolset above), `CI`, `Docs`, `Tech`.

## Task type

Distinguished via labels, not in the title: `bug`, `enhancement`, `tech-debt`, `architecture`, `documentation`.

## Estimation

Label `S`/`M`/`L` — task scale varies heavily here, so even a rough label helps with planning.

## Verification-sensitive work

A separate label — `needs live editor check` — for tasks whose result cannot be confirmed by a unit test alone, because they depend on a running Flax Editor session (the bridge, live tools, screenshot capture).

This distinction matters here specifically because the bridge spike already surfaced the gap: headless mode cannot allocate a render target (`editor_screenshot` fails there with a clear engine error), and the plugin's unload path was never observed outside a real GUI session. A green test suite proves the offline tools work — it says nothing about a tool that only makes sense with an editor window open. Tasks touching the bridge or live editor tools should state in the Definition of Done how the result was actually observed (which GUI action was taken, what was seen).

## Task description (body)

A task should be **self-contained** — don't rely solely on a link to a document. Links rot, documents get moved, and the tracker (especially during quick triage) needs to be understandable without clicking through everywhere. Always include a short summary in the task itself, with longer context in a linked document (if one exists).

Description structure:

```
## Context
1-3 sentences: what, why, what problem this solves.

## Details
Concrete info needed to do the task (not the whole document).

## References
Link to the analysis / architecture doc / external material — for anyone who wants more context.

## Definition of Done
- [ ] ...
```

Example of a filled-in Definition of Done:

```
- [ ] `scene_outline` on Mournfall's `Content/Scenes/Main.scene` returns the correct actor tree
- [ ] A scene deeper/larger than the configured limit is truncated with an explicit truncation flag
- [ ] Reachable end-to-end via `tools/call`, not just unit-tested
- [ ] No new build warnings
```

The DoD must be concrete and verifiable — something that can be checked off as done/not done without interpretation, not a vague statement like "works correctly".

**For any task that adds a tool, the DoD must include that it's reachable via `tools/call` over stdio** — not merely "class exists and has tests". A tool that compiles and has a green unit test but was never actually registered, or never actually called through the real MCP protocol, isn't done.

The plan document (`docs/plan-startu.md`) remains the source of truth for larger decisions, but the task itself must provide enough context to do the work without opening anything else.

## Milestones

Grouped by capability or project phase (Phase 2 / Phase 3 / ...), not by time-based sprints — scope shifts often, so sprint dates go stale quickly, while a phase name stays current.
