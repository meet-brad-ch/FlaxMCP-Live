using System;
using FlaxEngine;

namespace FlaxMcpBridge;

/// <summary>
/// Runtime placeholder plugin. The bridge itself only runs inside the Editor
/// (FlaxMcpBridge.Editor.BridgeEditorPlugin); this class exists because
/// <c>EditorPlugin.GamePluginType</c> expects a paired game plugin.
/// </summary>
public class BridgeGamePlugin : GamePlugin
{
    /// <inheritdoc />
    public BridgeGamePlugin()
    {
        _description = new PluginDescription
        {
            Name = "FlaxMCP-Live Bridge",
            Category = "Other",
            Author = "FlaxMCP-Live",
            RepositoryUrl = "https://github.com/meet-brad-ch/FlaxMCP-Live",
            Description = "Runtime counterpart of the FlaxMcp editor bridge plugin.",
            Version = new Version(0, 2, 0),
            IsAlpha = true,
            IsBeta = false,
        };
    }
}
