using System.Reflection;
using ModelContextProtocol.Protocol;

namespace FlaxMcp;

/// <summary>
/// The server's name and version. The MCP handshake (initialize) and the server_info tool both report these values, so
/// a client sees one identity.
/// </summary>
internal static class ServerIdentity
{
    /// <summary>The server's name.</summary>
    public const string Name = "FlaxMCP-Live";

    /// <summary>The release version, for example "1.0.0", without the source revision.</summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>The identity for the MCP handshake.</summary>
    /// <returns>The server's name and version.</returns>
    public static Implementation Create()
    {
        return new Implementation { Name = Name, Version = Version };
    }

    /// <summary>Reads the version from the assembly's informational version.</summary>
    /// <returns>The version before the "+revision" suffix, else the assembly version, else "0.0.0".</returns>
    private static string ReadVersion()
    {
        var assembly = typeof(ServerIdentity).Assembly;
        string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (informational is not null)
        {
            // The build appends "+<git revision>". The release version is the part before it.
            return informational.Split('+')[0];
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
