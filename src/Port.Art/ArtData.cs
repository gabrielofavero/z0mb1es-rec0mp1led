using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Port.Art;

internal static class ArtData
{
    internal static byte[] ResourceBytes(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Port.Art.Data." + name)
            ?? throw new InvalidDataException("Missing artwork recipe: " + name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
    internal static JsonNode Resource(string name) => JsonNode.Parse(ResourceBytes(name))!;
    internal static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
    internal static string Safe(string path) => path.Length > 0 && !Path.IsPathRooted(path) &&
        path.Split('/').All(p => p.Length > 0 && p is not ("." or "..") && !p.Contains('\\') && !p.Contains(':'))
        ? path : throw new InvalidDataException("Unsafe artwork path.");
    internal static string Text(this JsonNode node, string key) => node[key]!.GetValue<string>();
    internal static int Number(this JsonNode node, string key) => node[key]!.GetValue<int>();
    internal static byte[] Checked(byte[] bytes, JsonNode pin, string label)
    {
        if (bytes.LongLength != pin["bytes"]!.GetValue<long>() || Hash(bytes) != pin.Text("sha256"))
            throw new InvalidDataException(label + ": unsupported or damaged artwork input.");
        return bytes;
    }
    internal static int[] Rect(JsonNode node) => node.AsArray().Select(x => x!.GetValue<int>()).ToArray();
    internal static void Bounds(int[] rect, int width, int height)
    {
        if (rect.Length != 4 || rect[0] < 0 || rect[1] < 0 || rect[2] <= 0 || rect[3] <= 0 ||
            (long)rect[0] + rect[2] > width || (long)rect[1] + rect[3] > height)
            throw new InvalidDataException("Artwork rectangle exceeds its verified texture.");
    }
}
