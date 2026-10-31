using System.ComponentModel;
using FlaxMcp.Flax;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace FlaxMcp.Tools;

/// <summary>
/// Pictures that a camera renders offscreen. Unlike editor_screenshot, they do not read an editor window, so a covered,
/// small or minimised editor gives the same picture, in play mode and in edit mode.
/// </summary>
[McpServerToolType]
public sealed class EditorScreenshotTools
{
    /// <summary>The smallest picture side, in pixels.</summary>
    internal const int MinSize = 16;

    /// <summary>The largest picture side, in pixels.</summary>
    internal const int MaxSize = 8192;

    /// <summary>The most warm-up frames.</summary>
    internal const int MaxWarmUpFrames = 60;

    /// <summary>The bridge client that renders the pictures.</summary>
    private readonly IFlaxScreenshotClient _client;

    /// <summary>Creates the tools.</summary>
    /// <param name="client">Renders the pictures through the bridge.</param>
    public EditorScreenshotTools(IFlaxScreenshotClient client)
    {
        _client = client;
    }

    /// <summary>Renders a camera offscreen and saves the picture as a PNG.</summary>
    /// <param name="path">The output PNG, absolute or relative to the current folder. Its folder must exist.</param>
    /// <param name="width">The picture's width, in pixels.</param>
    /// <param name="height">The picture's height, in pixels.</param>
    /// <param name="cameraId">A Camera actor's id. Empty: the main camera in play mode, else the editor viewport's view.</param>
    /// <param name="warmUpFrames">The frames the camera renders before the capture.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The written file and what was rendered.</returns>
    /// <exception cref="McpException">The path is empty or not a .png file, its folder does not exist, or a value is out of range.</exception>
    [McpServerTool(Name = "editor_camera_screenshot", UseStructuredContent = true)]
    [Description(
        "Renders a camera into its own render target and saves a PNG. It does not read an editor window, so it works " +
        "with the editor covered, small or minimised, in play mode and in edit mode. Camera: the given Camera actor. " +
        "Without one: the main camera in play mode, else the editor viewport's view. The picture shows the 3D scene " +
        "with the game's post effects, without editor or screen-space UI. Not available in headless mode. Replaces an " +
        "existing file. Read the PNG to see it."
    )]
    public Task<FlaxCameraScreenshot> CaptureCameraScreenshotAsync(
        [Description("Absolute or current-working-directory-relative output path ending in .png. The folder must exist.")]
        string path,
        [Description("Picture width in pixels, 16..8192.")] int width = 1920,
        [Description("Picture height in pixels, 16..8192.")] int height = 1080,
        [Description("A Camera actor's id (from editor_scene_graph or editor_find_actors). Empty: the main camera in " +
                     "play mode, else the editor viewport's view.")]
        string? cameraId = null,
        [Description("Frames the camera renders before the capture, 1..60, so that auto exposure settles.")]
        int warmUpFrames = 8,
        CancellationToken cancellationToken = default)
    {
        // McpException carries its message to the client. Other exceptions reach it without their text.
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new McpException("path is empty: give an output path ending in .png.");
        }

        var resolvedPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(resolvedPath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpException($"Screenshot output path must use the .png extension: {resolvedPath}");
        }

        var directory = Path.GetDirectoryName(resolvedPath)!;
        if (!Directory.Exists(directory))
        {
            throw new McpException($"Screenshot output directory does not exist: {directory}");
        }

        CheckRange(width, MinSize, MaxSize, nameof(width));
        CheckRange(height, MinSize, MaxSize, nameof(height));
        CheckRange(warmUpFrames, 1, MaxWarmUpFrames, nameof(warmUpFrames));
        string? camera = null;
        if (!string.IsNullOrWhiteSpace(cameraId))
        {
            camera = cameraId.Trim();
        }

        return _client.CaptureCameraScreenshotAsync(resolvedPath, width, height, camera, warmUpFrames, cancellationToken);
    }

    /// <summary>Checks that a value lies in a range.</summary>
    /// <param name="value">The value.</param>
    /// <param name="min">The smallest allowed value.</param>
    /// <param name="max">The largest allowed value.</param>
    /// <param name="name">The parameter's name, for the message.</param>
    /// <exception cref="McpException">The value is out of range.</exception>
    private static void CheckRange(int value, int min, int max, string name)
    {
        if (value < min || value > max)
        {
            throw new McpException($"{name} {value} is out of range: it must be {min}..{max}.");
        }
    }
}
