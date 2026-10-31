using FlaxMcp.Flax;
using FlaxMcp.Tools;
using ModelContextProtocol;
using Xunit;

namespace FlaxMcp.Tests.Tools;

/// <summary>The camera screenshot tool checks its arguments and forwards them to the bridge.</summary>
public sealed class EditorScreenshotToolsTests
{
    /// <summary>A valid request reaches the bridge with the full path, the size and a trimmed camera id.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task CaptureCameraScreenshotAsync_ForwardsTheResolvedRequest()
    {
        var client = new StubScreenshotClient();
        var tool = new EditorScreenshotTools(client);
        string path = Path.Combine(Path.GetTempPath(), "camera-shot.png");

        await tool.CaptureCameraScreenshotAsync(path, 640, 360, " 1234 ", 4, TestContext.Current.CancellationToken);

        Assert.Equal(Path.GetFullPath(path), client.Path);
        Assert.Equal(640, client.Width);
        Assert.Equal(360, client.Height);
        Assert.Equal("1234", client.CameraId);
        Assert.Equal(4, client.WarmUpFrames);
    }

    /// <summary>An empty camera id becomes null: the bridge then picks the main camera or the viewport.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task CaptureCameraScreenshotAsync_WithAnEmptyCamera_SendsNull()
    {
        var client = new StubScreenshotClient();
        var tool = new EditorScreenshotTools(client);

        await tool.CaptureCameraScreenshotAsync(
            Path.Combine(Path.GetTempPath(), "camera-shot.png"), cameraId: " ", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(client.CameraId);
        Assert.Equal(1920, client.Width);
        Assert.Equal(1080, client.Height);
    }

    /// <summary>A path that is not a PNG is refused before the bridge is called.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task CaptureCameraScreenshotAsync_WithoutPng_Throws()
    {
        var client = new StubScreenshotClient();
        var tool = new EditorScreenshotTools(client);

        await Assert.ThrowsAsync<McpException>(() => tool.CaptureCameraScreenshotAsync(
            Path.Combine(Path.GetTempPath(), "camera-shot.jpg"), cancellationToken: TestContext.Current.CancellationToken));
        Assert.Null(client.Path);
    }

    /// <summary>A missing output folder is refused before the bridge is called.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task CaptureCameraScreenshotAsync_WithAMissingFolder_Throws()
    {
        var tool = new EditorScreenshotTools(new StubScreenshotClient());
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "shot.png");

        await Assert.ThrowsAsync<McpException>(() =>
            tool.CaptureCameraScreenshotAsync(path, cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>Sizes and warm-up frames outside their ranges are refused.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="warmUpFrames">The warm-up frames.</param>
    /// <returns>The test.</returns>
    [Theory]
    [InlineData(15, 1080, 8)]
    [InlineData(1920, 8193, 8)]
    [InlineData(1920, 1080, 0)]
    [InlineData(1920, 1080, 61)]
    public async Task CaptureCameraScreenshotAsync_OutOfRange_Throws(int width, int height, int warmUpFrames)
    {
        var tool = new EditorScreenshotTools(new StubScreenshotClient());

        McpException error = await Assert.ThrowsAsync<McpException>(() => tool.CaptureCameraScreenshotAsync(
            Path.Combine(Path.GetTempPath(), "camera-shot.png"), width, height, null, warmUpFrames,
            TestContext.Current.CancellationToken));
        Assert.Contains("out of range", error.Message);
    }

    /// <summary>Records the request instead of calling a bridge.</summary>
    private sealed class StubScreenshotClient : IFlaxScreenshotClient
    {
        /// <summary>The requested path.</summary>
        public string? Path { get; private set; }

        /// <summary>The requested width.</summary>
        public int Width { get; private set; }

        /// <summary>The requested height.</summary>
        public int Height { get; private set; }

        /// <summary>The requested camera id.</summary>
        public string? CameraId { get; private set; }

        /// <summary>The requested warm-up frames.</summary>
        public int WarmUpFrames { get; private set; }

        /// <inheritdoc />
        public Task<FlaxCameraScreenshot> CaptureCameraScreenshotAsync(
            string path, int width, int height, string? cameraId, int warmUpFrames, CancellationToken cancellationToken = default)
        {
            Path = path;
            Width = width;
            Height = height;
            CameraId = cameraId;
            WarmUpFrames = warmUpFrames;
            return Task.FromResult(new FlaxCameraScreenshot(path, 1, width, height, "editor viewport", false));
        }
    }
}
