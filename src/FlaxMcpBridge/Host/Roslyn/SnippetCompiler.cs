using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace FlaxMcpBridge.Host.Roslyn;

/// <summary>
/// Compiles editor_execute_csharp snippets in the bridge host, so the compiler stays loaded and warm across script
/// reloads. References are kept per file (path, write time, size) and compiled images per source and references; the
/// plugin loads each image into its own collectible load context, as before.
/// </summary>
/// <remarks>
/// Only BCL types cross to the plugin. Roslyn is loaded into the Default load context from the engine's tools folder
/// (<see cref="Configure"/> first): the plugin's own copy is in its collectible context and cannot be shared.
/// </remarks>
public static class SnippetCompiler
{
    /// <summary>Guards <see cref="References"/> and <see cref="_roslynFolder"/>.</summary>
    private static readonly object Gate = new();

    /// <summary>The loaded references by file path.</summary>
    private static readonly Dictionary<string, CachedReference> References = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The compiled images by source and references.</summary>
    private static readonly SnippetCache Images = new(64);

    /// <summary>The folder of the engine's Roslyn assemblies, or null before <see cref="Configure"/>.</summary>
    private static string? _roslynFolder;

    /// <summary>Tells the host where the engine's Roslyn assemblies are; call before the first compile.</summary>
    /// <param name="roslynFolder">The folder of Microsoft.CodeAnalysis.dll and Microsoft.CodeAnalysis.CSharp.dll.</param>
    public static void Configure(string roslynFolder)
    {
        lock (Gate)
        {
            if (_roslynFolder is not null)
            {
                return;
            }

            _roslynFolder = roslynFolder;
            AssemblyLoadContext.Default.Resolving += ResolveRoslyn;
        }
    }

    /// <summary>Compiles a snippet's source, or takes its image from the cache.</summary>
    /// <param name="source">The whole compilation unit.</param>
    /// <param name="referencePaths">The assemblies it may use, as files.</param>
    /// <param name="cacheHit">Whether the image came from the cache.</param>
    /// <param name="milliseconds">The time spent here.</param>
    /// <param name="referenceCount">How many references the compile used.</param>
    /// <returns>The compiled assembly.</returns>
    /// <exception cref="InvalidOperationException">The source does not compile; the message lists the errors.</exception>
    public static byte[] Compile(string source, string[] referencePaths, out bool cacheHit, out double milliseconds, out int referenceCount)
    {
        var watch = Stopwatch.StartNew();
        var references = ReferencesFor(referencePaths);
        referenceCount = references.Count;
        var key = Key(source, references);
        var image = Images.Find(key);
        cacheHit = image is not null;
        if (image is null)
        {
            image = Emit(key, source, references);
            Images.Store(key, image);
        }

        milliseconds = watch.Elapsed.TotalMilliseconds;
        return image;
    }

    /// <summary>Compiles a snippet to an assembly image. Not inlined, so Roslyn loads only when it runs.</summary>
    /// <param name="key">The cache key; its start names the assembly.</param>
    /// <param name="source">The whole compilation unit.</param>
    /// <param name="references">The assemblies it may use.</param>
    /// <returns>The compiled assembly.</returns>
    /// <exception cref="InvalidOperationException">The source does not compile; the message lists the errors.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static byte[] Emit(string key, string source, IReadOnlyList<CachedReference> references)
    {
        var assemblyName = "FlaxMcpDynamic_" + key[..16];
        var tree = CSharpSyntaxTree.ParseText(source);
        var metadata = references.Select(reference => (MetadataReference)reference.Reference);
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        var compilation = CSharpCompilation.Create(assemblyName, new[] { tree }, metadata, options);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        if (!emitted.Success)
        {
            var errors = emitted.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            var errorText = string.Join(Environment.NewLine, errors);
            throw new InvalidOperationException("C# compilation failed: " + errorText);
        }

        return image.ToArray();
    }

    /// <summary>Loads the references, reusing those whose file did not change.</summary>
    /// <param name="paths">The assembly files.</param>
    /// <returns>One reference per path, in order.</returns>
    private static IReadOnlyList<CachedReference> ReferencesFor(string[] paths)
    {
        lock (Gate)
        {
            var result = new List<CachedReference>(paths.Length);
            foreach (var path in paths)
            {
                var file = new FileInfo(path);
                var writeTicks = file.LastWriteTimeUtc.Ticks;
                var length = file.Length;
                if (!References.TryGetValue(path, out var reference) ||
                    reference.WriteTicks != writeTicks || reference.Length != length)
                {
                    var metadata = MetadataReference.CreateFromFile(path);
                    reference = new CachedReference(path, writeTicks, length, metadata);
                    References[path] = reference;
                }

                result.Add(reference);
            }

            return result;
        }
    }

    /// <summary>Hashes a source and the state of its references into a cache key.</summary>
    /// <param name="source">The compilation unit.</param>
    /// <param name="references">The references.</param>
    /// <returns>The hash as hexadecimal text.</returns>
    private static string Key(string source, IReadOnlyList<CachedReference> references)
    {
        var text = new StringBuilder(source);
        foreach (var reference in references)
        {
            text.Append('\n').Append(reference.Path).Append('|').Append(reference.WriteTicks).Append('|').Append(reference.Length);
        }

        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>Loads a Roslyn assembly from the engine's folder when the Default context asks for it.</summary>
    /// <param name="context">The context that asks.</param>
    /// <param name="name">The assembly wanted.</param>
    /// <returns>The assembly, or null when it is not a Roslyn assembly or the file is missing.</returns>
    private static Assembly? ResolveRoslyn(AssemblyLoadContext context, AssemblyName name)
    {
        if (name.Name is null || !name.Name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
        {
            return null;
        }

        var path = Path.Combine(_roslynFolder!, name.Name + ".dll");
        if (!File.Exists(path))
        {
            return null;
        }

        return context.LoadFromAssemblyPath(path);
    }

    /// <summary>A loaded reference with the file state it was loaded from.</summary>
    /// <param name="Path">The file path.</param>
    /// <param name="WriteTicks">The file's last write time in UTC ticks.</param>
    /// <param name="Length">The file's size in bytes.</param>
    /// <param name="Reference">The loaded reference.</param>
    private sealed record CachedReference(string Path, long WriteTicks, long Length, PortableExecutableReference Reference);
}
