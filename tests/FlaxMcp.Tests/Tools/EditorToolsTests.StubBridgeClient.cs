using FlaxMcp.Flax;

namespace FlaxMcp.Tests.Tools;

public sealed partial class EditorToolsTests
{
    /// <summary>The bridge fake's members for the bridge requests of the live fork (compile, open scene, host status).</summary>
    internal sealed partial class StubBridgeClient
    {
        /// <summary>The result that <see cref="CompileAsync"/> returns.</summary>
        public FlaxEditorCompileResult CompileResult { get; set; } = new("unchanged", 0, [], [], 0);

        /// <summary>The result that <see cref="OpenSceneAsync"/> returns.</summary>
        public FlaxEditorOpenSceneResult OpenSceneResult { get; set; } = new(0, "", "", []);

        /// <summary>The path of the last <see cref="OpenSceneAsync"/> call; null before the first one.</summary>
        public string? ScenePath { get; private set; }

        /// <summary>How many times <see cref="CompileAsync"/> was called.</summary>
        public int CompileCalls { get; private set; }

        /// <summary>The result that <see cref="GetHostStatusAsync"/> returns.</summary>
        public FlaxBridgeHostStatus HostStatus { get; set; } =
            new("v", DateTime.UnixEpoch, 1, true, true, null, 1, 0, 1, 1, false, 0, 0, 0, 0, 0);

        /// <summary>The collect argument of the last <see cref="GetHostStatusAsync"/> call; null before the first one.</summary>
        public bool? HostStatusCollect { get; private set; }

        /// <summary>Counts the call and returns <see cref="CompileResult"/>.</summary>
        /// <param name="progress">Not used.</param>
        /// <param name="cancellationToken">Not used.</param>
        /// <returns><see cref="CompileResult"/>.</returns>
        public Task<FlaxEditorCompileResult> CompileAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CompileCalls++;
            return Task.FromResult(CompileResult);
        }

        /// <summary>Keeps the path and returns <see cref="OpenSceneResult"/>.</summary>
        /// <param name="path">The scene path.</param>
        /// <param name="cancellationToken">Not used.</param>
        /// <returns><see cref="OpenSceneResult"/>.</returns>
        public Task<FlaxEditorOpenSceneResult> OpenSceneAsync(string path, CancellationToken cancellationToken = default)
        {
            ScenePath = path;
            return Task.FromResult(OpenSceneResult);
        }

        /// <summary>Keeps the collect argument and returns <see cref="HostStatus"/>.</summary>
        /// <param name="collect">Whether a garbage collection was asked for.</param>
        /// <param name="cancellationToken">Not used.</param>
        /// <returns><see cref="HostStatus"/>.</returns>
        public Task<FlaxBridgeHostStatus> GetHostStatusAsync(bool collect, CancellationToken cancellationToken = default)
        {
            HostStatusCollect = collect;
            return Task.FromResult(HostStatus);
        }
    }
}
