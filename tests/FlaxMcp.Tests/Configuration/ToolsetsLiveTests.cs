using FlaxMcp.Configuration;
using FlaxMcp.Tools;
using Xunit;

namespace FlaxMcp.Tests.Configuration;

/// <summary>Tests of the live fork's tool classes in the toolsets.</summary>
public sealed class ToolsetsLiveTests
{
    /// <summary>The editor toolset gives EditorTools first, then its companion tool classes.</summary>
    [Fact]
    public void Resolve_Editor_ReturnsAllEditorToolClasses()
    {
        var types = Toolsets.Resolve("editor");

        Assert.Equal([typeof(ServerInfoTool), typeof(EditorTools), typeof(EditorLiveTools), typeof(EditorObjectTools)], types);
    }
}
