#!/usr/bin/env python3
"""Regenerate references/tools-reference.md from a live FlaxMCP `tools/list`.

Run this whenever the FlaxMCP-Live server changes, so the reference never drifts the way
the upstream README did (it documented 27 tools while the server exposed 43).

    python skills/flaxmcp-live/scripts/dump_tools.py --project "C:/path/to/FlaxProject"

The default server is the checkout's published .publish/FlaxMcp.exe (tools/publish.sh).

Requires: the .NET SDK on PATH and a buildable FlaxMcp checkout. The Flax Editor does
NOT need to be running -- `tools/list` is answered by the server alone.

Stdlib only. Speaks MCP over stdio directly (newline-delimited JSON-RPC 2.0) because
that is the only way to enumerate tools without an MCP client attached.
"""

import argparse
import json
import os
import pathlib
import subprocess
import sys
import threading
from collections.abc import Callable
from dataclasses import dataclass
from typing import IO, ClassVar


@dataclass(frozen=True)
class ServerTools:
    """What the server answered: its version and its tools (each as `tools/list` gives it)."""

    version: str
    tools: list


@dataclass(frozen=True)
class Reference:
    """The written reference, and the names of the tools in no group (to add to ToolReference.GROUPS)."""

    text: str
    ungrouped: list


class McpSession:
    """One stdio session with an MCP server: initialize, list the tools, end the server.

    The server process comes from `start` (injected: a real Popen, or a test's stand-in with the same stdin, stdout,
    stderr, kill and wait)."""

    PROTOCOL = "2024-11-05"

    def __init__(self, start: Callable[[], subprocess.Popen]):
        self._start = start
        self._counter = 0

    def tools(self) -> ServerTools:
        proc = self._start()
        stdin, stdout, stderr = proc.stdin, proc.stdout, proc.stderr
        if stdin is None or stdout is None or stderr is None:
            raise RuntimeError("the MCP server was started without pipes for stdin, stdout and stderr")
        # drain stderr so the server's logging can never fill the pipe and deadlock us
        threading.Thread(target=stderr.read, daemon=True).start()
        try:
            self._send(
                stdin,
                "initialize",
                {
                    "protocolVersion": self.PROTOCOL,
                    "capabilities": {},
                    "clientInfo": {"name": "flax-mcp-skill-dump", "version": "1.0"},
                },
            )
            init = json.loads(stdout.readline())["result"]
            self._send(stdin, "notifications/initialized", notify=True)
            self._send(stdin, "tools/list")
            tools = json.loads(stdout.readline())["result"]["tools"]
            # the server's version is required by MCP
            return ServerTools(init["serverInfo"]["version"], tools)
        finally:
            # the dump is done (or failed): the server goes at once, nothing of it is kept
            proc.kill()
            proc.wait(timeout=10)

    def _send(self, stdin: IO[str], method: str, params: dict | None = None, notify: bool = False) -> None:
        msg: dict = {"jsonrpc": "2.0", "method": method}
        if params is not None:
            msg["params"] = params
        if not notify:
            self._counter += 1
            msg["id"] = self._counter
        stdin.write(json.dumps(msg) + "\n")
        stdin.flush()


class ToolReference:
    """The Markdown reference of a server's tools, in the groups below; tools in no group are listed last."""

    GROUPS: ClassVar[tuple] = (
        ("Server", ("server_info", "flax_status")),
        ("Project", ("project_info", "project_targets", "project_settings")),
        (
            "Content",
            (
                "content_search",
                "content_asset_info",
                "content_resolve_guid",
                "content_material_details",
                "content_create_material",
                "content_create_material_instance",
                "content_set_material_instance_parameter",
            ),
        ),
        ("Scene (offline file parsing)", ("scene_list", "scene_outline", "scene_find_actor")),
        (
            "Build (headless - refuses to run while the GUI editor is open; build_check_scripts works any time)",
            (
                "build_check_scripts",
                "build_compile_scripts",
                "build_generate_projects",
                "build_game",
                "build_clear_cache",
            ),
        ),
        ("Logs", ("logs_tail", "logs_errors")),
        (
            "Editor - inspection (live)",
            (
                "editor_scene_graph",
                "editor_get_selection",
                "editor_actor_details",
                "editor_static_model_details",
                "editor_box_collider_details",
                "editor_host_status",
            ),
        ),
        (
            "Editor - mutation (live)",
            (
                "editor_set_selection",
                "editor_create_actor",
                "editor_duplicate_actor",
                "editor_modify_actor",
                "editor_rename_actor",
                "editor_reparent_actor",
                "editor_delete_actor",
                "editor_set_static_model",
                "editor_set_static_model_material",
                "editor_create_box_collider",
                "editor_set_box_collider",
                "editor_save",
            ),
        ),
        (
            "Editor - session (live)",
            ("editor_compile", "editor_open_scene", "editor_play_mode", "editor_screenshot", "editor_execute_csharp"),
        ),
        (
            "Editor - live objects (typed, instead of snippets)",
            (
                "editor_find_actors",
                "editor_get_member",
                "editor_set_member",
                "editor_invoke_method",
                "editor_add_script",
            ),
        ),
        ("Events (the editor's recorded events)", ("editor_events", "editor_wait_for")),
        ("Jobs (long operations)", ("editor_job_start", "job_status", "job_wait")),
        ("Game commands (registered by the game)", ("game_commands", "game_command")),
    )

    @staticmethod
    def type_of(schema: dict) -> str:
        """A JSON Schema's type in short: a schema without a type takes any value."""
        t = schema.get("type")
        if isinstance(t, list):
            t = "|".join(x for x in t if x != "null") + "?"
        if t == "object" and "properties" in schema:
            return f"object{{{', '.join(schema['properties'])}}}"
        if t == "array":
            return f"array<{ToolReference.type_of(schema.get('items', {}))}>"
        return t or "any"

    @staticmethod
    def render(tool: dict) -> str:
        """One tool's section; inputSchema is required by MCP, its properties and the descriptions are optional."""
        schema = tool["inputSchema"]
        props = schema.get("properties", {})
        required = set(schema.get("required", []))
        out = [f"### `{tool['name']}`", "", (tool.get("description") or "").strip(), ""]
        if props:
            out += ["| Param | Type | Req | Notes |", "|---|---|---|---|"]
            for name, spec in props.items():
                note = (spec.get("description") or "").replace("|", "\\|").strip()
                if spec.get("default") is not None:
                    note += f" (default: {json.dumps(spec['default'])})"
                req = "yes" if name in required else "-"
                out.append(f"| `{name}` | {ToolReference.type_of(spec)} | {req} | {note} |")
        else:
            out.append("_No parameters._")
        return "\n".join(out) + "\n"

    @staticmethod
    def document(server: ServerTools) -> Reference:
        """The whole reference."""
        by_name = {t["name"]: t for t in server.tools}
        doc = [
            "# FlaxMCP Tool Reference",
            "",
            f"Generated from a live `tools/list` on FlaxMcp {server.version} ({len(server.tools)} tools).",
            "`editor_execute_csharp` is only present when `FLAX_ALLOW_CODE_EXECUTION=true`.",
            "",
            "Regenerate with `scripts/dump_tools.py` after changing the server.",
            "",
        ]
        seen = set()
        for title, names in ToolReference.GROUPS:
            doc.append(f"## {title}\n")
            for name in names:
                if name in by_name:
                    doc.append(ToolReference.render(by_name[name]))
                    seen.add(name)
                else:
                    doc.append(f"### `{name}`\n\n_Not present in this build._\n")
        ungrouped = sorted(n for n in by_name if n not in seen)
        if ungrouped:
            doc.append("## Ungrouped (new since this script was written)\n")
            doc.extend(ToolReference.render(by_name[name]) for name in ungrouped)
        return Reference("\n".join(doc), ungrouped)


class Cli:
    """The command line: start the server, write the reference."""

    # editor_execute_csharp is registered only when code execution is enabled, so we ask
    # for it here to keep it in the generated reference. This affects the dump only.
    DUMP_ENV: ClassVar[dict] = {"FLAX_ALLOW_CODE_EXECUTION": "true"}

    @staticmethod
    def server(command: str, flax_project: str) -> Callable[[], subprocess.Popen]:
        env = {**os.environ, "FLAX_PROJECT_PATH": flax_project, **Cli.DUMP_ENV}
        # a built FlaxMcp.exe (dotnet build --artifacts-path) avoids rebuilding into bin/ while an MCP session holds it
        argv = [command] if command.lower().endswith(".exe") else ["dotnet", "run", "--project", command]
        return lambda: subprocess.Popen(
            argv, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env, text=True, bufsize=1
        )

    @staticmethod
    def run(argv: list) -> int:
        here = pathlib.Path(__file__).resolve().parent
        ap = argparse.ArgumentParser(description=__doc__)
        ap.add_argument("--project", required=True, help="Path to the .flaxproj or its folder")
        ap.add_argument(
            "--server",
            default=str(here.parents[2] / ".publish" / "FlaxMcp.exe"),
            help="A published FlaxMcp.exe (tools/publish.sh), or the server csproj folder",
        )
        ap.add_argument("--out", default=str(here.parent / "references" / "tools-reference.md"))
        args = ap.parse_args(argv)
        server = McpSession(Cli.server(args.server, args.project)).tools()
        reference = ToolReference.document(server)
        pathlib.Path(args.out).write_text(reference.text, encoding="utf-8")
        print(f"Wrote {len(server.tools)} tools to {args.out}")
        if reference.ungrouped:
            print(f"NOTE: ungrouped tools found, add them to GROUPS: {', '.join(reference.ungrouped)}")
        return 0


if __name__ == "__main__":
    sys.exit(Cli.run(sys.argv[1:]))
