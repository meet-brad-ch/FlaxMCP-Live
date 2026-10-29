using System.Text.Json;
using FlaxMcp.Configuration;
using FlaxMcp.Flax;
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
}
