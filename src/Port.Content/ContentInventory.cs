using System.Security.Cryptography;

namespace Port.Content;

public sealed record ContentAsset(string Path, string Kind, long Bytes, string Sha256,
    string? Reader, char? Platform, byte? Version, byte? Flags);

public static class ContentInventory
{
    public static IReadOnlyList<ContentAsset> Read(string extractionRoot)
    {
        var root = Path.GetFullPath(extractionRoot);
        var content = Path.Combine(root, "Content");
        if (!Directory.Exists(content))
            throw new DirectoryNotFoundException($"Content directory is missing: {content}");

        var assets = new List<ContentAsset>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative is "ZombiesWP7.dll" or "AppManifest.xaml" or "WMAppManifest.xml" or
                "MDILFileList.xml" or "ZombiesWP7.gameconfig" or "Background.png" or "GameThumbnail.png")
                continue;
            if (relative == "content-manifest.json") continue;
            var extension = Path.GetExtension(path).ToLowerInvariant();
            var kind = extension switch
            {
                ".xnb" => "xnb",
                ".wma" => "wma-companion",
                ".ogg" => "ogg-companion",
                _ => "companion-data"
            };
            string? reader = null;
            char? platform = null;
            byte? version = null;
            byte? flags = null;
            if (extension == ".xnb")
            {
                using var input = File.OpenRead(path);
                using var binary = new BinaryReader(input);
                if (binary.ReadByte() != 'X' || binary.ReadByte() != 'N' || binary.ReadByte() != 'B')
                    throw new InvalidDataException($"Invalid XNB magic: {relative}");
                platform = (char)binary.ReadByte();
                version = binary.ReadByte();
                flags = binary.ReadByte();
                if (binary.ReadInt32() != input.Length)
                    throw new InvalidDataException($"Invalid XNB length: {relative}");
                if ((flags & 0x80) != 0)
                    throw new InvalidDataException($"Compressed XNB requires separate inspection: {relative}");
                var readers = Read7BitInt(binary);
                if (readers < 1 || readers > 32)
                    throw new InvalidDataException($"Invalid XNB reader count: {relative}");
                reader = binary.ReadString();
            }
            using var hashInput = File.OpenRead(path);
            assets.Add(new ContentAsset(relative, kind, hashInput.Length,
                Convert.ToHexString(SHA256.HashData(hashInput)).ToLowerInvariant(),
                reader, platform, version, flags));
        }
        return assets;
    }

    private static int Read7BitInt(BinaryReader input)
    {
        var result = 0;
        for (var shift = 0; shift < 35; shift += 7)
        {
            var value = input.ReadByte();
            result |= (value & 0x7f) << shift;
            if ((value & 0x80) == 0) return result;
        }
        throw new InvalidDataException("Invalid XNB reader count encoding.");
    }
}
