using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Port.Prepare;

namespace Port.Art;

internal static class ArtSources
{
    internal static (string Version, long Bytes) Preview(string kind, string path)
    {
        if (kind != "i-maed-steam" || Directory.Exists(path)) return PinnedOptionalArtSource.Preview(kind, path);
        var pin = ArtData.Resource("supported-steam-versions.json");
        using var zip = ZipFile.OpenRead(path);
        var prefix = SteamPrefix(zip);
        long bytes = 0;
        foreach (var row in pin["requiredTextures"]!.AsArray().Select(x => (x!, "Content/" + x!.Text("path")))
                     .Prepend((pin["executable"]!, pin["executable"]!.Text("path"))))
            bytes += ReadSteam(zip, prefix, row.Item2, row.Item1).LongLength;
        return ("I MAED Steam 1.0.0.0", bytes);
    }

    private static string SteamPrefix(ZipArchive zip)
    {
        var matches = zip.Entries.Where(e => e.FullName == "Z0MB1ES.exe" || e.FullName.EndsWith("/Z0MB1ES.exe", StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Steam ZIP must contain one Z0MB1ES.exe and its Content folder.");
        var prefix = matches[0].FullName[..^"Z0MB1ES.exe".Length];
        if (prefix.Length > 0) ArtData.Safe(prefix.TrimEnd('/'));
        return prefix;
    }

    private static byte[] ReadSteam(ZipArchive zip, string prefix, string name, JsonNode pin)
    {
        ArtData.Safe(name);
        var entries = zip.Entries.Where(e => e.FullName == prefix + name).ToArray();
        if (entries.Length != 1 || entries[0].Length != pin["bytes"]!.GetValue<long>())
            throw new InvalidDataException("Steam ZIP is missing the supported " + name);
        using var input = entries[0].Open();
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        return ArtData.Checked(buffer.ToArray(), pin, "Steam " + name);
    }

    internal static Dictionary<string, byte[]> Read(string kind, string path, IReadOnlyDictionary<string, JsonNode> needed,
        CancellationToken cancellation)
    {
        Preview(kind, path);
        if (kind == "i-maed-steam")
        {
            if (Directory.Exists(path))
                return needed.ToDictionary(row => row.Key, row => ArtData.Checked(File.ReadAllBytes(
                    Path.Combine(path, "Content", ArtData.Safe(row.Key))), row.Value, "Steam " + row.Key));
            using var zip = ZipFile.OpenRead(path);
            var prefix = SteamPrefix(zip);
            return needed.ToDictionary(row => row.Key, row => ReadSteam(zip, prefix, "Content/" + row.Key, row.Value));
        }
        var sourceId = kind == "i-maed-360" ? "zombies-360" : "viking-360";
        var inventory = ArtData.Resource(sourceId + "-inventory.json");
        var expected = inventory["files"]!.AsArray().ToDictionary(row => row!.Text("path"), row => row!);
        var data = File.ReadAllBytes(path);
        ArtData.Checked(data, inventory["input"]!, "LIVE package");
        int Number(int offset, int size, bool little = false)
        {
            if (offset < 0 || offset + size > data.Length) throw new InvalidDataException("STFS integer exceeds package.");
            var result = 0;
            for (var i = 0; i < size; i++) result = checked(result * 256 + data[offset + (little ? size - 1 - i : i)]);
            return result;
        }
        if (!data.AsSpan(0, 4).SequenceEqual("LIVE"u8) || data[0x379] != 0x24 || (data[0x37b] & 1) == 0 ||
            Number(0x37c, 2, true) != 1 || Number(0x37e, 3, true) != 0)
            throw new InvalidDataException("Unsupported LIVE/STFS layout.");
        var header = Number(0x340, 4);
        var total = Number(0x395, 4);
        if (total is < 1 or >= 28900) throw new InvalidDataException("Unsupported STFS block count.");
        var aligned = checked((header + 4095) / 4096 * 4096);
        var used = new HashSet<int> { 0 };
        byte[] ReadFile(int start, int blocks, int size)
        {
            cancellation.ThrowIfCancellationRequested();
            var output = new byte[checked(blocks * 4096)];
            for (var n = 0; n < blocks; n++)
            {
                var block = start + n;
                var extra = (block + 170) / 170 + (block >= 170 ? 1 : 0);
                var offset = checked(aligned + (block + extra) * 4096);
                var table = block < 170 ? 0 : block / 170 * 171 + 1;
                var hash = checked(aligned + table * 4096 + block % 170 * 24);
                if (offset < 0 || offset + 4096 > data.Length || hash < 0 || hash + 20 > data.Length ||
                    !SHA1.HashData(data.AsSpan(offset, 4096)).AsSpan().SequenceEqual(data.AsSpan(hash, 20)))
                    throw new InvalidDataException("STFS block checksum failed.");
                data.AsSpan(offset, 4096).CopyTo(output.AsSpan(n * 4096));
            }
            if (size < 0 || size > output.Length) throw new InvalidDataException("Invalid STFS file size.");
            return output.AsSpan(0, size).ToArray();
        }
        var tableBytes = ReadFile(0, 1, 4096);
        var entries = new List<(string Name, bool Directory, int Start, int Blocks, int Parent, int Size)>();
        for (var index = 0; index < 64 && tableBytes[index * 64] != 0; index++)
        {
            var row = tableBytes.AsSpan(index * 64, 64);
            var flags = row[40];
            if ((flags & 0x40) == 0) throw new InvalidDataException("Noncontiguous STFS files are unsupported.");
            var name = Encoding.ASCII.GetString(row[..(flags & 0x3f)]);
            ArtData.Safe(name);
            int Small(int offset) => tableBytes[index * 64 + offset] | tableBytes[index * 64 + offset + 1] << 8 |
                tableBytes[index * 64 + offset + 2] << 16;
            entries.Add((name, (flags & 0x80) != 0, Small(47), Small(44), row[50] * 256 + row[51],
                checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(row[52..56]))));
        }
        var paths = needed.ToDictionary(row => row.Value["packageEntry"]!.Text("path"), row => row.Key);
        var result = new Dictionary<string, byte[]>();
        var seen = new HashSet<string>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.Directory) continue;
            var names = new List<string> { entry.Name };
            var parents = new HashSet<int> { index };
            for (var parent = entry.Parent; parent != 0xffff; parent = entries[parent].Parent)
            {
                if (parent >= entries.Count || !parents.Add(parent) || !entries[parent].Directory)
                    throw new InvalidDataException("Invalid STFS directory tree.");
                names.Insert(0, entries[parent].Name);
            }
            var name = string.Join('/', names);
            if (!seen.Add(name) || entry.Blocks != (entry.Size + 4095) / 4096 || entry.Start < 1 || entry.Start + entry.Blocks > total)
                throw new InvalidDataException("Invalid STFS file allocation.");
            for (var block = entry.Start; block < entry.Start + entry.Blocks; block++)
                if (!used.Add(block)) throw new InvalidDataException("Overlapping STFS data blocks.");
            var payload = ReadFile(entry.Start, entry.Blocks, entry.Size);
            if (!expected.TryGetValue(name, out var pin)) throw new InvalidDataException("Unexpected STFS file.");
            ArtData.Checked(payload, pin, "STFS " + name);
            if (paths.TryGetValue(name, out var target)) result.Add(target, payload);
        }
        if (used.Count != total || seen.Count != expected.Count || result.Count != needed.Count)
            throw new InvalidDataException("Incomplete STFS inventory.");
        return result;
    }
}
