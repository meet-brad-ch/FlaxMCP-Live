using System.ComponentModel;
using System.Reflection;
using FlaxMcp.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Xunit;

namespace FlaxMcp.Tests.Configuration;

/// <summary>Tests of the fork's descriptions that replace the descriptions of upstream tools.</summary>
public sealed class ToolDescriptionsTests
{
    /// <summary>Registers every tool, the code-execution tools included.</summary>
    /// <returns>The registered tools by name.</returns>
    private static Dictionary<string, McpServerTool> RegisterAll()
    {
        var options = new FlaxMcpOptions { AllowCodeExecution = true };
        var services = new ServiceCollection();
        ToolRegistration.AddTools(services, options);
        using var provider = services.BuildServiceProvider();
        var tools = new Dictionary<string, McpServerTool>();
        foreach (var tool in provider.GetServices<McpServerTool>())
        {
            tools[tool.ProtocolTool.Name] = tool;
        }

        return tools;
    }

    /// <summary>Every override names a registered tool, so a tool that upstream renames or removes fails here.</summary>
    [Fact]
    public void Overrides_NameRegisteredTools_ARenamedToolFails()
    {
        var tools = RegisterAll();

        foreach (var name in ToolDescriptions.Overrides.Keys)
        {
            Assert.True(tools.ContainsKey(name), $"The override '{name}' names no registered tool.");
        }
    }

    /// <summary>A tool with an override reports the override in the tool list.</summary>
    [Fact]
    public void RegisteredTool_WithOverride_ReportsTheOverride()
    {
        var tools = RegisterAll();

        foreach (var (name, description) in ToolDescriptions.Overrides)
        {
            Assert.Equal(description, tools[name].ProtocolTool.Description);
        }
    }

    /// <summary>A tool without an override keeps the text of its Description attribute.</summary>
    [Fact]
    public void RegisteredTool_WithoutOverride_KeepsItsAttributeText()
    {
        var tools = RegisterAll();
        var method = typeof(FlaxMcp.Tools.ServerInfoTool).GetMethod(nameof(FlaxMcp.Tools.ServerInfoTool.GetServerInfo))!;
        var attributeText = method.GetCustomAttribute<DescriptionAttribute>()!.Description;

        Assert.False(ToolDescriptions.Overrides.ContainsKey("server_info"));
        Assert.Equal(attributeText, tools["server_info"].ProtocolTool.Description);
    }
}
