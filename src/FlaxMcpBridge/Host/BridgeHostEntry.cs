using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace FlaxMcpBridge.Host;

/// <summary>
/// The entry points the editor plugin calls by reflection. Only BCL types cross this boundary: the plugin lives in a
/// collectible load context and cannot share types with the host.
/// </summary>
/// <remarks>
/// The one static field is deliberate: the host is the process-lifetime object a reloaded plugin must find again,
/// and a static in the Default load context is the only thing that outlives the plugin.
/// </remarks>
public static class BridgeHostEntry
{
    /// <summary>Guards <see cref="_server"/>.</summary>
    private static readonly object Gate = new();

    /// <summary>The running host, or null.</summary>
    private static BridgeHostServer? _server;

    /// <summary>The editor's recent events: fed by the Flax adapter (Host/Flax), read by clients.</summary>
    public static BridgeEventRing Events { get; } = new(4096);

    /// <summary>Whether the host is running.</summary>
    public static bool IsStarted
    {
        get
        {
            lock (Gate)
            {
                return _server is not null;
            }
        }
    }

    /// <summary>Starts the host.</summary>
    /// <param name="optionsJson">JSON with projectFolder, sessionsDirectory, hostVersion and attachWaitSeconds.</param>
    /// <exception cref="InvalidOperationException">The host is already running.</exception>
    public static void Start(string optionsJson)
    {
        using var document = JsonDocument.Parse(optionsJson);
        var root = document.RootElement;
        var projectFolder = root.GetProperty("projectFolder").GetString()!;
        var pipeName = BridgeHostServer.PipeNameFor(projectFolder);
        var sessionsDirectory = root.GetProperty("sessionsDirectory").GetString()!;
        var handshakeFileName = BridgeHostServer.HandshakeFileNameFor(projectFolder);
        var handshakePath = Path.Combine(sessionsDirectory, handshakeFileName);
        var hostVersion = root.GetProperty("hostVersion").GetString()!;
        var attachWaitSeconds = root.GetProperty("attachWaitSeconds").GetDouble();
        var attachWait = TimeSpan.FromSeconds(attachWaitSeconds);
        var options = new BridgeHostOptions(projectFolder, pipeName, handshakePath, hostVersion, attachWait);
        lock (Gate)
        {
            if (_server is not null)
            {
                throw new InvalidOperationException("The bridge host is already running.");
            }

            _server = new BridgeHostServer(options, Events);
            _server.Start();
        }
    }

    /// <summary>Attaches a plugin generation.</summary>
    /// <param name="attachmentJson">JSON with generation, protocolVersion, pluginVersion and engineBuild.</param>
    /// <param name="dispatch">Answers a request line with a response line.</param>
    /// <exception cref="InvalidOperationException">The host is not running.</exception>
    public static void Attach(string attachmentJson, Func<string, Task<string>> dispatch)
    {
        using var document = JsonDocument.Parse(attachmentJson);
        var root = document.RootElement;
        var generation = root.GetProperty("generation").GetInt64();
        var protocolVersion = root.GetProperty("protocolVersion").GetInt32();
        var pluginVersion = root.GetProperty("pluginVersion").GetString()!;
        var engineBuild = root.GetProperty("engineBuild").GetInt32();
        var attachment = new BridgeHostAttachment(generation, protocolVersion, pluginVersion, engineBuild, dispatch);
        Server().Attach(attachment);
    }

    /// <summary>Detaches a plugin generation.</summary>
    /// <param name="generation">The generation going away.</param>
    /// <exception cref="InvalidOperationException">The host is not running.</exception>
    public static void Detach(long generation) => Server().Detach(generation);

    /// <summary>Stops the host and removes its handshake file (editor exit, or a newer host taking over).</summary>
    public static void Shutdown()
    {
        lock (Gate)
        {
            _server?.Dispose();
            _server = null;
        }
    }

    /// <summary>The running host.</summary>
    /// <returns>The host.</returns>
    /// <exception cref="InvalidOperationException">The host is not running.</exception>
    private static BridgeHostServer Server()
    {
        lock (Gate)
        {
            return _server ?? throw new InvalidOperationException("The bridge host is not running.");
        }
    }
}
