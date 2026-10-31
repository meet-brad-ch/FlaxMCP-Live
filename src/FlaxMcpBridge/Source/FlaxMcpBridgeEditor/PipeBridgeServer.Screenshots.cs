using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FlaxEditor.Windows;
using FlaxEngine;

namespace FlaxMcpBridge.Editor;

/// <summary>
/// The camera screenshot: a camera renders into its own render target, and the engine saves the target as a PNG.
/// No editor window takes part, so the picture is the same whether the editor is visible, covered or minimised.
/// </summary>
internal sealed partial class PipeBridgeServer
{
    /// <summary>The picture's width when the request gives none, in pixels.</summary>
    private const int DefaultScreenshotWidth = 1920;

    /// <summary>The picture's height when the request gives none, in pixels.</summary>
    private const int DefaultScreenshotHeight = 1080;

    /// <summary>The warm-up frames when the request gives none.</summary>
    private const int DefaultWarmUpFrames = 8;

    /// <summary>The smallest picture side, in pixels.</summary>
    private const int MinScreenshotSize = 16;

    /// <summary>The largest picture side, in pixels.</summary>
    private const int MaxScreenshotSize = 8192;

    /// <summary>The most warm-up frames: about one second at 60 frames per second.</summary>
    private const int MaxWarmUpFrames = 60;

    /// <summary>How often the capture checks the frame count and the file, in milliseconds.</summary>
    private const int ScreenshotPollMilliseconds = 20;

    /// <summary>How often the capture checks the PNG file's size, in milliseconds.</summary>
    private const int ScreenshotFilePollMilliseconds = 60;

    /// <summary>How long the warm-up frames may take, in milliseconds. With the file wait it stays under the 5 s request limit.</summary>
    private const int ScreenshotFramesTimeoutMilliseconds = 2500;

    /// <summary>How long the engine may take to write the file after the capture request, in milliseconds.</summary>
    private const int ScreenshotFileTimeoutMilliseconds = 2000;

    /// <summary>The render target and the task of one camera screenshot, with the camera it renders.</summary>
    private sealed class CameraShot
    {
        /// <summary>The picture's render target.</summary>
        public GPUTexture Output = null!;

        /// <summary>The scene render task that draws into <see cref="Output"/>.</summary>
        public SceneRenderTask Task = null!;

        /// <summary>A camera made for this picture from the editor viewport's view. Null for a scene camera.</summary>
        public Camera? TemporaryCamera;

        /// <summary>What the picture shows: the camera actor's name, "main camera" or "editor viewport".</summary>
        public string Source = "";

        /// <summary>The engine frame at which the task started to render.</summary>
        public ulong StartFrame;
    }

    /// <summary>Renders a camera offscreen and saves the picture as a PNG.</summary>
    /// <param name="path">The PNG file, absolute. Its folder exists. An existing file is replaced.</param>
    /// <param name="width">The picture's width, in pixels.</param>
    /// <param name="height">The picture's height, in pixels.</param>
    /// <param name="cameraId">A Camera actor's id. Null: the main camera in play mode, else the editor viewport's view.</param>
    /// <param name="warmUpFrames">Frames the task renders before the capture, so that auto exposure settles.</param>
    /// <returns>The file, its size, the picture size, what was rendered and whether the editor was in play mode.</returns>
    private static async Task<object> CaptureCameraScreenshotAsync(
        string path, int width, int height, string? cameraId, int warmUpFrames)
    {
        CheckScreenshotRequest(path, width, height, warmUpFrames);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        var shot = (CameraShot)await InvokeOnUpdateAsync(() => StartCameraShot(width, height, cameraId));
        try
        {
            await WaitForFramesAsync(shot.StartFrame + (ulong)warmUpFrames);
            bool isPlayMode = (bool)await InvokeOnUpdateAsync(() =>
            {
                Screenshot.Capture(shot.Output, path);
                return FlaxEditor.Editor.IsPlayMode;
            });
            long bytes = await WaitForFileAsync(path);
            return new { path, bytes, width, height, camera = shot.Source, isPlayMode };
        }
        finally
        {
            await InvokeOnUpdateAsync(() => StopCameraShot(shot));
        }
    }

    /// <summary>Checks the request before anything is created.</summary>
    /// <param name="path">The PNG file.</param>
    /// <param name="width">The picture's width.</param>
    /// <param name="height">The picture's height.</param>
    /// <param name="warmUpFrames">The warm-up frames.</param>
    /// <exception cref="ArgumentException">A value is out of range, or the path is not a .png file.</exception>
    /// <exception cref="InvalidOperationException">The editor runs headless, so there is no GPU to render with.</exception>
    private static void CheckScreenshotRequest(string path, int width, int height, int warmUpFrames)
    {
        if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"The screenshot path '{path}' must end in .png.");
        }

        if (width < MinScreenshotSize || width > MaxScreenshotSize || height < MinScreenshotSize || height > MaxScreenshotSize)
        {
            throw new ArgumentException(
                $"The picture size {width}x{height} is out of range ({MinScreenshotSize}..{MaxScreenshotSize} per side).");
        }

        if (warmUpFrames < 1 || warmUpFrames > MaxWarmUpFrames)
        {
            throw new ArgumentException($"warmUpFrames {warmUpFrames} is out of range (1..{MaxWarmUpFrames}).");
        }

        if (Engine.IsHeadless)
        {
            throw new InvalidOperationException("A camera screenshot needs a GPU, and a headless editor has none.");
        }
    }

    /// <summary>Creates the render target and the render task, and starts the rendering. Main thread only.</summary>
    /// <param name="width">The picture's width.</param>
    /// <param name="height">The picture's height.</param>
    /// <param name="cameraId">The Camera actor's id, or null.</param>
    /// <returns>The started shot.</returns>
    /// <exception cref="InvalidOperationException">The render target cannot be created.</exception>
    private static CameraShot StartCameraShot(int width, int height, string? cameraId)
    {
        var shot = new CameraShot();
        Camera camera = ResolveScreenshotCamera(cameraId, shot);
        shot.Output = new GPUTexture();
        GPUTextureDescription description = GPUTextureDescription.New2D(width, height, PixelFormat.R8G8B8A8_UNorm);
        if (shot.Output.Init(ref description))
        {
            DestroyCameraShot(shot);
            throw new InvalidOperationException($"Cannot create a {width}x{height} render target for the screenshot.");
        }

        // The game's own view flags, so the picture looks as the game does (post effects, sky, shadows).
        shot.Task = new SceneRenderTask
        {
            Order = -100,
            Camera = camera,
            Output = shot.Output,
            ViewFlags = ViewFlags.DefaultGame,
            Enabled = true,
        };
        shot.StartFrame = Engine.FrameCount;
        return shot;
    }

    /// <summary>Picks the camera: the given actor, the main camera in play mode, or the editor viewport's view.</summary>
    /// <param name="cameraId">The Camera actor's id, or null.</param>
    /// <param name="shot">Gets the source name and, for the viewport, the temporary camera.</param>
    /// <returns>The camera to render.</returns>
    /// <exception cref="ArgumentException">The actor is not a Camera.</exception>
    /// <exception cref="InvalidOperationException">No camera is given, there is no main camera, and no editor viewport is open.</exception>
    private static Camera ResolveScreenshotCamera(string? cameraId, CameraShot shot)
    {
        if (!string.IsNullOrWhiteSpace(cameraId))
        {
            Actor actor = FindActor(cameraId);
            Camera given = actor as Camera ??
                throw new ArgumentException($"Actor '{actor.Name}' ({cameraId}) is a {actor.GetType().Name}, not a Camera.");
            shot.Source = given.Name;
            return given;
        }

        if (FlaxEditor.Editor.IsPlayMode && Camera.MainCamera is { } main)
        {
            shot.Source = "main camera";
            return main;
        }

        // In edit mode there is no game camera running: the editor viewport's view stands in for it.
        EditGameWindow window = FlaxEditor.Editor.Instance.Windows.Windows.OfType<EditGameWindow>().FirstOrDefault() ??
            throw new InvalidOperationException("No camera was given, there is no main camera, and no editor viewport is open.");
        var viewport = window.Viewport;
        shot.TemporaryCamera = new Camera
        {
            Position = viewport.ViewPosition,
            Orientation = viewport.ViewOrientation,
            FieldOfView = viewport.FieldOfView,
            NearPlane = viewport.NearPlane,
            FarPlane = viewport.FarPlane,
        };
        shot.Source = "editor viewport";
        return shot.TemporaryCamera;
    }

    /// <summary>Waits until the engine has drawn a frame number.</summary>
    /// <param name="frame">The frame number to reach.</param>
    /// <exception cref="TimeoutException">The engine did not draw the frames in time.</exception>
    private static async Task WaitForFramesAsync(ulong frame)
    {
        for (int waited = 0; waited < ScreenshotFramesTimeoutMilliseconds; waited += ScreenshotPollMilliseconds)
        {
            bool reached = (bool)await InvokeOnUpdateAsync(() => Engine.FrameCount >= frame);
            if (reached)
            {
                return;
            }

            await Task.Delay(ScreenshotPollMilliseconds);
        }

        throw new TimeoutException(
            $"The engine did not draw the screenshot's warm-up frames within {ScreenshotFramesTimeoutMilliseconds} ms.");
    }

    /// <summary>Waits until the engine has written the PNG file and closed it.</summary>
    /// <param name="path">The PNG file.</param>
    /// <returns>The file's size, in bytes.</returns>
    /// <exception cref="IOException">The file was not written and closed in time.</exception>
    private static async Task<long> WaitForFileAsync(string path)
    {
        for (int waited = 0; waited < ScreenshotFileTimeoutMilliseconds; waited += ScreenshotFilePollMilliseconds)
        {
            // The engine reads the target back from the GPU and writes the file a few frames after the request.
            long length = ClosedFileLength(path);
            if (length > 0)
            {
                return length;
            }

            await Task.Delay(ScreenshotFilePollMilliseconds);
        }

        throw new IOException($"The engine did not write '{path}' within {ScreenshotFileTimeoutMilliseconds} ms of the capture.");
    }

    /// <summary>Gives the file's size when no other handle has the file open.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The size, in bytes. 0 when the file does not exist yet or a writer still has it open.</returns>
    private static long ClosedFileLength(string path)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        // The PNG writer (DirectXTex through WIC on Windows) can keep the file at the raw pixel size while it encodes.
        // An open without sharing succeeds only after the writer closed the file, and then the size is final.
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return stream.Length;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    /// <summary>Stops the render task and frees what the shot created. Main thread only.</summary>
    /// <param name="shot">The shot.</param>
    /// <returns>An empty object, for <see cref="InvokeOnUpdateAsync"/>.</returns>
    private static object StopCameraShot(CameraShot shot)
    {
        DestroyCameraShot(shot);
        return new object();
    }

    /// <summary>Destroys the task, the render target and the temporary camera that exist. Main thread only.</summary>
    /// <param name="shot">The shot.</param>
    private static void DestroyCameraShot(CameraShot shot)
    {
        if (shot.Task != null)
        {
            shot.Task.Enabled = false;
            FlaxEngine.Object.Destroy(shot.Task);
        }

        if (shot.Output != null)
        {
            shot.Output.ReleaseGPU();
            FlaxEngine.Object.Destroy(shot.Output);
        }

        if (shot.TemporaryCamera != null)
        {
            FlaxEngine.Object.Destroy(shot.TemporaryCamera);
        }
    }
}
