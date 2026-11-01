using System.ComponentModel;
using FlaxMcp.Flax;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace FlaxMcp.Tools;

/// <summary>Material assets that content_create_material cannot make: transparent ones, for glass and water.</summary>
[McpServerToolType]
public sealed class ContentMaterialTools
{
    /// <summary>The highest index of refraction the tool accepts. Diamond is 2.42.</summary>
    internal const double MaxRefraction = 3.0;

    /// <summary>The bridge client that creates the materials.</summary>
    private readonly IFlaxMaterialClient _client;

    /// <summary>Creates the tools.</summary>
    /// <param name="client">Creates the materials through the bridge.</param>
    public ContentMaterialTools(IFlaxMaterialClient client)
    {
        _client = client;
    }

    /// <summary>Creates a transparent Lit material asset whose four values are public parameters.</summary>
    /// <param name="relativePath">The asset path below Content, ending in .flax.</param>
    /// <param name="baseColor">The base color, each component in 0..1.</param>
    /// <param name="opacity">0 for fully clear, 1 for opaque.</param>
    /// <param name="roughness">The surface roughness, 0..1.</param>
    /// <param name="refraction">The index of refraction, 1..3. 1 bends nothing.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The created material.</returns>
    /// <exception cref="McpException">The path is not a .flax path, or a value is out of range.</exception>
    [McpServerTool(Name = "content_create_transparent_material", UseStructuredContent = true)]
    [Description(
        "Creates a transparent surface Material (blend mode Transparent, Lit) through the live Flax Editor, for glass, " +
        "water or film. Opacity 0 is fully clear. Refraction is the material's Refraction input: 1 bends nothing. " +
        "Flax applies it as a strong screen distortion: 1.03 already shrinks objects behind a bottle-sized surface to " +
        "about 40 % of their width, as through thick glass. Use 1.0 for thin glass with something inside (a bottle " +
        "with liquid), and values above 1 only for a lens effect. The material traces screen-space reflections. " +
        "Its four values are public parameters named BaseColor, Opacity, Roughness and Refraction, so " +
        "content_create_material_instance makes variants (a tint, frosted or thicker glass, water) of one material. " +
        "Paths are relative to Content/ and must end in .flax. No asset may exist at the path."
    )]
    public Task<FlaxTransparentMaterial> CreateTransparentMaterialAsync(
        [Description("Destination path relative to Content/, ending in .flax.")] string relativePath,
        [Description("Uniform RGBA base color, every component in 0..1. Near white for clear glass.")] FlaxColor baseColor,
        [Description("Opacity in 0..1. 0 is fully clear, 1 is opaque. Clear glass: about 0.1 to 0.25.")] double opacity,
        [Description("Surface roughness in 0..1. Polished glass: about 0.05.")] double roughness,
        [Description("Refraction input in 1..3. 1 bends nothing. Even 1.03 visibly shrinks what is behind the surface.")]
        double refraction = 1.0,
        CancellationToken cancellationToken = default)
    {
        // McpException carries its message to the client. Other exceptions reach it without their text.
        if (string.IsNullOrWhiteSpace(relativePath) ||
            !relativePath.EndsWith(".flax", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException($"relativePath must be a path below Content/ ending in .flax: '{relativePath}'.");
        }

        CheckUnit(baseColor.R, "baseColor.r");
        CheckUnit(baseColor.G, "baseColor.g");
        CheckUnit(baseColor.B, "baseColor.b");
        CheckUnit(baseColor.A, "baseColor.a");
        CheckUnit(opacity, nameof(opacity));
        CheckUnit(roughness, nameof(roughness));
        if (double.IsNaN(refraction) || refraction < 1.0 || refraction > MaxRefraction)
        {
            throw new McpException($"refraction {refraction} is out of range: it must be 1..{MaxRefraction}.");
        }

        var request = new FlaxTransparentMaterialRequest(baseColor, opacity, roughness, refraction);
        return _client.CreateTransparentMaterialAsync(relativePath, request, cancellationToken);
    }

    /// <summary>Checks that a value lies in 0..1.</summary>
    /// <param name="value">The value.</param>
    /// <param name="name">The value's name, for the message.</param>
    /// <exception cref="McpException">The value is out of range.</exception>
    private static void CheckUnit(double value, string name)
    {
        if (double.IsNaN(value) || value < 0.0 || value > 1.0)
        {
            throw new McpException($"{name} {value} is out of range: it must be 0..1.");
        }
    }
}
