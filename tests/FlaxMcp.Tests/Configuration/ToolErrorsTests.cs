using System.IO.Pipelines;
using FlaxMcp.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace FlaxMcp.Tests.Configuration;

/// <summary>
/// Tests of the call-tool filter through a real MCP client and server: a failed tool call keeps its reason. Without
/// the filter the SDK shows only "An error occurred invoking 'tool'." for every exception but McpException.
/// </summary>
public sealed class ToolErrorsTests
{
    /// <summary>The reason of an ArgumentException reaches the client, with the exception's type.</summary>
    [Fact]
    public async Task CallTool_ArgumentException_TheClientReadsTheReason()
    {
        var text = await CallFailingTool(withFilter: true, nameof(FailingTools.BadArgument));

        Assert.Equal("An error occurred invoking 'BadArgument': ArgumentException: The path is empty.", text);
    }

    /// <summary>An McpException keeps its own message: the filter does not wrap it again.</summary>
    [Fact]
    public async Task CallTool_McpException_TheMessageIsNotWrappedAgain()
    {
        var text = await CallFailingTool(withFilter: true, nameof(FailingTools.McpFailure));

        Assert.Equal("An error occurred invoking 'McpFailure': The editor is not connected.", text);
    }

    /// <summary>The check sees the failure: without the filter the SDK drops the reason.</summary>
    [Fact]
    public async Task CallTool_WithoutTheFilter_TheReasonIsLost()
    {
        var text = await CallFailingTool(withFilter: false, nameof(FailingTools.BadArgument));

        Assert.Equal("An error occurred invoking 'BadArgument'.", text);
    }

    /// <summary>Calls a failing tool through an in-memory MCP client and server.</summary>
    /// <param name="withFilter">Whether the server has the <see cref="ToolErrors"/> filter.</param>
    /// <param name="toolName">The tool to call, a method of <see cref="FailingTools"/>.</param>
    /// <returns>The text of the error result the client receives.</returns>
    private static async Task<string> CallFailingTool(bool withFilter, string toolName)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();
        var services = new ServiceCollection();
        services.AddLogging();
        var builder = services
                      .AddMcpServer()
                      .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())
                      .WithTools<FailingTools>();
        if (withFilter)
        {
            builder.WithRequestFilters(filters => filters.AddCallToolFilter(ToolErrors.Filter));
        }

        await using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<McpServer>();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var serverTask = server.RunAsync(stop.Token);

        var transport = new StreamClientTransport(clientToServer.Writer.AsStream(), serverToClient.Reader.AsStream());
        await using (var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken))
        {
            var result = await client.CallToolAsync(toolName, cancellationToken: cancellationToken);
            Assert.True(result.IsError);
            var block = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
            await stop.CancelAsync();
            await clientToServer.Writer.CompleteAsync();
            await serverToClient.Writer.CompleteAsync();
            await serverTask;
            return block.Text;
        }
    }

    /// <summary>Tools that fail the way the fork's tools do.</summary>
    [McpServerToolType]
    public sealed class FailingTools
    {
        /// <summary>Fails with an ArgumentException, as a tool that checks its arguments.</summary>
        /// <returns>Never returns.</returns>
        /// <exception cref="ArgumentException">Always.</exception>
        [McpServerTool(Name = nameof(BadArgument))]
        public string BadArgument() => throw new ArgumentException("The path is empty.");

        /// <summary>Fails with an McpException, as the bridge client.</summary>
        /// <returns>Never returns.</returns>
        /// <exception cref="McpException">Always.</exception>
        [McpServerTool(Name = nameof(McpFailure))]
        public string McpFailure() => throw new McpException("The editor is not connected.");
    }
}
