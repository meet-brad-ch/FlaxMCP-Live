using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEditor.Content;
using FlaxEditor.Scripting;
using FlaxEditor.Surface;
using FlaxEngine;

namespace FlaxMcpBridge.Editor;

/// <summary>
/// Transparent surface materials: glass, water, plastic film. The material is Lit and Transparent and traces
/// screen-space reflections. Its base color, opacity, roughness and index of refraction are public parameters, so
/// material instances make variants of one material.
/// </summary>
internal sealed partial class PipeBridgeServer
{
    /// <summary>The main node's Opacity input (FlaxEditor.Surface.Archetypes.Material, MaterialNodeBoxes).</summary>
    private const int OpacityBox = 9;

    /// <summary>The main node's Refraction input.</summary>
    private const int RefractionBox = 10;

    /// <summary>The highest index of refraction the tool accepts. Diamond is 2.42.</summary>
    private const double MaxRefraction = 3.0;

    /// <summary>The public parameters of a transparent material, in the order of the result.</summary>
    private static readonly string[] TransparentParameters = ["BaseColor", "Opacity", "Roughness", "Refraction"];

    /// <summary>Creates a transparent Lit material asset.</summary>
    /// <param name="relativePath">The asset path below Content, ending in .flax. No asset may exist there.</param>
    /// <param name="baseColor">The base color, RGBA in 0..1.</param>
    /// <param name="opacity">0 for fully clear, 1 for opaque.</param>
    /// <param name="roughness">The surface roughness, 0..1.</param>
    /// <param name="refraction">The index of refraction, 1 (no bending) to 3.</param>
    /// <returns>The material's id and path, the values it was made with, and its parameter names.</returns>
    private static async Task<object> CreateTransparentMaterialAsync(
        string relativePath, JsonElement baseColor, double opacity, double roughness, double refraction)
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
                WriteTransparentGraph(material, color, (float)opacity, (float)roughness, (float)refraction);
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
                    parameters = TransparentParameters,
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
    /// Connects public parameters to the base color, roughness, opacity and refraction inputs and a constant 0 to the
    /// metalness input, and saves the material as Lit and Transparent with screen-space reflections. Main thread only.
    /// </summary>
    /// <param name="material">The material.</param>
    /// <param name="color">The base color.</param>
    /// <param name="opacity">The opacity.</param>
    /// <param name="roughness">The roughness.</param>
    /// <param name="refraction">The index of refraction.</param>
    /// <exception cref="InvalidOperationException">The graph could not be loaded, serialized or saved.</exception>
    private static void WriteTransparentGraph(Material material, Color color, float opacity, float roughness, float refraction)
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
            Connect(SpawnParameter(surface, "BaseColor", typeof(Color), color, new Float2(-500, -100)), 0, main, 1);
            Connect(SpawnParameter(surface, "Roughness", typeof(float), roughness, new Float2(-300, 100)), 0, main, 6);
            Connect(surface.Context.SpawnNode(2, 3, new Float2(-300, 180), [0f], null), 0, main, 4);
            Connect(SpawnParameter(surface, "Opacity", typeof(float), opacity, new Float2(-300, 260)), 0, main, OpacityBox);
            Connect(SpawnParameter(surface, "Refraction", typeof(float), refraction, new Float2(-300, 340)), 0, main,
                RefractionBox);
            if (surface.Save())
            {
                throw new InvalidOperationException("The material graph could not be serialized.");
            }

            MaterialInfo info = MaterialInfo.Default;
            info.ShadingModel = MaterialShadingModel.Lit;
            info.BlendMode = MaterialBlendMode.Transparent;
            info.TransparentLightingMode = MaterialTransparentLightingMode.Surface;
            info.FeaturesFlags |= MaterialFeaturesFlags.ScreenSpaceReflections;

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

    /// <summary>Adds a public parameter to the graph and spawns the node that reads it. Main thread only.</summary>
    /// <param name="surface">The material's graph.</param>
    /// <param name="name">The parameter's name. Material instances override it by this name.</param>
    /// <param name="type">The parameter's type: Color or float.</param>
    /// <param name="value">The parameter's value.</param>
    /// <param name="location">The node's place on the graph.</param>
    /// <returns>The Get Parameter node. Its box 0 is the value.</returns>
    private static SurfaceNode SpawnParameter(MaterialSurface surface, string name, Type type, object value, Float2 location)
    {
        var parameter = new SurfaceParameter
        {
            ID = Guid.NewGuid(),
            IsPublic = true,
            Name = name,
            Type = new ScriptType(type),
            Value = value,
        };
        surface.Parameters.Add(parameter);
        surface.OnParamCreated(parameter);
        return surface.Context.SpawnNode(6, 1, location, [parameter.ID], null);
    }
}
