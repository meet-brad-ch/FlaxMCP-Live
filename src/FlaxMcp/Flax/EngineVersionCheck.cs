using System.Text.Json;
using ModelContextProtocol;

namespace FlaxMcp.Flax;

/// <summary>
/// Refuses an engine older than the project's <c>MinEngineVersion</c>. Such an editor exits at once with code -1 and
/// writes no project log, so a headless run would report only "exit code -1". The engine comes from
/// <c>FLAX_ENGINE_PATH</c> or the Flax Launcher's <c>Versions.txt</c>, which can name an older install than the project
/// needs.
/// </summary>
public static class EngineVersionCheck
{
    /// <summary>Flax writes its project files with trailing commas, and Flax reads comments.</summary>
    private static readonly JsonDocumentOptions FlaxJson = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Throws when the engine is older than the project's <c>MinEngineVersion</c>.</summary>
    /// <param name="enginePath">The engine's root folder, which holds <c>Flax.flaxproj</c>.</param>
    /// <param name="projectFile">The project's <c>.flaxproj</c> file.</param>
    /// <exception cref="McpException">
    /// The engine is older than the project needs, or a version in either file cannot be read.
    /// </exception>
    public static void Require(string enginePath, string projectFile)
    {
        Version? minimum = ReadMinimum(projectFile);
        if (minimum is null)
        {
            // A project without MinEngineVersion accepts any engine.
            return;
        }

        Version engine = ReadEngineVersion(Path.Combine(enginePath, "Flax.flaxproj"));
        if (engine < minimum)
        {
            throw new McpException(
                $"The Flax engine at '{enginePath}' is {engine}, older than the project's MinEngineVersion {minimum}: " +
                $"its editor exits at once (code -1) without building. Set FLAX_ENGINE_PATH to an engine of " +
                $"{minimum} or later.");
        }
    }

    /// <summary>The project's <c>MinEngineVersion</c> ("1.13.7012"), or null when the project names none.</summary>
    /// <param name="projectFile">The project's <c>.flaxproj</c> file.</param>
    /// <returns>The minimum version as major.minor.build.</returns>
    /// <exception cref="McpException">The value is not a version.</exception>
    internal static Version? ReadMinimum(string projectFile)
    {
        using JsonDocument project = JsonDocument.Parse(File.ReadAllText(projectFile), FlaxJson);
        if (!project.RootElement.TryGetProperty("MinEngineVersion", out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        string text = value.GetString() ?? "";
        return Version.TryParse(text, out Version? minimum) ?
            minimum :
            throw new McpException($"MinEngineVersion '{text}' in '{projectFile}' is not a version.");
    }

    /// <summary>The engine's version from its <c>Flax.flaxproj</c>: major.minor.build.</summary>
    /// <param name="engineProject">The engine's <c>Flax.flaxproj</c>.</param>
    /// <returns>The version as major.minor.build (the same parts as a project's MinEngineVersion).</returns>
    /// <exception cref="McpException">The file has no Version with Major, Minor and Build numbers.</exception>
    internal static Version ReadEngineVersion(string engineProject)
    {
        using JsonDocument engine = JsonDocument.Parse(File.ReadAllText(engineProject), FlaxJson);
        if (engine.RootElement.TryGetProperty("Version", out JsonElement version) &&
            version.ValueKind == JsonValueKind.Object &&
            version.TryGetProperty("Major", out JsonElement major) &&
            version.TryGetProperty("Minor", out JsonElement minor) &&
            version.TryGetProperty("Build", out JsonElement build))
        {
            return new Version(major.GetInt32(), minor.GetInt32(), build.GetInt32());
        }

        throw new McpException($"'{engineProject}' has no Version with Major, Minor and Build.");
    }
}
