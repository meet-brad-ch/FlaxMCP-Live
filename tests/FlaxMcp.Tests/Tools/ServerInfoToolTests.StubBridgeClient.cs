using FlaxMcp.Flax;

namespace FlaxMcp.Tests.Tools;

public partial class ServerInfoToolTests
{
    /// <summary>The bridge fake's members for the bridge requests of the live fork; the server info tests do not use them.</summary>
    private sealed partial class StubBridgeClient
    {
        /// <summary>Not used by these tests.</summary>
        /// <param name="progress">Not used.</param>
        /// <param name="cancellationToken">Not used.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="NotSupportedException">Always.</exception>
        public Task<FlaxEditorCompileResult> CompileAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <summary>Not used by these tests.</summary>
        /// <param name="path">Not used.</param>
        /// <param name="cancellationToken">Not used.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="NotSupportedException">Always.</exception>
        public Task<FlaxEditorOpenSceneResult> OpenSceneAsync(string path, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        /// <summary>Not used by these tests.</summary>
        /// <param name="collect">Not used.</param>
        /// <param name="cancellationToken">Not used.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="NotSupportedException">Always.</exception>
        public Task<FlaxBridgeHostStatus> GetHostStatusAsync(bool collect, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
