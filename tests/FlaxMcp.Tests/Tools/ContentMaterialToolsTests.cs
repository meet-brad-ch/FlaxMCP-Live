using FlaxMcp.Flax;
using FlaxMcp.Tools;
using ModelContextProtocol;
using Xunit;

namespace FlaxMcp.Tests.Tools;

/// <summary>The transparent material tool checks its arguments and forwards them to the bridge.</summary>
public sealed class ContentMaterialToolsTests
{
    private static readonly FlaxColor ClearGlass = new(0.95, 0.97, 1.0);

    /// <summary>Valid arguments reach the bridge unchanged.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task CreateTransparentMaterialAsync_ForwardsTheValues()
    {
        var client = new StubMaterialClient();
        var tool = new ContentMaterialTools(client);

        await tool.CreateTransparentMaterialAsync("Materials/Glass.flax", ClearGlass, 0.15, 0.05, 1.05, false,
            TestContext.Current.CancellationToken);

        Assert.Equal("Materials/Glass.flax", client.Path);
        Assert.Equal(ClearGlass, client.Color);
        Assert.Equal(0.15, client.Opacity);
        Assert.Equal(0.05, client.Roughness);
        Assert.Equal(1.05, client.Refraction);
        Assert.False(client.ScreenSpaceReflections);
    }

    /// <summary>Without refraction and reflection values, the tool bends nothing and traces reflections.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task CreateTransparentMaterialAsync_DefaultsToNoBendingWithReflections()
    {
        var client = new StubMaterialClient();
        var tool = new ContentMaterialTools(client);

        await tool.CreateTransparentMaterialAsync("Materials/Glass.flax", ClearGlass, 0.2, 0.05,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1.0, client.Refraction);
        Assert.True(client.ScreenSpaceReflections);
    }

    /// <summary>A path without the .flax extension is refused before the bridge is called.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task CreateTransparentMaterialAsync_WithoutFlaxPath_Throws()
    {
        var client = new StubMaterialClient();
        var tool = new ContentMaterialTools(client);

        McpException error = await Assert.ThrowsAsync<McpException>(() => tool.CreateTransparentMaterialAsync(
            "Materials/Glass.png", ClearGlass, 0.2, 0.05, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(".flax", error.Message);
        Assert.Null(client.Path);
    }

    /// <summary>Values outside their ranges are refused with the value's name.</summary>
    /// <param name="opacity">The opacity.</param>
    /// <param name="roughness">The roughness.</param>
    /// <param name="refraction">The index of refraction.</param>
    /// <param name="name">The name the message must contain.</param>
    /// <returns>The test.</returns>
    [Theory]
    [InlineData(-0.1, 0.05, 1.0, "opacity")]
    [InlineData(0.2, 1.5, 1.0, "roughness")]
    [InlineData(0.2, 0.05, 0.9, "refraction")]
    [InlineData(0.2, 0.05, 3.1, "refraction")]
    public async Task CreateTransparentMaterialAsync_OutOfRange_Throws(double opacity, double roughness,
        double refraction, string name)
    {
        var tool = new ContentMaterialTools(new StubMaterialClient());

        McpException error = await Assert.ThrowsAsync<McpException>(() => tool.CreateTransparentMaterialAsync(
            "Materials/Glass.flax", ClearGlass, opacity, roughness, refraction,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(name, error.Message);
    }

    /// <summary>A color component outside 0..1 is refused.</summary>
    /// <returns>The test.</returns>
    [Fact]
    public async Task CreateTransparentMaterialAsync_WithABadColor_Throws()
    {
        var tool = new ContentMaterialTools(new StubMaterialClient());

        McpException error = await Assert.ThrowsAsync<McpException>(() => tool.CreateTransparentMaterialAsync(
            "Materials/Glass.flax", new FlaxColor(1.2, 1, 1), 0.2, 0.05,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("baseColor.r", error.Message);
    }

    /// <summary>Records the request instead of calling a bridge.</summary>
    private sealed class StubMaterialClient : IFlaxMaterialClient
    {
        /// <summary>The requested path.</summary>
        public string? Path { get; private set; }

        /// <summary>The requested color.</summary>
        public FlaxColor? Color { get; private set; }

        /// <summary>The requested opacity.</summary>
        public double Opacity { get; private set; }

        /// <summary>The requested roughness.</summary>
        public double Roughness { get; private set; }

        /// <summary>The requested index of refraction.</summary>
        public double Refraction { get; private set; }

        /// <summary>Whether reflections were requested.</summary>
        public bool ScreenSpaceReflections { get; private set; }

        /// <inheritdoc />
        public Task<FlaxTransparentMaterial> CreateTransparentMaterialAsync(
            string relativePath, FlaxTransparentMaterialRequest request, CancellationToken cancellationToken = default)
        {
            Path = relativePath;
            Color = request.BaseColor;
            Opacity = request.Opacity;
            Roughness = request.Roughness;
            Refraction = request.Refraction;
            ScreenSpaceReflections = request.ScreenSpaceReflections;
            return Task.FromResult(new FlaxTransparentMaterial(1, "id", relativePath, "Transparent", request.BaseColor,
                request.Opacity, request.Roughness, request.Refraction, request.ScreenSpaceReflections));
        }
    }
}
