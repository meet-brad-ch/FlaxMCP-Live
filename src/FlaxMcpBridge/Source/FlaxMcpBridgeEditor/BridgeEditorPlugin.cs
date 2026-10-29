using System;
using FlaxEditor;
using FlaxEngine;

namespace FlaxMcpBridge.Editor;

/// <summary>
/// Spike editor plugin for FlaxMCP: hosts a local named-pipe bridge so an external MCP server
/// process can query and drive a running Flax Editor session.
/// </summary>
/// <seealso cref="FlaxEditor.EditorPlugin" />
public class BridgeEditorPlugin : EditorPlugin
{
    private PipeBridgeServer? _server;

    /// <inheritdoc />
    public override Type GamePluginType => typeof(BridgeGamePlugin);

    /// <inheritdoc />
    public override void InitializeEditor()
    {
        base.InitializeEditor();

        try
        {
            _server = new PipeBridgeServer();
            _server.Start();
            Debug.Log($"[FlaxMcpBridge] Attached to the bridge host (generation {PipeBridgeServer.Generation}).");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[FlaxMcpBridge] Failed to attach to the bridge host: {ex}");
        }
    }

    /// <inheritdoc />
    public override void Deinitialize()
    {
        _server?.Dispose();
        _server = null;

        base.Deinitialize();
    }
}
