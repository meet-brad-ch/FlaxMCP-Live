using System.Text.Json;
using System.Text.Json.Serialization;
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
}
