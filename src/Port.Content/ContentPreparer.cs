using System.Diagnostics;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace Port.Content;

public sealed record ContentProgress(string Stage, long Completed, long Total);

/// <summary>Host-supplied WMA decoder/encoder. Implementations write Ogg Vorbis.</summary>
public interface IContentAudioConverter
{
    string ToolSha256 { get; }
    void Convert(string input, string output, Action<long>? onProgress);
}

public static class ContentPreparer
{
    public static string Prepare(string extractionRoot, string outputDirectory, string ffmpegPath,
        string expectedFfmpegSha256, string sourceXapSha256, Action<ContentProgress>? progress = null)
    {
        var tool = Path.GetFullPath(ffmpegPath);
        if (Hash(tool) != expectedFfmpegSha256.ToLowerInvariant())
            throw new InvalidDataException("FFmpeg executable differs from the pinned binary.");
        return Prepare(extractionRoot, outputDirectory,
            new ProcessAudioConverter(tool, expectedFfmpegSha256.ToLowerInvariant()),
            sourceXapSha256, progress);
    }

    public static string Prepare(string extractionRoot, string outputDirectory,
        IContentAudioConverter converter, string sourceXapSha256,
        Action<ContentProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(converter);
        var source = Path.GetFullPath(extractionRoot);
        var output = Path.GetFullPath(outputDirectory);
        if (Path.Exists(output)) throw new InvalidOperationException("Content output must be a new directory.");
        if (output.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Content output must be outside the source extraction.");
        var parent = Path.GetDirectoryName(output) ?? throw new InvalidOperationException("Output must not be a drive root.");
        if (converter.ToolSha256.Length != 64 ||
            !converter.ToolSha256.All(Uri.IsHexDigit))
            throw new ArgumentException("Audio converter must provide a SHA-256 identity.", nameof(converter));
        var assets = ContentInventory.Read(source);
        if (assets.Count(asset => asset.Kind == "xnb") != 91 ||
            assets.Count(asset => asset.Kind == "wma-companion") != 3 ||
            assets.Count(asset => asset.Kind == "companion-data") != 9)
            throw new InvalidDataException("Content inventory differs from the supported XAP.");

        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".{Path.GetFileName(output)}.stage-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            var copied = assets.Where(asset => asset.Kind != "wma-companion").ToArray();
            progress?.Invoke(new ContentProgress("copy", 0, copied.Length));
            for (var i = 0; i < copied.Length; i++)
            {
                var asset = copied[i];
                var destination = Path.Combine(staging, asset.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(source, asset.Path.Replace('/', Path.DirectorySeparatorChar)), destination);
                if (Hash(destination) != asset.Sha256)
                    throw new InvalidDataException($"Copied file changed: {asset.Path}");
                progress?.Invoke(new ContentProgress("copy", i + 1, copied.Length));
            }
            var music = assets.Where(asset => asset.Kind == "wma-companion").ToArray();
            var durations = music.Select(DurationMicroseconds).ToArray();
            var encoded = new long[music.Length];
            var totalDuration = durations.Sum();
            var progressLock = new object();
            progress?.Invoke(new ContentProgress("music", 0, totalDuration));
            Task.WhenAll(music.Select((asset, index) => Task.Run(() =>
                ConvertAudio(converter, source, staging, asset, progress is null ? null : elapsed =>
                {
                    lock (progressLock)
                    {
                        encoded[index] = Math.Max(encoded[index], Math.Clamp(elapsed, 0, durations[index]));
                        progress(new ContentProgress("music", encoded.Sum(), totalDuration));
                    }
                }))))
                .GetAwaiter().GetResult();

            // SceneMgr.ReadTextures loads gfx/scenes/<keyframe texture>, while this
            // exact package stores the five matching XNBs under gfx_2/scenes/.
            // Copy byte-identical aliases so the original lookup path can resolve.
            foreach (var name in new[] { "diamond", "face1", "face2", "heart", "viking" })
            {
                var original = Path.Combine(staging, "Content", "gfx_2", "scenes", name + ".xnb");
                var alias = Path.Combine(staging, "Content", "gfx", "scenes", name + ".xnb");
                if (!File.Exists(original) || File.Exists(alias))
                    throw new InvalidDataException($"Scene alias precondition failed: {name}");
                Directory.CreateDirectory(Path.GetDirectoryName(alias)!);
                File.Copy(original, alias);
                if (Hash(alias) != Hash(original))
                    throw new InvalidDataException($"Scene alias differs from source: {name}");
            }

            progress?.Invoke(new ContentProgress("finalize", 0, 1));
            var outputs = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
                .Select(path => new ContentOutput(Path.GetRelativePath(staging, path).Replace('\\', '/'),
                    new FileInfo(path).Length, Hash(path)))
                .OrderBy(asset => asset.Path, StringComparer.Ordinal).ToArray();
            var manifest = new ContentManifest(1, sourceXapSha256, converter.ToolSha256.ToLowerInvariant(),
                assets, outputs);
            var manifestPath = Path.Combine(staging, "content-manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Directory.Move(staging, output);
            progress?.Invoke(new ContentProgress("finalize", 1, 1));
            return Hash(Path.Combine(output, "content-manifest.json"));
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static long DurationMicroseconds(ContentAsset asset) => asset.Path.Split('/')[^1] switch
    {
        // The installer accepts one exact XAP, so these source durations are fixed.
        "endless.wma" => 302_050_000,
        "epicopus.wma" => 817_950_000,
        "timeviking.wma" => 907_670_000,
        _ => throw new InvalidDataException($"Unknown music track: {asset.Path}")
    };

    private static void ConvertAudio(IContentAudioConverter converter, string source, string staging, ContentAsset asset,
        Action<long>? onProgress)
    {
        var input = Path.Combine(source, asset.Path.Replace('/', Path.DirectorySeparatorChar));
        var relative = Path.ChangeExtension(asset.Path, ".ogg");
        var destination = Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        converter.Convert(input, destination, onProgress);
        if (!File.Exists(destination) || new FileInfo(destination).Length == 0)
            throw new InvalidDataException($"Audio conversion failed for {asset.Path}.");
        VerifyOggVorbis(destination);
        onProgress?.Invoke(DurationMicroseconds(asset));
    }

    private sealed class ProcessAudioConverter(string tool, string hash) : IContentAudioConverter
    {
        public string ToolSha256 => hash;

        public void Convert(string input, string destination, Action<long>? onProgress)
        {
            var start = new ProcessStartInfo(tool)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = onProgress is not null,
                CreateNoWindow = true
            };
            if (onProgress is not null)
                foreach (var argument in new[] { "-stats_period", "0.25", "-progress", "pipe:1" })
                    start.ArgumentList.Add(argument);
            foreach (var argument in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-i", input,
                         "-map", "0:a:0", "-vn", "-c:a", "libvorbis", "-q:a", "5", "-map_metadata", "-1",
                         "-bitexact", destination })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Could not start FFmpeg.");
            var errorTask = process.StandardError.ReadToEndAsync();
            if (onProgress is not null)
            {
                string? line;
                while ((line = process.StandardOutput.ReadLine()) is not null)
                    if (line.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                        long.TryParse(line.AsSpan("out_time_us=".Length), out var elapsed))
                        onProgress(elapsed);
            }
            process.WaitForExit();
            var standardError = errorTask.GetAwaiter().GetResult();
            if (process.ExitCode != 0)
                throw new InvalidDataException($"Audio conversion failed for {Path.GetFileName(input)}: {standardError.Trim()}");
        }
    }

    private static void VerifyOggVorbis(string path)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[27];
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual("OggS"u8) || header[4] != 0 ||
            (header[5] & 0x02) == 0 || header[26] == 0)
            throw new InvalidDataException($"Converted music is not an Ogg beginning-of-stream page: {path}");
        var segments = new byte[header[26]];
        stream.ReadExactly(segments);
        if (segments[0] < 16)
            throw new InvalidDataException($"Converted music lacks a Vorbis identification packet: {path}");
        var packet = new byte[segments[0]];
        stream.ReadExactly(packet);
        if (!packet.AsSpan(0, 7).SequenceEqual("\x01vorbis"u8) ||
            packet[11] != 2 || BinaryPrimitives.ReadInt32LittleEndian(packet.AsSpan(12, 4)) != 44100)
            throw new InvalidDataException($"Converted music must be stereo 44.1 kHz Ogg Vorbis: {path}");
    }

    private sealed record ContentOutput(string Path, long Bytes, string Sha256);
    private sealed record ContentManifest(int SchemaVersion, string SourceXapSha256,
        string FfmpegSha256, IReadOnlyList<ContentAsset> Inputs, IReadOnlyList<ContentOutput> Outputs);
}
