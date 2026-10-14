"""A stdio MCP round trip against a FlaxMcp server build: initialize, tools/list, then the tool calls given as
name=json (or name=@file.json) arguments, in order. Progress notifications are printed as they arrive; a structured
result that lacks a property its output schema requires counts as a failure (strict clients reject it). Prints each call's time and text result (or its error). Exits 1 if any call failed.

    python tools/mcp_roundtrip.py .publish/FlaxMcp.exe C:/path/to/FlaxProject flax_status='{}' editor_compile='{}'
"""
import dataclasses
import json
import os
import subprocess
import sys
import time


@dataclasses.dataclass
class ToolCall:
    """One tool call from the command line."""
    name: str
    arguments: dict


@dataclasses.dataclass
class CallOutcome:
    """What one tool call returned."""
    name: str
    seconds: float
    failed: bool
    text: str


class StdioMcpClient:
    """Speaks newline-delimited JSON-RPC 2.0 with a server process over its stdin and stdout."""

    def __init__(self, exe: str, project: str):
        env = dict(os.environ, FLAX_PROJECT_PATH=project, FLAX_ALLOW_CODE_EXECUTION='true')
        self._process = subprocess.Popen([os.path.abspath(exe)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                         stderr=subprocess.DEVNULL, env=env, text=True, encoding='utf-8')
        self._next_id = 0

    def request(self, method: str, params: dict) -> dict:
        """Sends a request and returns the reply with the same id; fails if the server closes its output."""
        self._next_id += 1
        self._write({'jsonrpc': '2.0', 'id': self._next_id, 'method': method, 'params': params})
        while True:
            line = self._process.stdout.readline()
            if not line:
                raise RuntimeError(f'the server closed its output during {method}')
            reply = json.loads(line)
            if reply.get('method') == 'notifications/progress':
                value = reply.get('params', {})
                print(f"  progress {value.get('progress')}: {value.get('message')}", flush=True)
                continue
            if reply.get('id') == self._next_id:
                return reply

    def notify(self, method: str) -> None:
        """Sends a notification (no reply)."""
        self._write({'jsonrpc': '2.0', 'method': method})

    def close(self) -> None:
        """Closes the server's input and waits for it to exit."""
        self._process.stdin.close()
        self._process.wait(timeout=10)

    def _write(self, message: dict) -> None:
        self._process.stdin.write(json.dumps(message) + '\n')
        self._process.stdin.flush()


class ArgumentText:
    """A call's JSON arguments: inline, or `@path` to read them from a file (Windows mangles `\\"` on the command
    line, so snippets with string literals go in a file)."""

    @staticmethod
    def read(value: str) -> str:
        """Returns the JSON text the value names."""
        if not value.startswith('@'):
            return value
        with open(value[1:], encoding='utf-8') as file:
            text = file.read()
        # A .cs file is an editor_execute_csharp body.
        return json.dumps({'code': text}) if value.endswith('.cs') else text


class SchemaCheck:
    """Checks a structured result against the tool's output schema the way a strict client does: every required
    property present, in nested objects and in the items of arrays too."""

    @staticmethod
    def missing(value, schema: dict, path: str = 'data') -> list:
        """The required properties absent from the value, as JSON paths."""
        if not isinstance(schema, dict) or value is None:
            return []
        problems = []
        if isinstance(value, dict):
            for name in schema.get('required', []):
                if name not in value:
                    problems.append(f'{path}.{name}')
            for name, child in schema.get('properties', {}).items():
                if name in value:
                    problems += SchemaCheck.missing(value[name], child, f'{path}.{name}')
        elif isinstance(value, list) and isinstance(schema.get('items'), dict):
            for index, item in enumerate(value):
                problems += SchemaCheck.missing(item, schema['items'], f'{path}[{index}]')
        return problems


class RoundTrip:
    """Runs the calls against one server and reports them."""

    def __init__(self, client: StdioMcpClient):
        self._client = client
        self._schemas = {}

    def run(self, calls: list) -> list:
        """Initializes the session, lists the tools and runs every call in order."""
        self._client.request('initialize', {'protocolVersion': '2025-06-18', 'capabilities': {},
                                            'clientInfo': {'name': 'mcp_roundtrip', 'version': '1'}})
        self._client.notify('notifications/initialized')
        tools = self._client.request('tools/list', {})['result']['tools']
        self._schemas = {tool['name']: tool.get('outputSchema') for tool in tools}
        print(f'{len(tools)} tools')
        return [self._call(call) for call in calls]

    def _call(self, call: ToolCall) -> CallOutcome:
        start = time.time()
        # every call asks for progress notifications; the server sends them only for tools that report progress
        reply = self._client.request('tools/call', {'name': call.name, 'arguments': call.arguments,
                                                    '_meta': {'progressToken': call.name}})
        seconds = time.time() - start
        if 'error' in reply:
            return CallOutcome(call.name, seconds, True, f'PROTOCOL ERROR {reply["error"]}')
        result = reply['result']
        text = ' '.join(part.get('text', '') for part in result.get('content', []))
        schema = self._schemas.get(call.name)
        if schema and not result.get('isError') and 'structuredContent' in result:
            missing = SchemaCheck.missing(result['structuredContent'], schema)
            if missing:
                return CallOutcome(call.name, seconds, True, 'OUTPUT SCHEMA: missing required ' + ', '.join(missing[:8]))
        return CallOutcome(call.name, seconds, bool(result.get('isError')), text)


def main() -> int:
    """Entry point: python mcp_roundtrip.py <FlaxMcp.exe> <project folder> name=json ..."""
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    calls = [ToolCall(name, json.loads(ArgumentText.read(arguments))) for name, arguments in
             (argument.split('=', 1) for argument in sys.argv[3:])]
    client = StdioMcpClient(sys.argv[1], sys.argv[2])
    outcomes = RoundTrip(client).run(calls)
    client.close()
    for outcome in outcomes:
        flag = 'ERROR ' if outcome.failed else ''
        print(f'{outcome.name} ({outcome.seconds:.1f} s): {flag}{outcome.text}')
    return 1 if any(outcome.failed for outcome in outcomes) else 0


if __name__ == '__main__':
    sys.exit(main())
