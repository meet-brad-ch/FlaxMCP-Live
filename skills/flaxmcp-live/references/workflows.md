# FlaxMCP-Live Workflows

Worked sequences for driving a Flax project over MCP, plus the `editor_execute_csharp` contract.
Parameter details are in [tools-reference.md](tools-reference.md). This file is about the order of calls and
the constraints that cause problems in practice.

## Table of Contents

- [1. Preflight and orient](#1-preflight-and-orient)
- [2. Recolour an actor](#2-recolour-an-actor)
- [3. Create a material and verify it visually](#3-create-a-material-and-verify-it-visually)
- [4. Compile scripts while the editor is open](#4-compile-scripts-while-the-editor-is-open)
- [4b. Drive the game: play, commands, events, live objects](#4b-drive-the-game-play-commands-events-live-objects)
- [5. Build a small scene](#5-build-a-small-scene)
- [6. Diagnose a failure](#6-diagnose-a-failure)
- [The editor_execute_csharp contract](#the-editor_execute_csharp-contract)
- [Verified snippets](#verified-snippets)

---

## 1. Preflight and orient

```python
flax_status()          # connected:true? -> live editor_* tools are usable
project_info()         # name, targets, references (confirms FlaxMcpBridge is referenced)
scene_list()           # -> [{"relativePath": "Scenes/Main.scene", ...}]
scene_outline(path="Scenes/Main.scene")   # feed back the relativePath verbatim
editor_scene_graph()   # the LIVE actor tree, including unsaved state
```

`scene_outline` / `scene_find_actor` take the **`relativePath` reported by `scene_list`** (Content-relative,
e.g. `Scenes/Main.scene`), not an absolute path.

`scene_list` / `scene_outline` parse files **on disk**. `editor_scene_graph` reads the **live** editor, including
edits the user has not saved. When they disagree, the live view is the truth about what is on screen. The disk view
is the truth about what survives a restart.

`editor_scene_graph` truncates at 500 nodes / 32 levels and sets `truncated: true`. If you see that flag,
narrow the search with `scene_find_actor(path=..., name=...)`. Do not try to page the graph.

---

## 2. Recolour an actor

This is the full chain, end to end. Take every ID from a previous response. Never construct an ID by hand.

```python
# 1. Find the actor, take its dashed GUID from the tree
editor_scene_graph()
# -> {"id": "a6c68c34-3172-424f-b646-3747bb122685", "name": "Cube", "typeName": "FlaxEngine.StaticModel"}

# 2. Inspect what it currently uses (also confirms it IS a StaticModel)
editor_static_model_details(actorId="a6c68c34-3172-424f-b646-3747bb122685")

# 3. Either reuse an existing material...
content_search(query="Material", typeName="FlaxEngine.Material")
#    ...or create one (see workflow 3)

# 4. Assign to a slot. materialId is the 32-hex content GUID, verbatim from step 3.
editor_set_static_model_material(
    actorId="a6c68c34-3172-424f-b646-3747bb122685",
    slotIndex=0,
    materialId="eef51a054e0fe2026700fe804c7a0e21",
)

# 5. LOOK at it. The call above returns success even when the render is wrong.
editor_screenshot(path="C:/some/existing/dir/check.png")   # then Read the PNG

# 6. Persist. Until this runs the change is in memory only.
editor_save()
```

The server validates slot bounds against the model. An out-of-range `slotIndex` returns
`"StaticModel actor '<id>' has N material slots."` A primitive cube has exactly one slot.

---

## 3. Create a material and verify it visually

```python
content_create_material(
    relativePath="Materials/Green.flax",   # relative to Content/, must end .flax
    baseColor={"r": 0.0, "g": 1.0, "b": 0.0, "a": 1.0},
    roughness=0.5,
    metallic=0.0,
)
```

The server enforces these constraints:

- `relativePath` must stay inside `Content/`, must end in `.flax`, and may not contain `.`/`..` segments.
  The server creates missing parent folders.
- `baseColor` components, `roughness` and `metallic` must be finite and within `0-1`.
- If you create over an existing item, the call fails with `"A content item already exists at '<path>'."`. Pick a
  new name, or delete the old item first.
- The server rejects `uvTiling` unless you also pass `baseColorTextureId` or `normalTextureId`.

Then assign the material as in workflow 2, take a screenshot, and **read the image**.

> **Do not trust `Material.Info` to verify a material.** When read through the C# API, it reported
> `ShadingModel = "Lit"` for a material whose asset editor clearly showed **Unlit**. Ground truth is the
> asset editor (or the rendered result), not that property. This cost real debugging time. A material can
> report perfect colour/roughness/metallic and still render solid black.

For variants of one base material, prefer an instance over a second material:

```python
content_create_material_instance(
    relativePath="Materials/GreenDark.flax",
    baseMaterialId="eef51a054e0fe2026700fe804c7a0e21",
    parameters={},   # only PUBLIC parameters of the base material can be overridden
)
```

A plain material that `content_create_material` creates exposes **no** public parameters. Thus
`content_set_material_instance_parameter` has nothing to set on its instances, unless the base graph
declares some. Supported parameter types are `Color, Float, Vector2, Vector3, Vector4, Texture, Bool`.

---

## 4. Compile scripts while the editor is open

`build_*` tools take an exclusive per-project session lock. The running GUI editor holds that lock. Thus, while the
editor is open, use `editor_compile`:

```python
editor_compile()   # stops play, compiles, waits until the editor has reloaded and settled
# -> {"outcome": "reloaded" | "unchanged" | "failed", "seconds": 1.9, "errors": [...], "buildErrors": [...], "reloads": 1}
```

- It first checks the scripts outside the editor (`build_check_scripts`). A failed check returns `failed` with
  structured errors (file, line, column, code) in ~0.3 s and does not touch the editor. `check` in the result tells
  the outcome of the check: `passed`, `failed`, or `unavailable: <reason>` before the first build.
- A `failed` from the editor's own build carries the errors and the build tool's lines (`Failed to run build tool`,
  `Sharing violation`). `editor_compile` reads them from the log lines written after the request.
- The editor compiles changed sources on its own (its file watcher). When sources are marked changed,
  `editor_compile` uses the editor's own check. It waits until no compile runs and no change is pending for 0.5 s.
  Thus a second reload cannot occur after it returned. `reloads` is the number of reloads that it saw.
- It runs as a job. With a progress token, you get "compiling" and "reloaded 1x, waiting for the editor to settle".
  `editor_job_start(kind="compile")` starts it and does not wait.

---

## 4b. Drive the game: play, commands, events, live objects

```python
editor_play_mode(action="start")                 # waits for the scene load (7 s on a heavy scene)
game_commands()                                  # what the game registered (FlaxMcpBridge.GameCommands)
game_command(name="status", args="")             # runs on the main thread, returns its text
editor_find_actors(scriptType="PlayerController")   # -> id, name, type, path, scripts
editor_get_member(actorId=id, scriptType="PlayerController", member="Score")
editor_set_member(actorId=id, member="Position.Y", value=1200)   # a struct on the way is written back
seq = editor_events(limit=1)["lastSeq"]
editor_wait_for(kind="log", textContains="[Test] wave cleared", sinceSeq=seq, timeoutSeconds=120)
editor_play_mode(action="stop")
```

- A game registers commands in its scripts' OnEnable and disposes them in OnDisable:
  `_registrations.Add(GameCommands.Register("status", "What it does.", arg => Status()));`. The game module depends
  on the bridge's runtime module `FlaxMcpBridge`. Errors: `unknown_command` lists the names. `command_failed`
  carries the exception. A handler must return within the 5 s request timeout. For long work, the handler logs a
  line to wait for.
- In play mode, a change from `editor_set_member` lasts until play ends. In edit mode, the change goes through the
  editor's undo and marks the scene edited. `editor_save` keeps it.
- Event kinds: log, exception, compile_begin, compile_succeeded, compile_failed, reload_begin, reload_end, play_begin,
  play_end, scene_loaded, scene_load_error, scene_saved, scene_save_error, scene_unloaded. Log events are managed lines
  only (Debug.Log). Compiler output is native and stays in the log file.
- `editor_job_start` (compile, play_start/stop/pause/resume, open_scene) returns at once. `job_wait` follows the job
  with progress notifications. Editor jobs run one at a time (`job_conflict`).

---

## 5. Build a small scene

`editor_create_actor` accepts an allowlist of exactly two types: `EmptyActor` and `StaticModel`. Any other
type returns `"Actor type '<t>' is not allowed. Allowed types: EmptyActor, StaticModel."`

```python
editor_create_actor(
    actorType="StaticModel", name="Crate",
    sceneId="<scene guid from editor_scene_graph>", parentId=None,
    translation={"x": 0, "y": 100, "z": 0},
    orientation={"x": 0, "y": 0, "z": 0, "w": 1},
    scale={"x": 1, "y": 1, "z": 1},
)
editor_set_static_model(actorId="<new actor guid>", modelId="<model guid from content_search>")
editor_create_box_collider(
    parentId="<new actor guid>", name="BoxCollider",
    size={"x": 100, "y": 100, "z": 100}, center={"x": 0, "y": 0, "z": 0}, isTrigger=False,
)
editor_save()
```

Transform rules:

- Y is up. Translations are world-space. Orientation is a quaternion.
- `editor_modify_actor` **replaces the entire transform**. Call `editor_actor_details` first. Then copy into the
  call the components that you do not intend to change.
- `editor_reparent_actor` needs exactly one of `sceneId` / `parentId`
  (`"Set exactly one of sceneId or parentId."`). It cannot move actors between scenes. It refuses to parent
  an actor to itself or to a descendant.
- `editor_delete_actor` refuses an actor with children unless `deleteDescendants=true`.

---

## 6. Diagnose a failure

```python
logs_errors()        # warnings + errors only, newest log file
logs_tail(lines=80)  # surrounding context: shader compiles, asset imports, play-mode transitions
```

The logs carry data that no tool payload mentions: shader compilation results, asset (re)import, and play-mode
state changes. A material that "created successfully" but renders wrong usually has its explanation here.

Use a targeted `editor_execute_csharp` probe only after you read the logs. Let the probe answer a single question.

Play mode: `editor_play_mode(action="start"|"stop"|"pause"|"resume")`. When you enter or leave play mode, the scene
reloads, and the bridge can disconnect for a short time. If a later call fails, run `flax_status` again.
Runtime changes made during play are discarded on stop.

---

## The `editor_execute_csharp` contract

The tool requires `FLAX_ALLOW_CODE_EXECUTION=true` **and** `FLAX_READ_ONLY` off. When either setting is wrong,
the tool is absent from the tool list. There is no error to read.

The server interpolates your snippet verbatim as a **static method body**:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using FlaxEngine;
using FlaxEditor;

namespace FlaxMcpBridge.Dynamic;

public static class CodeExecution
{
    public static object? Execute()
    {
        // <-- your snippet lands here
    }
}
```

Consequences:

- **No `using` directives, no type declarations** (no `class`, `record`, `struct`). Local functions are fine.
  Fully qualify anything outside those five namespaces, e.g. `System.IO.File`, `System.Text.Json`.
- **No `await`**. The method is synchronous. It runs on the editor main thread.
- `return` an object. Anonymous types serialise correctly. Results are capped at depth 32. Results with reference
  cycles or null dictionary keys are rejected.
- It must finish inside the **5 second** bridge request timeout.
- **Load assets by path, not by a hand-built `Guid`.** Content GUIDs use a Flax-specific encoding (see
  SKILL.md rule 2). `new Guid(...)` on a 32-hex content id silently addresses the wrong asset.

---

## Verified snippets

All of these snippets ran against Flax 1.12.6912.

**Select the Editor viewport so a screenshot has a live render target:**

```csharp
var win = FlaxEditor.Editor.Instance.Windows.Windows
    .OfType<FlaxEditor.Windows.EditGameWindow>()
    .First(w => w.Title == "Editor");
win.SelectTab();
win.Focus();
return new { title = win.Title, selected = win.IsSelected };
```

**Enumerate editor windows (to find the correct window):**

```csharp
return FlaxEditor.Editor.Instance.Windows.Windows
    .Select(w => new { type = w.GetType().FullName, title = w.Title,
                       selected = w.IsSelected, hidden = w.IsHidden })
    .ToList();
```

**Load a material by path (the correct way to reach an asset from a snippet):**

```csharp
var material = FlaxEngine.Content.LoadAsync<FlaxEngine.Material>(
    @"C:\path\to\Project\Content\Materials\Green.flax");
material.WaitForLoaded();
return new { path = material.Path, loaded = material.IsLoaded };
```

**Open an asset's editor window (ground truth for materials):**

```csharp
var item = FlaxEditor.Editor.Instance.ContentDatabase.Find(
    @"C:\path\to\Project\Content\Materials\Green.flax");
if (item == null) return new { error = "not found" };
FlaxEditor.Editor.Instance.ContentEditing.Open(item);
return new { opened = true };
```

**Delete content items through the database (not by deleting files).** Do not delete a loaded asset and create a new
one at the same path. The delete is deferred, and it removed the new file (see flax-gotchas.md, Content). Rebuild in
place.

```csharp
var item = FlaxEditor.Editor.Instance.ContentDatabase.Find(
    @"C:\path\to\Project\Content\Materials\Scratch.flax");
if (item != null) FlaxEditor.Editor.Instance.ContentDatabase.Delete(item, true);
return new { deleted = item != null };
```
