using System.Text.Json;
using System.Text.Json.Nodes;
using Port.Prepare;

namespace Port.Art;

/// <summary>The desktop art recipes implemented in-process for sandboxed hosts.</summary>
public sealed class PortableArtImporter : IOptionalArtImporter
{
    private static readonly string[] Kinds = ["i-maed-360", "i-maed-steam", "time-viking-360"];
    private const string PhoneHash = "7b819cb562826ae4d2592ff741877e76da0e57bfbe48192aabb79319fdb36823";
    public (string Version, long SourceBytes) Preview(string kind, string path) => ArtSources.Preview(kind, path);

    public void Prepare(string kind, string path, string phoneRoot, string artRoot,
        Action<string>? progress, CancellationToken cancellation)
    {
        Preview(kind, path);
        var mapping = ArtData.Resource(kind + ".json");
        ValidatePhone(mapping, phoneRoot);
        var known = Inventory(kind);
        var required = mapping["elements"]!.AsArray().Where(e => e!.Text("status") == "candidate")
            .Select(e => e!["source"]!.Text("path")).ToHashSet(StringComparer.Ordinal);
        if (kind == "i-maed-steam")
            required.UnionWith(ArtData.Resource("supported-steam-versions.json")["requiredTextures"]!.AsArray().Select(t => t!.Text("path")));
        progress?.Invoke("validate-" + kind);
        var raw = ArtSources.Read(kind, path, required.ToDictionary(name => name, name => known[name]), cancellation);
        var images = new Dictionary<string, Pixels>();
        foreach (var (name, bytes) in raw)
        {
            cancellation.ThrowIfCancellationRequested();
            progress?.Invoke("decode-" + kind + " " + name);
            images.Add(name, TexturePixels.Decode(bytes, known[name]));
        }
        var vignette = Vignette(mapping, images, phoneRoot);
        var temporary = Path.Combine(artRoot, ".art-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temporary, "art"));
        try
        {
            var rows = new JsonArray();
            var elements = mapping["elements"]!.AsArray();
            foreach (var elementValue in elements)
            {
                cancellation.ThrowIfCancellationRequested();
                var element = elementValue!;
                var row = new JsonObject { ["id"] = element["id"]!.DeepClone(), ["mode"] = element["mode"]?.DeepClone(),
                    ["fallback"] = element["fallback"]!.DeepClone(), ["selection"] = "phone" };
                if (element.Text("status") == "candidate")
                {
                    var source = element["source"]!;
                    var name = source.Text("path");
                    if (source.Text("decodedSha256") != known[name].Text("sha256"))
                        throw new InvalidDataException("Artwork mapping source pin differs.");
                    var rect = ArtData.Rect(source["rectangle"]!);
                    var expected = element["expectedOutput"]!;
                    if (!ArtData.Rect(expected["physicalSize"]!).SequenceEqual(rect[2..]) ||
                        ArtData.Rect(expected["logicalSize"]!).Any(v => v <= 0))
                        throw new InvalidDataException("Artwork output shape differs.");
                    var background = element.Text("id").StartsWith("background/i-maed/", StringComparison.Ordinal);
                    if (background && vignette is null) throw new InvalidDataException("Missing phone vignette.");
                    var png = TexturePixels.CropPng(images[name], rect, background ? vignette!.Value.Mask : null);
                    var sha = ArtData.Hash(png);
                    var output = Path.Combine(temporary, "art", sha + ".png");
                    if (!File.Exists(output)) File.WriteAllBytes(output, png);
                    row["selection"] = kind;
                    row["sourcePath"] = name;
                    row["sourceRect"] = source["rectangle"]!.DeepClone();
                    row["sourceSha256"] = known[name]["sha256"]!.DeepClone();
                    row["output"] = new JsonObject { ["path"] = "art/" + sha + ".png", ["sha256"] = sha,
                        ["size"] = new JsonArray(rect[2] + 4, rect[3] + 4),
                        ["contentRect"] = new JsonArray(2, 2, rect[2], rect[3]),
                        ["logicalSize"] = expected["logicalSize"]!.DeepClone(), ["sampler"] = "clamp", ["padding"] = "edge-extended" };
                    if (background) row["treatment"] = new JsonObject { ["id"] = "phone-vignette-v1", ["maskSha256"] = vignette!.Value.Hash };
                }
                rows.Add(row);
                if (rows.Count % 20 == 0) progress?.Invoke($"prepare-{kind} {rows.Count}/{elements.Count}");
            }
            var inputs = Identity(kind);
            var textures = new JsonObject();
            foreach (var name in required.Order(StringComparer.Ordinal))
            {
                var meta = known[name];
                var texture = new JsonObject { ["sha256"] = meta["sha256"]!.DeepClone(),
                    ["size"] = new JsonArray(meta.Number("width"), meta.Number("height")), ["type"] = meta["type"]!.DeepClone(),
                    ["surfaceFormat"] = meta["surfaceFormat"]!.DeepClone(), ["mipCount"] = meta["mipCount"]!.DeepClone() };
                if (kind.EndsWith("360", StringComparison.Ordinal)) texture["packageEntry"] = meta["packageEntry"]!.DeepClone();
                textures[name] = texture;
            }
            inputs["textures"] = textures;
            var manifest = new JsonObject { ["schemaVersion"] = 1, ["sourceId"] = kind,
                ["mappingSha256"] = ArtData.Hash(ArtData.ResourceBytes(kind + ".json")), ["phoneDllSha256"] = PhoneHash,
                ["inputs"] = inputs, ["elements"] = rows };
            var manifestPath = Path.Combine(temporary, "manifest.json");
            Write(manifestPath, manifest);
            var generation = ArtData.HashFile(manifestPath);
            var parent = Path.Combine(artRoot, kind);
            var destination = Path.Combine(parent, generation);
            Directory.CreateDirectory(parent);
            if (!Directory.Exists(destination)) Directory.Move(temporary, destination);
            else if (ArtData.HashFile(Path.Combine(destination, "manifest.json")) != generation)
                throw new InvalidDataException("Artwork generation is damaged.");
            Write(Path.Combine(parent, "current.json"), new JsonObject { ["schemaVersion"] = 1, ["generation"] = generation });
            VerifySource(artRoot, kind);
            progress?.Invoke("prepared-" + kind);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    public void Compose(string phoneRoot, string artRoot, Action<string>? progress, CancellationToken cancellation)
    {
        var mapping = ArtData.Resource("endless-360.json");
        ValidatePhone(mapping, phoneRoot);
        var sources = new Dictionary<string, (string Generation, Dictionary<string, JsonNode> Rows)>();
        foreach (var kind in Kinds)
        {
            if (!File.Exists(Path.Combine(artRoot, kind, "current.json"))) continue;
            var (generation, manifest) = VerifySource(artRoot, kind);
            sources[kind] = (generation, manifest["elements"]!.AsArray().ToDictionary(e => e!.Text("id"), e => e!));
        }
        if (sources.Count == 0) return;
        progress?.Invoke("compose-endless");
        var rows = new JsonArray();
        foreach (var elementValue in mapping["elements"]!.AsArray())
        {
            cancellation.ThrowIfCancellationRequested();
            var element = elementValue!;
            var row = new JsonObject { ["id"] = element["id"]!.DeepClone(), ["fallback"] = element["fallback"]!.DeepClone(), ["selection"] = "phone" };
            foreach (var variantValue in element["variants"]!.AsArray())
            {
                var variant = variantValue!;
                var kind = variant.Text("sourceId");
                if (!sources.TryGetValue(kind, out var source) || !source.Rows.TryGetValue(element.Text("id"), out var candidate) ||
                    candidate.Text("selection") != kind || candidate.Text("sourcePath") != variant["source"]!.Text("path") ||
                    !JsonNode.DeepEquals(candidate["sourceRect"], variant["source"]!["rectangle"])) continue;
                row["selection"] = kind; row["generation"] = source.Generation;
                foreach (var key in new[] { "output", "sourcePath", "sourceRect" }) row[key] = candidate[key]!.DeepClone();
                break;
            }
            rows.Add(row);
        }
        var manifestValue = new JsonObject { ["schemaVersion"] = 1, ["game"] = "endless", ["phoneDllSha256"] = PhoneHash,
            ["priority"] = mapping["sourcePriority"]!.DeepClone(), ["elements"] = rows };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifestValue).Concat(new byte[] { 10 }).ToArray();
        var hash = ArtData.Hash(bytes);
        Directory.CreateDirectory(Path.Combine(artRoot, "endless-360"));
        File.WriteAllBytes(Path.Combine(artRoot, "endless-360", hash + ".json"), bytes);
        Write(Path.Combine(artRoot, "endless-360/current.json"), new JsonObject { ["schemaVersion"] = 1, ["generation"] = hash });
    }

    private static Dictionary<string, JsonNode> Inventory(string kind) => ArtData.Resource("asset-swap-inventory.json")["textures"]!
        .AsArray().Where(t => t!.Text("source") == kind).ToDictionary(t => t!.Text("path"), t => t!);

    private static void ValidatePhone(JsonNode mapping, string root)
    {
        if (mapping.Text("phoneDllSha256") != PhoneHash || ArtData.HashFile(Path.Combine(root, "ZombiesWP7.dll")) != PhoneHash)
            throw new InvalidDataException("Original phone art version differs from the supported input.");
        var known = Inventory("phone");
        var checkedPaths = new HashSet<string>();
        foreach (var element in mapping["elements"]!.AsArray())
        foreach (var alternative in element!["fallback"]!["alternatives"]!.AsArray())
        {
            var path = alternative!.Text("path");
            var meta = known[path];
            if (alternative!.Text("sha256") != meta.Text("sha256")) throw new InvalidDataException("Original art recipe pin differs.");
            if (checkedPaths.Add(path)) ArtData.Checked(File.ReadAllBytes(Path.Combine(root, "Content", ArtData.Safe(path))), meta, "Original art " + path);
            if (element!["fallback"]!["rectangle"] is { } rect) ArtData.Bounds(ArtData.Rect(rect), meta.Number("width"), meta.Number("height"));
        }
    }

    private static JsonObject Identity(string kind)
    {
        if (kind == "i-maed-steam")
        {
            var pin = ArtData.Resource("supported-steam-versions.json");
            return new JsonObject { ["sourceId"] = kind, ["version"] = pin["version"]!.DeepClone(), ["executableSha256"] = pin["executable"]!["sha256"]!.DeepClone() };
        }
        var package = ArtData.Resource("supported-360-versions.json")["titles"]!.AsArray()
            .Single(t => t!.Text("sourceId") == (kind == "i-maed-360" ? "zombies-360" : "viking-360"))!["package"]!;
        return new JsonObject { ["sourceId"] = kind, ["format"] = "LIVE/STFS", ["packageSha256"] = package["sha256"]!.DeepClone(),
            ["gameInfoTitleId"] = package["gameInfoTitleId"]!.DeepClone() };
    }

    private static (string Generation, JsonNode Manifest) VerifySource(string root, string kind)
    {
        var parent = Path.Combine(root, kind);
        var pointer = JsonNode.Parse(File.ReadAllText(Path.Combine(parent, "current.json")))!;
        var generation = pointer.Text("generation");
        if (generation.Length != 64 || generation.Any(c => !Uri.IsHexDigit(c) || char.IsUpper(c))) throw new InvalidDataException("Unsafe art generation.");
        var folder = Path.Combine(parent, generation);
        var path = Path.Combine(folder, "manifest.json");
        if (ArtData.HashFile(path) != generation) throw new InvalidDataException("Artwork manifest differs from its receipt.");
        var manifest = JsonNode.Parse(File.ReadAllText(path))!;
        if (manifest.Number("schemaVersion") != 1 || manifest.Text("sourceId") != kind || manifest.Text("phoneDllSha256") != PhoneHash ||
            manifest.Text("mappingSha256") != ArtData.Hash(ArtData.ResourceBytes(kind + ".json")))
            throw new InvalidDataException("Artwork manifest has unsupported provenance.");
        var recipe = ArtData.Resource(kind + ".json")["elements"]!.AsArray();
        var rows = manifest["elements"]!.AsArray();
        if (rows.Count != recipe.Count) throw new InvalidDataException("Incomplete artwork coverage.");
        var identity = Identity(kind);
        foreach (var (key, value) in identity)
            if (!JsonNode.DeepEquals(manifest["inputs"]![key], value))
                throw new InvalidDataException("Artwork source identity differs.");
        var known = Inventory(kind);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index]!;
            var element = recipe[index]!;
            if (row.Text("id") != element.Text("id") || !JsonNode.DeepEquals(row["fallback"], element["fallback"]) ||
                row.Text("selection") != (element.Text("status") == "candidate" ? kind : "phone"))
                throw new InvalidDataException("Artwork coverage or original fallback differs.");
            if (row.Text("selection") != kind) continue;
            var source = element["source"]!;
            var output = row["output"]!;
            var rect = ArtData.Rect(source["rectangle"]!);
            if (row.Text("sourcePath") != source.Text("path") || !JsonNode.DeepEquals(row["sourceRect"], source["rectangle"]) ||
                row.Text("sourceSha256") != known[source.Text("path")].Text("sha256") ||
                output.Text("path") != "art/" + output.Text("sha256") + ".png" ||
                !ArtData.Rect(output["size"]!).SequenceEqual(new[] { rect[2] + 4, rect[3] + 4 }) ||
                !ArtData.Rect(output["contentRect"]!).SequenceEqual(new[] { 2, 2, rect[2], rect[3] }) ||
                !JsonNode.DeepEquals(output["logicalSize"], element["expectedOutput"]!["logicalSize"]) ||
                output.Text("sampler") != "clamp" || output.Text("padding") != "edge-extended")
                throw new InvalidDataException("Artwork recipe or geometry differs.");
            if (ArtData.HashFile(Path.Combine(folder, ArtData.Safe(output.Text("path")))) != output.Text("sha256"))
                throw new InvalidDataException("Artwork pixels differ from their manifest.");
        }
        return (generation, manifest);
    }

    private static void Write(string path, JsonNode value)
    {
        File.WriteAllText(path + ".tmp", value.ToJsonString() + "\n");
        File.Move(path + ".tmp", path, overwrite: true);
    }

    private static (byte[] Mask, string Hash)? Vignette(JsonNode mapping, Dictionary<string, Pixels> images, string phoneRoot)
    {
        var rows = mapping["elements"]!.AsArray().Where(e => e!.Text("status") == "candidate" &&
            e!.Text("id").StartsWith("background/i-maed/", StringComparison.Ordinal)).ToArray();
        if (rows.Length == 0) return null;
        if (rows.Length != 8) throw new InvalidDataException("Phone vignette needs all eight supported backgrounds.");
        var known = Inventory("phone");
        var numerator = new double[640 * 360]; var denominator = new double[numerator.Length];
        foreach (var rowValue in rows)
        {
            var row = rowValue!;
            var source = images[row["source"]!.Text("path")];
            var fallback = row["fallback"]!["alternatives"]![0]!.Text("path");
            var original = TexturePixels.Decode(File.ReadAllBytes(Path.Combine(phoneRoot, "Content", fallback)), known[fallback]);
            if (source.Width != 1280 || source.Height != 720 || original.Width != 640 || original.Height != 360)
                throw new InvalidDataException("Phone vignette texture dimensions differ.");
            for (var y = 0; y < 360; y++)
            for (var x = 0; x < 640; x++)
            for (var c = 0; c < 3; c++)
            {
                var a = original.Rgba[(y * 640 + x) * 4 + c];
                int Pixel(int dx, int dy) => source.Rgba[((y * 2 + dy) * 1280 + x * 2 + dx) * 4 + c];
                // Pillow's BOX filter rounds its separable horizontal and vertical passes.
                var b = (((Pixel(0, 0) + Pixel(1, 0) + 1) / 2) + ((Pixel(0, 1) + Pixel(1, 1) + 1) / 2) + 1) / 2;
                numerator[y * 640 + x] += a * b;
                denominator[y * 640 + x] += b * b;
            }
        }
        var low = numerator.Select((n, i) => (byte)Math.Round(Math.Clamp(n / Math.Max(1, denominator[i]), 0, 1) * 255,
            MidpointRounding.ToEven)).ToArray();
        var horizontal = new byte[1280 * 360]; var high = new byte[1280 * 720];
        for (var y = 0; y < 360; y++)
        for (var x = 0; x < 1280; x++)
        {
            var left = (int)Math.Floor((x + 0.5) / 2 - 0.5); var weight = x % 2 == 0 ? 3 : 1;
            horizontal[y * 1280 + x] = (byte)((low[y * 640 + Math.Clamp(left, 0, 639)] * (4 - weight) +
                low[y * 640 + Math.Clamp(left + 1, 0, 639)] * weight + 2) / 4);
        }
        for (var y = 0; y < 720; y++)
        for (var x = 0; x < 1280; x++)
        {
            var top = (int)Math.Floor((y + 0.5) / 2 - 0.5); var weight = y % 2 == 0 ? 3 : 1;
            high[y * 1280 + x] = (byte)((horizontal[Math.Clamp(top, 0, 359) * 1280 + x] * (4 - weight) +
                horizontal[Math.Clamp(top + 1, 0, 359) * 1280 + x] * weight + 2) / 4);
        }
        return (high, ArtData.Hash(low));
    }
}
