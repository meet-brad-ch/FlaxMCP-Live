using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace FlaxMcp.Configuration;

/// <summary>
/// The call-tool filter that gives the client the reason of every failed tool call. The SDK shows the message of an
/// <see cref="McpException"/> only; for any other exception the client reads "An error occurred invoking 'tool'."
/// and nothing more. The tools and the bridge client throw <see cref="ArgumentException"/>,
/// <see cref="InvalidOperationException"/>, <see cref="KeyNotFoundException"/>, <see cref="IOException"/> and others
/// with a message that names the cause, so the filter passes each of them on as an <see cref="McpException"/> with
/// the exception's type and message.
/// </summary>
internal static class ToolErrors
{
    /// <summary>Wraps the tool-call handler so that a failed call keeps its reason.</summary>
    /// <param name="next">The next handler: the tool call itself.</param>
    /// <returns>The handler with the filter.</returns>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next)
    {
        return async (request, cancellationToken) =>
        {
            try
            {
                return await next(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (Passes(exception, cancellationToken))
            {
                throw new McpException(Reason(exception), exception);
            }
        };
    }

    /// <summary>The text the client reads for an exception: its type and its message.</summary>
    /// <param name="exception">The exception of the tool call.</param>
    /// <returns>For example <c>ArgumentException: The path is empty.</c></returns>
    public static string Reason(Exception exception)
    {
        var type = exception.GetType().Name;
        return $"{type}: {exception.Message}";
    }

    /// <summary>
    /// Whether the filter passes the exception on with its reason. The SDK already shows an
    /// <see cref="McpException"/>, a cancelled call stays a cancellation, and an <see cref="InputRequiredException"/>
    /// is the SDK's control flow, not a failure.
    /// </summary>
    /// <param name="exception">The exception of the tool call.</param>
    /// <param name="cancellationToken">The call's cancellation token.</param>
    /// <returns>True when the exception is turned into an <see cref="McpException"/>.</returns>
    private static bool Passes(Exception exception, CancellationToken cancellationToken)
    {
        if (exception is McpException or InputRequiredException)
        {
            return false;
        }

        var cancelled = exception is OperationCanceledException && cancellationToken.IsCancellationRequested;
        return !cancelled;
    }
}
