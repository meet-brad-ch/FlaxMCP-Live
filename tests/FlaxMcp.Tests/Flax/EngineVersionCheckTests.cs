using FlaxMcp.Flax;
using ModelContextProtocol;
using Xunit;

namespace FlaxMcp.Tests.Flax;

/// <summary>An engine older than the project's MinEngineVersion is refused with both versions in the message.</summary>
public sealed class EngineVersionCheckTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "FlaxMcpTests_" + Guid.NewGuid().ToString("N"));

    public EngineVersionCheckTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>A 1.12 engine for a 1.13 project: the run that exited with -1 and no log.</summary>
    [Fact]
    public void Require_WithAnOlderEngine_ThrowsWithBothVersions()
    {
        string engine = Engine(1, 12, 6912);
        string project = Project("1.13.7012");

        McpException error = Assert.Throws<McpException>(() => EngineVersionCheck.Require(engine, project));

        Assert.Contains("1.12.6912", error.Message);
        Assert.Contains("1.13.7012", error.Message);
        Assert.Contains("FLAX_ENGINE_PATH", error.Message);
    }

    /// <summary>The same version and a newer one are accepted.</summary>
    /// <param name="minor">The engine's minor version.</param>
    /// <param name="build">The engine's build number.</param>
    [Theory]
    [InlineData(13, 7012)]
    [InlineData(13, 7100)]
    [InlineData(14, 1)]
    public void Require_WithTheSameOrANewerEngine_Passes(int minor, int build)
    {
        EngineVersionCheck.Require(Engine(1, minor, build), Project("1.13.7012"));
    }

    /// <summary>A project without MinEngineVersion accepts any engine, also one whose version cannot be read.</summary>
    [Fact]
    public void Require_WithoutMinEngineVersion_Passes()
    {
        string project = Path.Combine(_tempDir, "Game.flaxproj");
        File.WriteAllText(project, "{\"Name\": \"Game\"}");

        EngineVersionCheck.Require(_tempDir, project);
    }

    /// <summary>A MinEngineVersion that is not a version is named in the message.</summary>
    [Fact]
    public void ReadMinimum_WithABadVersion_Throws()
    {
        McpException error = Assert.Throws<McpException>(() => EngineVersionCheck.ReadMinimum(Project("latest")));

        Assert.Contains("latest", error.Message);
    }

    /// <summary>An engine file without Major, Minor and Build is refused.</summary>
    [Fact]
    public void ReadEngineVersion_WithoutAVersion_Throws()
    {
        string file = Path.Combine(_tempDir, "Flax.flaxproj");
        File.WriteAllText(file, "{\"Name\": \"Flax\"}");

        McpException error = Assert.Throws<McpException>(() => EngineVersionCheck.ReadEngineVersion(file));

        Assert.Contains("Version", error.Message);
    }

    /// <summary>Flax's own files have trailing commas and comments: they must still be read.</summary>
    [Fact]
    public void Require_WithTrailingCommasAndComments_ReadsBothFiles()
    {
        string root = Path.Combine(_tempDir, "engine-trailing");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Flax.flaxproj"),
            "{\n  \"Name\": \"Flax\",\n  \"Version\": {\n    \"Major\": 1,\n    \"Minor\": 12,\n    \"Build\": 6912,\n  },\n}");
        string project = Path.Combine(_tempDir, "Trailing.flaxproj");
        File.WriteAllText(project, "{\n  // the game\n  \"Name\": \"Game\",\n  \"MinEngineVersion\": \"1.13.7012\",\n}");

        McpException error = Assert.Throws<McpException>(() => EngineVersionCheck.Require(root, project));

        Assert.Contains("1.12.6912", error.Message);
    }

    private string Engine(int major, int minor, int build)
    {
        string root = Path.Combine(_tempDir, $"engine-{major}.{minor}.{build}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Flax.flaxproj"),
            $"{{\"Name\": \"Flax\", \"Version\": {{\"Major\": {major}, \"Minor\": {minor}, \"Revision\": 0, \"Build\": {build}}}}}");
        return root;
    }

    private string Project(string minEngineVersion)
    {
        string file = Path.Combine(_tempDir, $"Game-{Guid.NewGuid():N}.flaxproj");
        File.WriteAllText(file, $"{{\"Name\": \"Game\", \"MinEngineVersion\": \"{minEngineVersion}\"}}");
        return file;
    }
}
