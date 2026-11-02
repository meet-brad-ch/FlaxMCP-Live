using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using ModelContextProtocol;

namespace FlaxMcp.Configuration;

/// <summary>The JSON options with which the tools write their results.</summary>
internal static class ToolSerialization
{
    /// <summary>
    /// The SDK's options, except that null properties are written: the output schema lists every record parameter
    /// without a default as required, and a client that checks the schema (Claude Code does) rejects a result whose
    /// null property was left out.
    /// </summary>
    public static JsonSerializerOptions Options { get; } =
        new(McpJsonUtilities.DefaultOptions) { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    /// <summary>
    /// The schema options of the tool list: the boolean schema <c>true</c> (any value) becomes the empty object
    /// schema <c>{}</c>, which means the same. The SDK writes <c>true</c> for a JsonElement or object member, and a
    /// client that checks the list with the TypeScript SDK (Glama does) rejects it.
    /// </summary>
    public static AIJsonSchemaCreateOptions SchemaOptions { get; } =
        new() { TransformSchemaNode = (_, node) => ObjectSchema(node) };

    /// <summary>Replaces the boolean schema <c>true</c> by the empty object schema.</summary>
    /// <param name="node">A schema node.</param>
    /// <returns>The empty object schema for <c>true</c>, else <paramref name="node"/>.</returns>
    private static JsonNode ObjectSchema(JsonNode node)
    {
        bool isTrue = node.GetValueKind() == JsonValueKind.True;
        if (isTrue)
        {
            return new JsonObject();
        }

        return node;
    }
}
