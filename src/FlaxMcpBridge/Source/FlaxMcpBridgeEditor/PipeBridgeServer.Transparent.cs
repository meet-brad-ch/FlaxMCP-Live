using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEditor.Content;
using FlaxEditor.Surface;
using FlaxEngine;

namespace FlaxMcpBridge.Editor;

/// <summary>
/// Transparent surface materials: glass, water, plastic film. The material is Lit and Transparent, with constant
/// base color, opacity, roughness and index of refraction, and optional screen-space reflections.
/// </summary>
internal sealed partial class PipeBridgeServer
{
    /// <summary>The main node's Opacity input (FlaxEditor.Surface.Archetypes.Material, MaterialNodeBoxes).</summary>
    private const int OpacityBox = 9;

    /// <summary>The main node's Refraction input.</summary>
    private const int RefractionBox = 10;

    /// <summary>The highest index of refraction the tool accepts. Diamond is 2.42.</summary>
    private const double MaxRefraction = 3.0;

    /// <summary>Creates a transparent Lit material asset.</summary>
    /// <param name="relativePath">The asset path below Content, ending in .flax. No asset may exist there.</param>
    /// <param name="baseColor">The base color, RGBA in 0..1.</param>
    /// <param name="opacity">0 for fully clear, 1 for opaque.</param>
    /// <param name="roughness">The surface roughness, 0..1.</param>
    /// <param name="refraction">The index of refraction, 1 (no bending) to 3.</param>
    /// <param name="screenSpaceReflections">Whether the material traces screen-space reflections.</param>
    /// <returns>The material's id and path, and the values it was made with.</returns>
    private static async Task<object> CreateTransparentMaterialAsync(
        string relativePath, JsonElement baseColor, double opacity, double roughness, double refraction,
        bool screenSpaceReflections)
    {
        var color = ReadColor(baseColor, "baseColor");
        ValidateUnitValue(opacity, "opacity");
        ValidateUnitValue(roughness, "roughness");
        if (double.IsNaN(refraction) || refraction < 1.0 || refraction > MaxRefraction)
        {
            throw new ArgumentException($"refraction {refraction} is out of range (1..{MaxRefraction}).");
        }

        var path = (string)await InvokeOnUpdateAsync(() => ResolveContentPath(relativePath));
        var directory = Path.GetDirectoryName(path)!;
        var directoryExisted = Directory.Exists(directory);
        Material? createdMaterial = null;
        var mutationStarted = false;
        try
        {
            return await InvokeOnUpdateAsync(() =>
            {
                if (File.Exists(path) || FlaxEditor.Editor.Instance.ContentDatabase.Find(path) is not null)
                {
                    throw new InvalidOperationException($"A content item already exists at '{relativePath}'.");
                }

                Directory.CreateDirectory(directory);
                mutationStarted = true;
                Material material = createdMaterial = CreateEmptyMaterial(path);
                WriteTransparentGraph(material, color, (float)opacity, (float)roughness, (float)refraction,
                    screenSpaceReflections);
                return new
                {
                    mainThreadId = Globals.MainThreadID,
                    materialId = FormatContentGuid(material.ID),
                    materialPath = material.Path,
                    blendMode = "Transparent",
                    baseColor = BuildColor(color),
                    opacity,
                    roughness,
                    refraction,
                    screenSpaceReflections,
                };
            });
        }
        catch
        {
            if (mutationStarted)
            {
                await CleanupFailedMaterialAsync(path, directory, directoryExisted, createdMaterial);
            }

            throw;
        }
    }

    /// <summary>Creates a material asset with the editor's default graph and loads it. Main thread only.</summary>
    /// <param name="path">The asset's absolute path.</param>
    /// <returns>The loaded material.</returns>
    /// <exception cref="InvalidOperationException">The editor did not register or load the new asset.</exception>
    private static Material CreateEmptyMaterial(string path)
    {
        new MaterialProxy().Create(path, null);
        RefreshContentDatabase();
        var item = FlaxEditor.Editor.Instance.ContentDatabase.Find(path) as AssetItem ??
            throw new InvalidOperationException("The created material was not registered in the Content Database.");
        item.LoadAsync();
        Material material = Content.LoadAsync<Material>(item.ID) ??
            throw new InvalidOperationException("The created material could not be loaded.");
        if (material.WaitForLoaded(AssetLoadTimeoutMilliseconds) || !material.IsLoaded)
        {
            throw new InvalidOperationException(
                $"The created material could not be loaded within {AssetLoadTimeoutMilliseconds:0} ms.");
        }

        return material;
    }

    /// <summary>
    /// Connects constant nodes to the base color, metalness, roughness, opacity and refraction inputs, and saves the
    /// material as Lit and Transparent. Main thread only.
    /// </summary>
    /// <param name="material">The material.</param>
    /// <param name="color">The base color.</param>
    /// <param name="opacity">The opacity.</param>
    /// <param name="roughness">The roughness.</param>
    /// <param name="refraction">The index of refraction.</param>
    /// <param name="screenSpaceReflections">Whether the material traces screen-space reflections.</param>
    /// <exception cref="InvalidOperationException">The graph could not be loaded, serialized or saved.</exception>
    private static void WriteTransparentGraph(
        Material material, Color color, float opacity, float roughness, float refraction, bool screenSpaceReflections)
    {
        var owner = new MaterialSurfaceOwner(material.LoadSurface(false));
        var surface = new MaterialSurface(owner);
        try
        {
            if (surface.Load())
            {
                throw new InvalidOperationException("The editor-generated material graph could not be loaded.");
            }

            var main = surface.Nodes.Single(node => node.Type == 65537);
            Connect(surface.Context.SpawnNode(2, 7, new Float2(-500, -100), [color], null), 0, main, 1);
            Connect(surface.Context.SpawnNode(2, 3, new Float2(-300, 100), [roughness], null), 0, main, 6);
            Connect(surface.Context.SpawnNode(2, 3, new Float2(-300, 180), [0f], null), 0, main, 4);
            Connect(surface.Context.SpawnNode(2, 3, new Float2(-300, 260), [opacity], null), 0, main, OpacityBox);
            Connect(surface.Context.SpawnNode(2, 3, new Float2(-300, 340), [refraction], null), 0, main, RefractionBox);
            if (surface.Save())
            {
                throw new InvalidOperationException("The material graph could not be serialized.");
            }

            MaterialInfo info = MaterialInfo.Default;
            info.ShadingModel = MaterialShadingModel.Lit;
            info.BlendMode = MaterialBlendMode.Transparent;
            info.TransparentLightingMode = MaterialTransparentLightingMode.Surface;
            if (screenSpaceReflections)
            {
                info.FeaturesFlags |= MaterialFeaturesFlags.ScreenSpaceReflections;
            }

            if (material.SaveSurface(owner.SurfaceData, info))
            {
                throw new InvalidOperationException("The material graph could not be saved.");
            }

            material.Reload();
        }
        finally
        {
            surface.Dispose();
        }
    }
}
