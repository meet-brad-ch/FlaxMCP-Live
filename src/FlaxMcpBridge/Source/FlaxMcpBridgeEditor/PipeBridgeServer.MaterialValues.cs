using System;
using FlaxEditor.Surface;
using FlaxEngine;

namespace FlaxMcpBridge.Editor;

/// <summary>
/// The value of a material input, for content_material_details: a constant node, or a Get Parameter node that reads a
/// parameter of the graph. Materials that content_create_transparent_material makes, and many that people make in the
/// editor, drive their inputs with parameters.
/// </summary>
internal sealed partial class PipeBridgeServer
{
    /// <summary>The group of the Get Parameter node (FlaxEditor.Surface.Archetypes.Parameters).</summary>
    private const ushort ParametersGroup = 6;

    /// <summary>The Get Parameter node's type in its group.</summary>
    private const ushort GetParameterType = 1;

    /// <summary>The group of the constant nodes (FlaxEditor.Surface.Archetypes.Constants).</summary>
    private const ushort ConstantsGroup = 2;

    /// <summary>The Color constant node's type in its group.</summary>
    private const ushort ColorConstantType = 7;

    /// <summary>Reads the value that a node gives: its constant, or the value of the parameter that it reads.</summary>
    /// <param name="node">The node, or null when the input is not connected.</param>
    /// <returns>The value, or null when the input is not connected or the parameter does not exist.</returns>
    private static object? ReadNodeValue(SurfaceNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node.GroupArchetype.GroupID == ParametersGroup && node.Archetype.TypeID == GetParameterType)
        {
            return node.Values?[0] is Guid id ? node.Surface.GetParameter(id)?.Value : null;
        }

        return node.Values?[0];
    }

    /// <summary>
    /// Reads the base color from the node on the base color input, or from the node that a texture multiplies with:
    /// a Color constant or a Color parameter.
    /// </summary>
    /// <param name="colorSource">The node on the main node's base color input.</param>
    /// <returns>The base color.</returns>
    /// <exception cref="InvalidOperationException">The input holds no Color constant and no Color parameter.</exception>
    private static Color ReadBaseColor(SurfaceNode? colorSource)
    {
        SurfaceNode? node = FindNode(colorSource, ConstantsGroup, ColorConstantType) ??
            FindNode(colorSource, ParametersGroup, GetParameterType);
        return ReadNodeValue(node) is Color color ? color : throw UnsupportedMaterialGraph("base color");
    }
}
