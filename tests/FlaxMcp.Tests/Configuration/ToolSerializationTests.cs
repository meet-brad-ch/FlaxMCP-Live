using System.Text.Json;
using FlaxMcp.Configuration;
using FlaxMcp.Flax;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Xunit;

namespace FlaxMcp.Tests.Configuration;

/// <summary>Tests of the JSON options with which the tools write their results.</summary>
public sealed class ToolSerializationTests
{
    /// <summary>Null properties are written, because the output schema lists them as required.</summary>
    [Fact]
    public void Options_WriteNullProperties_TheOutputSchemaRequires()
    {
        var status = new FlaxBridgeHostStatus("v", DateTime.UnixEpoch, null, false, true, null, 0, 0, 0, 0, false, 0, 0, 0, 0, 0);

        var json = JsonSerializer.Serialize(status, ToolSerialization.Options);

        Assert.Contains("\"acceptLoopFault\":null", json);
        Assert.Contains("\"generation\":null", json);
    }

    /// <summary>
    /// Every schema in the tool list is an object: clients that check the list with the TypeScript SDK (Glama does)
    /// reject the boolean schema <c>true</c>, which the SDK writes for a JsonElement or object member.
    /// </summary>
    [Fact]
    public void ToolSchemas_HaveNoBooleanSchemas_TypeScriptClientsRejectThem()
    {
        var options = new FlaxMcpOptions { AllowCodeExecution = true };
        var services = new ServiceCollection();
        ToolRegistration.AddTools(services, options);
        using var provider = services.BuildServiceProvider();
        var problems = new List<string>();

        foreach (var tool in provider.GetServices<McpServerTool>())
        {
            var name = tool.ProtocolTool.Name;
            CollectBooleanSchemas(tool.ProtocolTool.InputSchema, name + ".inputSchema", problems);
            if (tool.ProtocolTool.OutputSchema is { } outputSchema)
            {
                CollectBooleanSchemas(outputSchema, name + ".outputSchema", problems);
            }
        }

        Assert.Empty(problems);
    }

    /// <summary>Adds the path of every schema position below <paramref name="schema"/> that is not an object.</summary>
    /// <param name="schema">A JSON schema.</param>
    /// <param name="path">The schema's path, for the message.</param>
    /// <param name="problems">The paths found so far.</param>
    private static void CollectBooleanSchemas(JsonElement schema, string path, List<string> problems)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            problems.Add(path + " = " + schema.GetRawText());
            return;
        }

        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                CollectBooleanSchemas(property.Value, path + ".properties." + property.Name, problems);
            }
        }

        if (schema.TryGetProperty("items", out var items))
        {
            CollectBooleanSchemas(items, path + ".items", problems);
        }

        foreach (var keyword in new[] { "anyOf", "oneOf", "allOf" })
        {
            if (!schema.TryGetProperty(keyword, out var branches))
            {
                continue;
            }

            var index = 0;
            foreach (var branch in branches.EnumerateArray())
            {
                CollectBooleanSchemas(branch, path + "." + keyword + "[" + index + "]", problems);
                index++;
            }
        }
    }
}
