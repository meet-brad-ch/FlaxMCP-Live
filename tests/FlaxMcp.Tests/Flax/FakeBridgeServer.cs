using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlaxMcp.Flax;
using Xunit;

namespace FlaxMcp.Tests.Flax;

/// <summary>One request that a fake bridge expects, and its answer.</summary>
/// <param name="Method">The request method that the fake bridge expects.</param>
/// <param name="Response">The JSON line that the fake bridge sends back.</param>
internal sealed record FakeBridgeStep(string Method, string Response);

/// <summary>
/// A bridge session for tests: a project folder and a sessions directory in the temp folder, and the handshake file
/// that points a <see cref="FlaxBridgeClient"/> at a fake bridge's pipe. Dispose deletes both folders.
/// </summary>
internal sealed class FakeBridgeSession : IDisposable
{
    /// <summary>Creates the project folder and the sessions directory.</summary>
    public FakeBridgeSession()
    {
        Directory.CreateDirectory(SessionsDirectory);
        Directory.CreateDirectory(ProjectFolder);
    }

    /// <summary>The directory that holds the handshake file.</summary>
    public string SessionsDirectory { get; } = Path.Combine(Path.GetTempPath(), "FlaxMcpTests_" + Guid.NewGuid().ToString("N"));

    /// <summary>The project folder of the session.</summary>
    public string ProjectFolder { get; } = Path.Combine(Path.GetTempPath(), "FlaxProject_" + Guid.NewGuid().ToString("N"));

    /// <summary>Deletes the project folder and the sessions directory.</summary>
    public void Dispose()
    {
        Directory.Delete(SessionsDirectory, recursive: true);
        Directory.Delete(ProjectFolder, recursive: true);
    }

    /// <summary>A new unique pipe name for a fake bridge.</summary>
    /// <returns>The pipe name.</returns>
    public static string NewPipeName()
    {
        return "FlaxMcpTests-" + Guid.NewGuid().ToString("N");
    }

    /// <summary>A client for the session.</summary>
    /// <returns>The client.</returns>
    public FlaxBridgeClient CreateClient()
    {
        return new FlaxBridgeClient(ProjectFolder, SessionsDirectory);
    }

    /// <summary>Writes the handshake file of an editor that runs as this test process.</summary>
    /// <param name="pipeName">The fake bridge's pipe name.</param>
    public void WriteHandshake(string pipeName)
    {
        WriteHandshake(pipeName, Environment.ProcessId);
    }

    /// <summary>Writes the handshake file with the current protocol version.</summary>
    /// <param name="pipeName">The fake bridge's pipe name.</param>
    /// <param name="pid">The editor's process id in the handshake.</param>
    public void WriteHandshake(string pipeName, int pid)
    {
        string handshakePath = Path.Combine(SessionsDirectory, ProjectHash(ProjectFolder) + ".json");
        string json = JsonSerializer.Serialize(
            new
            {
                pipeName,
                protocolVersion = FlaxBridgeClient.CurrentProtocolVersion,
                pid,
                pluginVersion = "1.2.3",
                engineBuild = 12000,
                startedUtc = DateTime.UtcNow,
            }
        );
        File.WriteAllText(handshakePath, json);
    }

    /// <summary>The handshake file's name for a project folder, as the bridge plugin computes it.</summary>
    /// <param name="projectFolder">The project folder.</param>
    /// <returns>The first 16 hex digits of the SHA-256 of the normalized folder path.</returns>
    private static string ProjectHash(string projectFolder)
    {
        string normalized = Path.GetFullPath(projectFolder).Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes)[..16];
    }
}

/// <summary>A fake Flax Editor bridge: a named pipe server that checks each request's method and sends a fixed answer.</summary>
internal static class FakeBridgeServer
{
    /// <summary>Answers one request.</summary>
    /// <param name="pipeName">The pipe name.</param>
    /// <param name="response">The JSON line to send back.</param>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <param name="method">The request method that the server expects.</param>
    /// <param name="expectedRequestText">Text that the request must contain, or null.</param>
    /// <returns>A task that completes after the answer.</returns>
#pragma warning disable CA1068 // Same parameter order as upstream's ServeResponseAsync in FlaxBridgeClientTests.cs, so tests read alike.
    public static async Task ServeResponseAsync(
        string pipeName,
        string response,
        CancellationToken cancellationToken,
        string method,
        string? expectedRequestText = null)
#pragma warning restore CA1068
    {
        await using NamedPipeServerStream pipe = new(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous
        );
        await pipe.WaitForConnectionAsync(cancellationToken);
        await AnswerAsync(pipe, method, response, expectedRequestText, cancellationToken);
    }

    /// <summary>Answers one request per step, in order.</summary>
    /// <param name="pipeName">The pipe name.</param>
    /// <param name="cancellationToken">Stops the server.</param>
    /// <param name="steps">The expected requests and their answers.</param>
    /// <returns>A task that completes after the last answer.</returns>
    public static async Task ServeSequenceAsync(
        string pipeName,
        CancellationToken cancellationToken,
        params FakeBridgeStep[] steps)
    {
        // The next instance is created before the current one closes: on Unix a pipe is a socket whose listener
        // goes away with its last instance, and a client reconnecting at once would be reset.
        NamedPipeServerStream pipe = CreateSequencePipe(pipeName);
        for (int i = 0; i < steps.Length; i++)
        {
            await pipe.WaitForConnectionAsync(cancellationToken);
            NamedPipeServerStream? next = null;
            if (i + 1 < steps.Length)
            {
                next = CreateSequencePipe(pipeName);
            }

            await using (pipe)
            {
                await AnswerAsync(pipe, steps[i].Method, steps[i].Response, null, cancellationToken);
            }

            pipe = next!;
        }
    }

    /// <summary>
    /// Serves the steps in order, then answers every later request with the last step's response until stopped: a
    /// client that polls until a state holds may need more answers than a fixed list (one failed poll restarts its wait).
    /// </summary>
    /// <param name="pipeName">The pipe name.</param>
    /// <param name="stop">Stops the server; the test cancels it after the client returned.</param>
    /// <param name="steps">The expected requests and their answers.</param>
    /// <returns>A task that completes when the server stopped.</returns>
    public static async Task ServeThenRepeatLastAsync(
        string pipeName,
        CancellationToken stop,
        params FakeBridgeStep[] steps)
    {
        NamedPipeServerStream pipe = CreateSequencePipe(pipeName);
        try
        {
            for (int i = 0; ; i++)
            {
                await pipe.WaitForConnectionAsync(stop);
                NamedPipeServerStream next = CreateSequencePipe(pipeName);
                FakeBridgeStep step = steps[Math.Min(i, steps.Length - 1)];
                await using (pipe)
                {
                    await AnswerAsync(pipe, step.Method, step.Response, null, stop);
                }

                pipe = next;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The test stopped the fake bridge after the client returned.
        }
        finally
        {
            await pipe.DisposeAsync();
        }
    }

    /// <summary>A pipe server instance that allows the next instance of the same name to exist beside it.</summary>
    /// <param name="pipeName">The pipe name.</param>
    /// <returns>The pipe server.</returns>
    private static NamedPipeServerStream CreateSequencePipe(string pipeName)
    {
        return new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous
        );
    }

    /// <summary>Reads one request from a connected pipe, checks it, and sends the answer.</summary>
    /// <param name="pipe">The connected pipe.</param>
    /// <param name="method">The request method that the server expects.</param>
    /// <param name="response">The JSON line to send back.</param>
    /// <param name="expectedRequestText">Text that the request must contain, or null.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>A task that completes after the answer.</returns>
    private static async Task AnswerAsync(
        NamedPipeServerStream pipe,
        string method,
        string response,
        string? expectedRequestText,
        CancellationToken cancellationToken)
    {
        using StreamReader reader = new(
            pipe,
            Encoding.UTF8,
            false,
            4096,
            leaveOpen: true
        );
        await using StreamWriter writer = new(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n",
        };
        string? request = await reader.ReadLineAsync(cancellationToken);
        Assert.Contains($"\"method\":\"{method}\"", request);
        if (expectedRequestText is not null)
        {
            Assert.Contains(expectedRequestText, request);
        }

        await writer.WriteLineAsync(response);
    }
}
