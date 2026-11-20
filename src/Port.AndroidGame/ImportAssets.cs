using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Android.Content.Res;
using Port.Art;
using Port.Content;
using Port.Prepare;

namespace Port.AndroidGame;

internal static class ImportAssets
{
    private sealed record Entry(string Path, long Bytes, string Sha256);

    internal static PersistentAssetStore Store(AssetManager assets, string root)
    {
        return new(Path.Combine(root, "Assets"), Path.Combine(root, "Saves"), (xap, work, progress, token) =>
        {
            var inputs = OpenInputs(assets, root);
            using var identity = assets.Open("converter-identity.txt");
            using var reader = new StreamReader(identity);
            return AndroidGamePreparation.Prepare(xap, work, inputs, new AudioConverter(reader.ReadToEnd().Trim()), progress, token);
        }, new PortableArtImporter());
    }

    private static string OpenInputs(AssetManager assets, string root)
    {
        var destination = Path.Combine(root, "preparation");
        Directory.CreateDirectory(destination);
        using var stream = assets.Open("preparation/inputs-manifest.json");
        var entries = JsonSerializer.Deserialize<Entry[]>(stream) ?? throw new InvalidDataException("Missing preparation inputs.");
        foreach (var entry in entries)
        {
            if (entry.Path.Contains('\\') || entry.Path.Contains(':') || entry.Path.StartsWith('/') ||
                entry.Path.Split('/').Any(part => part is "" or "." or "..")) throw new InvalidDataException("Invalid preparation path.");
            var path = Path.Combine(destination, entry.Path);
            if (File.Exists(path) && new FileInfo(path).Length == entry.Bytes && Hash(path) == entry.Sha256) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var input = assets.Open("preparation/" + entry.Path))
            using (var output = File.Create(path)) input.CopyTo(output);
            if (new FileInfo(path).Length != entry.Bytes || Hash(path) != entry.Sha256)
                throw new InvalidDataException("Preparation input differs: " + entry.Path);
        }
        return destination;
    }

    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }

    private sealed class AudioConverter(string identity) : IContentAudioConverter
    {
        public string ToolSha256 => identity;
        public string OutputExtension => ".qoa";
        public void Convert(string input, string output, Action<long>? progress)
        {
            if (Transcode(input, output) != 0) throw new InvalidDataException("Could not prepare the selected package's music.");
        }
        [DllImport("port_audio_converter", EntryPoint = "z0_audio_convert", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Transcode([MarshalAs(UnmanagedType.LPUTF8Str)] string input,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string output);
    }
}
