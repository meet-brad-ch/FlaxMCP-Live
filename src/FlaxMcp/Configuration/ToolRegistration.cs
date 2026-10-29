using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace FlaxMcp.Configuration;

public static class ToolRegistration
{
    public static int AddTools(IServiceCollection services, FlaxMcpOptions options)
    {
        var registered = 0;

        foreach (var toolType in Toolsets.Resolve(options.Toolsets))
        {
            foreach (var method in SelectMethods(toolType, options))
            {
                var capturedType = toolType;
                var capturedMethod = method;
                services.AddSingleton<McpServerTool>(serviceProvider => McpServerTool.Create(
                        capturedMethod,
                        _ => ActivatorUtilities.CreateInstance(serviceProvider, capturedType),
                        new McpServerToolCreateOptions { Services = serviceProvider, SerializerOptions = ToolSerialization.Options }
                    )
                );
                registered++;
            }
        }

        return registered;
    }

    private static IEnumerable<MethodInfo> SelectMethods(Type toolType, FlaxMcpOptions options)
    {
        return toolType
               .GetMethods(BindingFlags.Public | BindingFlags.Instance)
               .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is { } attribute &&
                   (!options.ReadOnly || attribute.ReadOnly) &&
                   (method.GetCustomAttribute<RequiresCodeExecutionAttribute>() is null ||
                       options.AllowCodeExecution && !options.ReadOnly)
               );
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class RequiresCodeExecutionAttribute : Attribute;
