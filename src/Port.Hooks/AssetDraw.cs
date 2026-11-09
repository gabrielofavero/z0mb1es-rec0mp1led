using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Port.Hooks;

/// <summary>Routes an original phone draw to an individually prepared image, retaining phone geometry.</summary>
public static class AssetDraw
{
    private readonly record struct Key(string Path, Rectangle Rectangle);
    private sealed class Route(string file, Rectangle content, Vector2 ratio, string id)
    {
        public readonly string File = file;
        public readonly Rectangle Content = content;
        public readonly Vector2 Ratio = ratio;
        public readonly string Id = id;
        public Texture2D? Texture;
    }

    private static readonly ConcurrentDictionary<Texture2D, string> Names = new();
    private static readonly Dictionary<(Texture2D Atlas, Rectangle Source), Texture2D> PhoneCharacters = new();
    private static readonly Dictionary<Texture2D, Texture2D> PhoneShadows = new();
    private static readonly Dictionary<Key, Route> Normal = new();
    private static readonly Dictionary<Key, Route> Viking = new();
    private static readonly Dictionary<Key, Route> Endless = new();
    private static FieldInfo? stateField;
    private static FieldInfo? zoomField;
    private static int replacements;
    private static int originalDraws;
    private static readonly ConcurrentDictionary<string, int> Used = new();

    public static int Replacements => Volatile.Read(ref replacements);
    public static int OriginalDraws => Volatile.Read(ref originalDraws);
    public static int RouteCount(string mode) => mode switch
    {
        "normal" => Normal.Count,
        "viking" => Viking.Count,
        "endless" => Endless.Count,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
    public static int UsedCount => Used.Count;
    public static string UsedSample => string.Join(",", Used.Keys.Order(StringComparer.Ordinal).Take(12));

    public static int RenderCatalog(ContentManager content, GraphicsDevice device, string output)
    {
        var routes = CurrentRoutes();
        if (routes is null || routes.Count == 0) return 0;
        if (routes.Count > 375) throw new InvalidDataException("Art catalog exceeds the diagnostic canvas.");
        using var target = new RenderTarget2D(device, 600, 360, false, SurfaceFormat.Color, DepthFormat.None);
        using var sprite = new SpriteBatch(device);
        device.SetRenderTarget(target);
        device.Clear(new Color(18, 18, 18));
        sprite.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp,
            DepthStencilState.None, RasterizerState.CullNone);
        var before = Replacements;
        var index = 0;
        foreach (var entry in routes.OrderBy(row => row.Key.Path, StringComparer.Ordinal)
                                    .ThenBy(row => row.Key.Rectangle.Y).ThenBy(row => row.Key.Rectangle.X))
        {
            var source = LoadTexture(content, entry.Key.Path[..^4]);
            Draw(sprite, source, new Rectangle(index % 25 * 24, index / 25 * 24, 24, 24),
                entry.Key.Rectangle, Color.White);
            index++;
        }
        sprite.End();
        device.SetRenderTarget(null);
        if (Replacements - before != routes.Count)
            throw new InvalidDataException($"Art catalog missed routes: {Replacements - before}/{routes.Count}.");
        using (var stream = File.Create(output)) target.SaveAsPng(stream, target.Width, target.Height);
        return routes.Count;
    }

    public static void Configure(string? store, string normal, string viking, string endless)
    {
        foreach (var character in PhoneCharacters.Values) character.Dispose();
        PhoneCharacters.Clear();
        foreach (var shadow in PhoneShadows.Values) shadow.Dispose();
        PhoneShadows.Clear();
        foreach (var texture in Normal.Values.Concat(Viking.Values).Concat(Endless.Values)
                     .Select(route => route.Texture).OfType<Texture2D>().Distinct())
            texture.Dispose();
        Normal.Clear(); Viking.Clear(); Endless.Clear(); Used.Clear();
        stateField = null;
        zoomField = null;
        replacements = originalDraws = 0;
        if (normal == "original" && viking == "original" && endless == "original") return;
        if (store is null) throw new InvalidDataException("Imported art requires --art-store.");
        var root = Path.GetFullPath(store);
        if (normal != "original") TryLoad(normal == "steam" ? "i-maed-steam" : "i-maed-360", Normal,
            target => LoadSource(root, normal == "steam" ? "i-maed-steam" : "i-maed-360", target));
        if (viking != "original") TryLoad("time-viking-360", Viking,
            target => LoadSource(root, "time-viking-360", target));
        if (endless != "original") TryLoad("endless-360", Endless,
            target => LoadEndless(root, target));
    }

    public static (bool Available, string Reason) CheckSource(string store, string kind)
    {
        var routes = new Dictionary<Key, Route>();
        try
        {
            if (kind == "endless-360") LoadEndless(store, routes, announce: false);
            else if (kind is "i-maed-steam" or "i-maed-360" or "time-viking-360")
                LoadSource(store, kind, routes, announce: false);
            else throw new ArgumentOutOfRangeException(nameof(kind));
            return routes.Count > 0 ? (true, "") : (false, "No mapped art was prepared.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      InvalidDataException or JsonException or KeyNotFoundException)
        {
            return (false, error.Message);
        }
    }

    private static void TryLoad(string kind, Dictionary<Key, Route> routes,
        Action<Dictionary<Key, Route>> loader)
    {
        var staged = new Dictionary<Key, Route>();
        try
        {
            loader(staged);
            foreach (var (key, route) in staged) routes.Add(key, route);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
                                      InvalidDataException or JsonException or KeyNotFoundException)
        {
            Console.Error.WriteLine($"ART_UNAVAILABLE kind={kind} action=repair reason={error.Message}");
        }
    }

    public static Texture2D LoadTexture(ContentManager content, string name)
    {
        var texture = content.Load<Texture2D>(name);
        Names[texture] = Normalize(name);
        return texture;
    }

    private static string Normalize(string name) =>
        name.Replace('\\', '/').TrimStart('/').ToLowerInvariant().Replace(".xnb", "") + ".xnb";

    private static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Generation(string root, string kind)
    {
        using var pointer = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, kind, "current.json")));
        var generation = pointer.RootElement.GetProperty("generation").GetString()!;
        if (generation.Length != 64 || generation.Any(c => !Uri.IsHexDigit(c) || char.IsUpper(c)))
            throw new InvalidDataException($"Invalid {kind} art generation.");
        return generation;
    }

    private static JsonDocument ReadManifest(string path, string generation)
    {
        if (Hash(path) != generation)
            throw new InvalidDataException($"Imported art manifest hash mismatch: {path}");
        var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported imported art manifest version.");
        return document;
    }

    private static Rectangle Rect(JsonElement element) =>
        new(element[0].GetInt32(), element[1].GetInt32(), element[2].GetInt32(), element[3].GetInt32());

    private static void Add(JsonElement element, string file, Dictionary<Key, Route> routes, Dictionary<string, string> verified)
    {
        var fallback = element.GetProperty("fallback");
        if (fallback.GetProperty("rectangle").ValueKind != JsonValueKind.Array) return;
        var phoneRect = Rect(fallback.GetProperty("rectangle"));
        var output = element.GetProperty("output");
        var content = Rect(output.GetProperty("contentRect"));
        var size = output.GetProperty("size");
        var logical = output.GetProperty("logicalSize");
        var outputHash = output.GetProperty("sha256").GetString()!;
        if (phoneRect.Width <= 0 || phoneRect.Height <= 0 || content.Width <= 0 || content.Height <= 0 ||
            logical[0].GetInt32() != phoneRect.Width || logical[1].GetInt32() != phoneRect.Height ||
            content.X < 0 || content.Y < 0 || content.Right > size[0].GetInt32() || content.Bottom > size[1].GetInt32())
            throw new InvalidDataException($"Prepared art geometry mismatch: {element.GetProperty("id").GetString()}");
        if (!verified.TryGetValue(file, out var hash))
        {
            hash = Hash(file);
            verified.Add(file, hash);
        }
        if (hash != outputHash)
            throw new InvalidDataException($"Prepared art pixel hash mismatch: {file}");
        var ratio = new Vector2(phoneRect.Width / (float)content.Width, phoneRect.Height / (float)content.Height);
        foreach (var alternative in fallback.GetProperty("alternatives").EnumerateArray())
        {
            var key = new Key(Normalize(alternative.GetProperty("path").GetString()!), phoneRect);
            var route = new Route(file, content, ratio, element.GetProperty("id").GetString()!);
            if (routes.TryGetValue(key, out var prior))
            {
                if (prior.File != file || prior.Content != content || prior.Ratio != ratio)
                    throw new InvalidDataException($"Ambiguous prepared art route: {key.Path} {key.Rectangle}");
            }
            else routes.Add(key, route);
        }
    }

    private static string ArtPath(string root, string kind, string generation, JsonElement output)
    {
        var relative = output.GetProperty("path").GetString()!;
        if (!relative.StartsWith("art/", StringComparison.Ordinal) || relative.Contains("..", StringComparison.Ordinal) || relative.Contains('\\'))
            throw new InvalidDataException("Unsafe prepared art path.");
        return Path.Combine(root, kind, generation, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void LoadSource(string root, string kind, Dictionary<Key, Route> routes, bool announce = true)
    {
        var generation = Generation(root, kind);
        using var manifest = ReadManifest(Path.Combine(root, kind, generation, "manifest.json"), generation);
        if (manifest.RootElement.GetProperty("sourceId").GetString() != kind)
            throw new InvalidDataException($"Wrong prepared art source: {kind}");
        var verified = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in manifest.RootElement.GetProperty("elements").EnumerateArray())
        {
            if (row.GetProperty("selection").GetString() != kind) continue;
            Add(row, ArtPath(root, kind, generation, row.GetProperty("output")), routes, verified);
        }
        if (announce) Console.WriteLine($"ART_SOURCE kind={kind} routes={routes.Count} generation={generation}");
    }

    private static void LoadEndless(string root, Dictionary<Key, Route> routes, bool announce = true)
    {
        const string kind = "endless-360";
        var generation = Generation(root, kind);
        using var manifest = ReadManifest(Path.Combine(root, kind, generation + ".json"), generation);
        var verified = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in manifest.RootElement.GetProperty("elements").EnumerateArray())
        {
            var source = row.GetProperty("selection").GetString()!;
            if (source == "phone") continue;
            if (source is not ("i-maed-360" or "i-maed-steam" or "time-viking-360") ||
                Generation(root, source) != row.GetProperty("generation").GetString())
                throw new InvalidDataException("Endless art source is no longer current.");
            Add(row, ArtPath(root, source, row.GetProperty("generation").GetString()!, row.GetProperty("output")), routes, verified);
        }
        if (announce) Console.WriteLine($"ART_SOURCE kind=endless-360 routes={routes.Count} generation={generation}");
    }

    private static Dictionary<Key, Route>? CurrentRoutes()
    {
        if (stateField is null)
        {
            stateField = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("IMAK3Z0MB1EGAEM.director.GameState", false))
                .FirstOrDefault(t => t is not null)?
                .GetField("state", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (stateField is null) return null;
        }
        return (int)stateField.GetValue(null)! switch
        {
            2 or 3 => Normal,
            4 or 5 => Viking,
            6 or 7 => Endless,
            _ => null
        };
    }

    private static bool RouteDraw(SpriteBatch sprite, ref Texture2D texture, ref Rectangle? source, out Vector2 ratio)
    {
        ratio = Vector2.One;
        var routes = CurrentRoutes();
        if (routes is null || routes.Count == 0 || !Names.TryGetValue(texture, out var name))
        {
            Interlocked.Increment(ref originalDraws);
            return false;
        }
        var logical = source ?? new Rectangle(0, 0, texture.Width, texture.Height);
        var exact = routes.TryGetValue(new Key(name, logical), out var route);
        var phoneRect = logical;
        if (!exact)
        {
            foreach (var entry in routes)
            {
                if (entry.Key.Path != name || !entry.Value.Id.StartsWith("background/i-maed/", StringComparison.Ordinal))
                    continue;
                route = entry.Value;
                phoneRect = entry.Key.Rectangle;
                break;
            }
        }
        if (route is null)
        {
            Interlocked.Increment(ref originalDraws);
            return false;
        }
        if (exact)
        {
            source = route.Content;
            ratio = route.Ratio;
        }
        else
        {
            var x = route.Content.X + (int)Math.Round((logical.X - phoneRect.X) / route.Ratio.X);
            var y = route.Content.Y + (int)Math.Round((logical.Y - phoneRect.Y) / route.Ratio.Y);
            var w = (int)Math.Round(logical.Width / route.Ratio.X);
            var h = (int)Math.Round(logical.Height / route.Ratio.Y);
            // The original map draw intentionally pads its camera crop by four
            // pixels. At a map edge that crop can extend outside the texture;
            // SpriteBatch's clamp sampler repeats the edge pixels there. Keep
            // those UVs beyond the enhanced texture too, instead of falling
            // back to the lower-resolution phone background.
            source = new Rectangle(x, y, w, h);
            ratio = new Vector2(logical.Width / (float)w, logical.Height / (float)h);
        }
        texture = GetRouteTexture(sprite, route);
        Used.AddOrUpdate(route.Id, 1, (_, count) => count + 1);
        Interlocked.Increment(ref replacements);
        return true;
    }

    private static Texture2D GetRouteTexture(SpriteBatch sprite, Route route)
    {
        if (route.Texture is null || route.Texture.IsDisposed)
        {
            using var stream = File.OpenRead(route.File);
            route.Texture = Texture2D.FromStream(sprite.GraphicsDevice, stream);
            if (route.Texture.Width < route.Content.Right || route.Texture.Height < route.Content.Bottom)
                throw new InvalidDataException($"Prepared art bounds mismatch: {route.Id}");
        }
        return route.Texture;
    }

    private static void Draw360CharacterShadow(SpriteBatch sprite, Texture2D texture,
        Vector2 position, Rectangle? source)
    {
        var routes = CurrentRoutes();
        if (routes is null || !Names.TryGetValue(texture, out var name) ||
            name != "gfx_2/zombie/sprites.xnb") return;
        var logical = source ?? new Rectangle(0, 0, texture.Width, texture.Height);
        if (!routes.TryGetValue(new Key(name, logical), out var body) ||
            !(body.Id.StartsWith("frame/IMAK3Z0MB1EGAEM.character/Hero/001/", StringComparison.Ordinal) ||
              body.Id is "draw/IMAK3Z0MB1EGAEM.character/Monster/008" or
                         "draw/IMAK3Z0MB1EGAEM.character/Monster/009" or
                         "draw/IMAK3Z0MB1EGAEM.character/Monster/010") ||
            !routes.TryGetValue(new Key(name, new Rectangle(288, 64, 96, 96)), out var shadow) ||
            !shadow.File.Contains("i-maed-360", StringComparison.OrdinalIgnoreCase)) return;
        zoomField ??= stateField?.DeclaringType?.Assembly
            .GetType("IMAK3Z0MB1EGAEM.ScrollMan", false)?
            .GetField("zoom", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (zoomField?.GetValue(null) is not float zoom) return;
        // The 360 game draws this clean circular shadow at each living character's
        // location before its sprite. The phone game instead baked a clipped shadow
        // into the character atlas and left its own DrawShadow method unused.
        sprite.Draw(GetRouteTexture(sprite, shadow), position, shadow.Content,
            new Color(0f, 0f, 0f, 0.5f), 0f, new Vector2(96f, 96f),
            0.4f * zoom, SpriteEffects.None, 1f);
        Used.AddOrUpdate(shadow.Id, 1, (_, count) => count + 1);
        Interlocked.Increment(ref replacements);
    }

    private static bool IsPhoneCharacter(Rectangle source) =>
        (source.Y == 0 && source.Width == 128 && source.Height == 64 &&
         source.X is 0 or 128 or 256 or 384 or 512) ||
        source == new Rectangle(192, 64, 96, 96) ||
        source == new Rectangle(416, 192, 96, 128) ||
        source == new Rectangle(576, 0, 64, 96);

    private static Texture2D GetPhoneShadow(SpriteBatch sprite, Texture2D atlas)
    {
        if (PhoneShadows.TryGetValue(atlas, out var shadow) && !shadow.IsDisposed) return shadow;
        const int size = 96;
        var pixels = new Color[size * size];
        atlas.GetData(0, new Rectangle(288, 64, size, size), pixels, 0, pixels.Length);
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var index = y * size + x;
            var edge = Math.Min(Math.Min(x, size - 1 - x), Math.Min(y, size - 1 - y));
            pixels[index].A = (byte)(pixels[index].A * Math.Min(edge, 12) / 12);
        }
        shadow = new Texture2D(sprite.GraphicsDevice, size, size);
        shadow.SetData(pixels);
        PhoneShadows[atlas] = shadow;
        return shadow;
    }

    private static bool TryDrawPhoneCharacter(SpriteBatch sprite, Texture2D atlas, Vector2 position,
        Rectangle? source, Color color, float rotation, Vector2 origin, Vector2 scale,
        SpriteEffects effects, float depth)
    {
        if (CurrentRoutes()?.Count != 0 || source is not Rectangle frame || !IsPhoneCharacter(frame) ||
            !Names.TryGetValue(atlas, out var name) || name != "gfx_2/zombie/sprites.xnb") return false;

        if (!PhoneCharacters.TryGetValue((atlas, frame), out var character) || character.IsDisposed)
        {
            var pixels = new Color[frame.Width * frame.Height];
            atlas.GetData(0, frame, pixels, 0, pixels.Length);
            // Opaque pixels form the character, including its dark outlines.
            // The broad baked shadow has lower alpha. Keep a short soft fringe
            // around the character and discard the rest of that shadow.
            var distance = new byte[pixels.Length];
            Array.Fill(distance, (byte)6);
            var frontier = new Queue<int>();
            for (var index = 0; index < pixels.Length; index++)
                if (pixels[index].A >= 235)
                {
                    distance[index] = 0;
                    frontier.Enqueue(index);
                }
            while (frontier.Count > 0)
            {
                var index = frontier.Dequeue();
                var next = (byte)(distance[index] + 1);
                if (next > 5) continue;
                var x = index % frame.Width;
                var y = index / frame.Width;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (nx < 0 || nx >= frame.Width || ny < 0 || ny >= frame.Height) continue;
                    var neighbor = ny * frame.Width + nx;
                    if (distance[neighbor] <= next) continue;
                    distance[neighbor] = next;
                    frontier.Enqueue(neighbor);
                }
            }
            for (var y = 0; y < frame.Height; y++)
            for (var x = 0; x < frame.Width; x++)
            {
                var index = y * frame.Width + x;
                var pixel = pixels[index];
                if (pixel.A >= 235) continue;
                var alpha = pixel.A * Math.Min(4, 6 - distance[index]) / 4;
                // A few body pixels sit close to a frame edge. Remove only the
                // remaining dark translucent fringe there, leaving those pixels.
                if (Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) <= 32)
                {
                    var edge = Math.Min(Math.Min(x, frame.Width - 1 - x),
                                        Math.Min(y, frame.Height - 1 - y));
                    alpha = alpha * Math.Min(edge, 10) / 10;
                }
                pixels[index].A = (byte)alpha;
            }
            character = new Texture2D(sprite.GraphicsDevice, frame.Width, frame.Height);
            character.SetData(pixels);
            PhoneCharacters[(atlas, frame)] = character;
        }

        zoomField ??= stateField?.DeclaringType?.Assembly
            .GetType("IMAK3Z0MB1EGAEM.ScrollMan", false)?
            .GetField("zoom", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (zoomField?.GetValue(null) is float zoom)
            sprite.Draw(GetPhoneShadow(sprite, atlas), position, null,
                new Color(0f, 0f, 0f, 0.5f), 0f, new Vector2(48f, 48f),
                0.8f * zoom, SpriteEffects.None, depth);
        sprite.Draw(character, position, null, color, rotation, origin, scale, effects, depth);
        Interlocked.Increment(ref originalDraws);
        return true;
    }

    public static void Draw(SpriteBatch sprite, Texture2D texture, Vector2 position, Color color)
    {
        Rectangle? source = null;
        if (RouteDraw(sprite, ref texture, ref source, out var ratio))
            sprite.Draw(texture, position, source, color, 0f, Vector2.Zero, ratio, SpriteEffects.None, 0f);
        else sprite.Draw(texture, position, color);
    }

    public static void Draw(SpriteBatch sprite, Texture2D texture, Vector2 position, Rectangle? source, Color color)
    {
        if (RouteDraw(sprite, ref texture, ref source, out var ratio))
            sprite.Draw(texture, position, source, color, 0f, Vector2.Zero, ratio, SpriteEffects.None, 0f);
        else sprite.Draw(texture, position, source, color);
    }

    public static void Draw(SpriteBatch sprite, Texture2D texture, Vector2 position, Rectangle? source, Color color,
        float rotation, Vector2 origin, float scale, SpriteEffects effects, float depth)
    {
        if (DesktopPictures.TryDrawPicture(sprite, texture, position, source, color,
                rotation, origin, scale, effects, depth)) return;
        if (TryDrawPhoneCharacter(sprite, texture, position, source, color, rotation, origin,
                new Vector2(scale), effects, depth)) return;
        Draw360CharacterShadow(sprite, texture, position, source);
        if (RouteDraw(sprite, ref texture, ref source, out var ratio))
            sprite.Draw(texture, position, source, color, rotation,
                new Vector2(origin.X / ratio.X, origin.Y / ratio.Y), scale * ratio, effects, depth);
        else sprite.Draw(texture, position, source, color, rotation, origin, scale, effects, depth);
    }

    public static void Draw(SpriteBatch sprite, Texture2D texture, Vector2 position, Rectangle? source, Color color,
        float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float depth)
    {
        if (TryDrawPhoneCharacter(sprite, texture, position, source, color, rotation, origin,
                scale, effects, depth)) return;
        Draw360CharacterShadow(sprite, texture, position, source);
        if (RouteDraw(sprite, ref texture, ref source, out var ratio))
            sprite.Draw(texture, position, source, color, rotation,
                new Vector2(origin.X / ratio.X, origin.Y / ratio.Y), scale * ratio, effects, depth);
        else sprite.Draw(texture, position, source, color, rotation, origin, scale, effects, depth);
    }

    public static void Draw(SpriteBatch sprite, Texture2D texture, Rectangle destination, Color color)
    {
        Rectangle? source = null;
        if (RouteDraw(sprite, ref texture, ref source, out _)) sprite.Draw(texture, destination, source, color);
        else sprite.Draw(texture, destination, color);
    }

    public static void Draw(SpriteBatch sprite, Texture2D texture, Rectangle destination, Rectangle? source, Color color)
    {
        RouteDraw(sprite, ref texture, ref source, out _);
        sprite.Draw(texture, destination, source, color);
    }

    public static void Draw(SpriteBatch sprite, Texture2D texture, Rectangle destination, Rectangle? source, Color color,
        float rotation, Vector2 origin, SpriteEffects effects, float depth)
    {
        if (RouteDraw(sprite, ref texture, ref source, out var ratio))
            origin = new Vector2(origin.X / ratio.X, origin.Y / ratio.Y);
        sprite.Draw(texture, destination, source, color, rotation, origin, effects, depth);
    }
}
