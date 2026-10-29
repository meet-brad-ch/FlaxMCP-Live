using FlaxMcp.Flax;
using FlaxMcp.Tools;
using Xunit;

namespace FlaxMcp.Tests.Tools;

/// <summary>Tests of the live fork's editor tools: checked compile, scene open and host status.</summary>
public sealed class EditorLiveToolsTests
{
    /// <summary>A clean check goes on to the editor's compile; the result carries the check and its time.</summary>
    /// <returns>The test's task.</returns>
    [Fact]
    public async Task CompileAsync_ReturnsBridgeResult()
    {
        var expected = new FlaxEditorCompileResult("reloaded", 1.2, [], [], 1);
        var bridge = new EditorToolsTests.StubBridgeClient { CompileResult = expected };
        var tool = new EditorLiveTools(bridge, new FlaxJobManager(), new StubScriptChecker());

        var result = await tool.CompileAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(expected with { Check = "passed", Seconds = expected.Seconds + 0.5 }, result);
    }

    /// <summary>editor_host_status passes collect to the bridge and returns its status.</summary>
    /// <returns>The test's task.</returns>
    [Fact]
    public async Task GetHostStatusAsync_ForwardsCollect()
    {
        var client = new EditorToolsTests.StubBridgeClient();
        var tool = new EditorLiveTools(client, new FlaxJobManager(), new StubScriptChecker());

        var result = await tool.GetHostStatusAsync(true, TestContext.Current.CancellationToken);

        Assert.Same(client.HostStatus, result);
        Assert.True(client.HostStatusCollect);
    }

    /// <summary>editor_open_scene passes the path to the bridge and returns its result.</summary>
    /// <returns>The test's task.</returns>
    [Fact]
    public async Task OpenSceneAsync_ForwardsPath()
    {
        var expected = new FlaxEditorOpenSceneResult(12, "abc", "Main", ["Main"]);
        var bridge = new EditorToolsTests.StubBridgeClient { OpenSceneResult = expected };
        var tool = new EditorLiveTools(bridge, new FlaxJobManager(), new StubScriptChecker());

        var result = await tool.OpenSceneAsync("Scenes/Main.scene", TestContext.Current.CancellationToken);

        Assert.Same(expected, result);
        Assert.Equal("Scenes/Main.scene", bridge.ScenePath);
    }
}
