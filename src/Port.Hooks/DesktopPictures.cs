using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Port.Hooks;

/// <summary>Imports desktop image thumbnails into the original creator source list.</summary>
public static class DesktopPictures
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    // The phone creator uses a 100-unit coordinate space. Keep four source
    // pixels per unit so the enlarged desktop preview and monster stay sharp.
    private const int ThumbnailSize = 400;
    private const int MaximumPictures = 256;
    private static readonly HashSet<Texture2D> OwnedTextures = [];
    private static readonly HashSet<Texture2D> SourceTextures = [];
    private static readonly Dictionary<Texture2D, float> MaskTextures = [];
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg" };
    private static string? pictureFolder;
    private static object? laidOutMapper;
    private static object? laidOutItems;
    private static float laidOutOffset;
    private static int laidOutIndex = -1;
    private static bool laidOutEnabled;
    private static object? controlledMapper;
    private static bool sliderDragging;
    private static bool previousSliderMouse;
    private static bool previousLeftShoulder;
    private static bool previousRightShoulder;
    private static string LegacyFolderFile => Path.Combine(DesktopStorage.SaveDirectory, "pictures-folder.txt");

    public static void Configure(string? folder) =>
        pictureFolder = string.IsNullOrWhiteSpace(folder) ? null : Path.GetFullPath(folder);

    public static bool ImportFolder(object manager, string folder) => Import(manager, folder);

    // Both the creator and monster use the phone's 100-unit image geometry.
    // Adjust only desktop picture textures when the original Draw calls run.
    public static bool TryDrawPicture(SpriteBatch sprite, Texture2D texture, Vector2 position,
        Rectangle? source, Color color, float rotation, Vector2 origin, float scale,
        SpriteEffects effects, float depth)
    {
        if (SourceTextures.Contains(texture))
            scale *= 100f / Math.Max(texture.Width, texture.Height);
        else if (MaskTextures.TryGetValue(texture, out var scaleCorrection))
        {
            var resolution = Math.Max(texture.Width, texture.Height) / 100f;
            if (source is { Width: 100, Height: 100 })
                source = new Rectangle(0, 0, texture.Width, texture.Height);
            origin *= resolution;
            scale = scale / resolution * scaleCorrection;
        }
        else return false;
        sprite.Draw(texture, position, source, color, rotation, origin, scale, effects, depth);
        return true;
    }

    public static void ProcessCustomImage(object image, object source, GraphicsDevice device)
    {
        var sourceType = source.GetType();
        var texture = (Texture2D)sourceType.GetMethod("GetTexture", InstanceFields)!.Invoke(source, null)!;
        var imageType = image.GetType();
        var old = (Texture2D?)imageType.GetField("texture", InstanceFields)!.GetValue(image);
        if (old is not null)
        {
            MaskTextures.Remove(old);
            old.Dispose();
        }
        var pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);
        var point = (Vector2)imageType.GetField("point", InstanceFields)!.GetValue(image)!;
        var size = (float)imageType.GetField("size", InstanceFields)!.GetValue(image)!;
        var resolution = Math.Max(texture.Width, texture.Height) / 100f;
        var logicalWidth = texture.Width / resolution;
        var logicalHeight = texture.Height / resolution;
        var previousId = (string?)imageType.GetField("ID", InstanceFields)!.GetValue(image);
        var sourceId = (string?)sourceType.GetField("ID", InstanceFields)!.GetValue(source);
        if (SourceTextures.Contains(texture) &&
            (!string.Equals(previousId, sourceId, StringComparison.Ordinal) ||
             point.X < 0 || point.Y < 0 || point.X >= logicalWidth || point.Y >= logicalHeight))
        {
            point = new Vector2(logicalWidth / 2f, logicalHeight / 2f);
            imageType.GetField("point", InstanceFields)!.SetValue(image, point);
        }
        var scaleCorrection = 1f;
        float radius;
        if (SourceTextures.Contains(texture))
        {
            size = Math.Clamp(size, 0f, 0.5f);
            imageType.GetField("size", InstanceFields)!.SetValue(image, size);
            (point, radius, scaleCorrection) = FitMaskGeometry(texture.Width, texture.Height, point, size);
            imageType.GetField("point", InstanceFields)!.SetValue(image, point);
        }
        else radius = (0.1f + size) * 100f;
        for (var y = 0; y < texture.Height; y++)
        for (var x = 0; x < texture.Width; x++)
        {
            var distance = Vector2.Distance(new Vector2(x / resolution, y / resolution), point);
            var coverage = Math.Clamp((radius - distance) / 2f, 0f, 1f);
            var index = x + y * texture.Width;
            pixels[index].A = (byte)MathF.Round(pixels[index].A * coverage);
        }
        var masked = new Texture2D(device, texture.Width, texture.Height, false, texture.Format);
        masked.SetData(pixels);
        imageType.GetField("texture", InstanceFields)!.SetValue(image, masked);
        imageType.GetField("ID", InstanceFields)!.SetValue(image, sourceId);
        imageType.GetField("enabled", InstanceFields)!.SetValue(image, true);
        MaskTextures.Add(masked, scaleCorrection);
    }

    // The phone slider can request a circle larger than a portrait photo's
    // width. Fit that circle inside the actual picture and preserve its drawn
    // size in the preview and during play.
    public static (Vector2 Center, float Radius, float ScaleCorrection) FitMaskGeometry(
        int width, int height, Vector2 requestedCenter, float size)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        var resolution = Math.Max(width, height) / 100f;
        var bounds = new Vector2(width / resolution, height / resolution);
        var maximumRadius = MathF.Min(bounds.X, bounds.Y) / 2f;
        var phoneRadius = (0.1f + Math.Clamp(size, 0f, 0.5f)) * 100f;
        var radius = maximumRadius * phoneRadius / 60f;
        var center = Vector2.Clamp(requestedCenter, new Vector2(radius), bounds - new Vector2(radius));
        return (center, radius, phoneRadius / radius);
    }

    public static void LayoutMapper(object mapper)
    {
        var type = mapper.GetType();
        var game = type.Assembly.GetType("ZombiesWP7.Game1", throwOnError: true)!;
        var scale = (float)game.GetField("MENUSCALE", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
        var offset = (DesktopDisplay.SceneSize.X - DesktopDisplay.LogicalWidth) / (2f * scale);
        var items = (Array?)type.BaseType!.GetField("item", InstanceFields)!.GetValue(mapper);
        var manager = game.GetField("custom", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
        var index = (int)type.GetField("curPic", InstanceFields)!.GetValue(mapper)!;
        var count = (int)manager.GetType().GetMethod("GetSrcImageCount", InstanceFields)!.Invoke(manager, null)!;
        var enabled = count > index && (bool)manager.GetType()
            .GetMethod("GetSrcEnabled", InstanceFields)!.Invoke(manager, [index])!;
        if (ReferenceEquals(laidOutMapper, mapper) && ReferenceEquals(laidOutItems, items) &&
            laidOutOffset == offset && laidOutIndex == index && laidOutEnabled == enabled) return;

        type.GetField("orig", InstanceFields)!.SetValue(mapper,
            new Vector2((enabled ? 150 : 240) + offset, 190));
        type.GetField("rOrig", InstanceFields)!.SetValue(mapper, new Vector2(330 + offset, 190));
        if (items is not null)
        {
            var positions = new[] { new Vector2(30 + offset, 180),
                new Vector2(450 + offset, 180), new Vector2(240 + offset, 280) };
            for (var i = 0; i < Math.Min(items.Length, positions.Length); i++)
            {
                var item = items.GetValue(i)!;
                item.GetType().GetField("loc", InstanceFields)!.SetValue(item, positions[i]);
                item.GetType().GetField("tiltLoc", InstanceFields)!.SetValue(item, positions[i]);
            }
        }
        laidOutMapper = mapper;
        laidOutItems = items;
        laidOutOffset = offset;
        laidOutIndex = index;
        laidOutEnabled = enabled;
    }

    public static void UpdateMapperControls(object? mapper, Vector2? pointer, bool mousePressed,
        GamePadState pad, float frameTime)
    {
        if (!ReferenceEquals(controlledMapper, mapper))
        {
            controlledMapper = mapper;
            sliderDragging = previousSliderMouse = false;
            previousLeftShoulder = previousRightShoulder = false;
        }
        if (mapper is null) return;
        LayoutMapper(mapper);
        if (mousePressed && !previousSliderMouse && pointer is Vector2 press && SliderHit(mapper, press))
            sliderDragging = true;
        if (!mousePressed) sliderDragging = false;
        previousSliderMouse = mousePressed;
        if (sliderDragging && pointer is Vector2 drag)
            SetSliderSize(mapper, drag);

        var leftShoulder = pad.Buttons.LeftShoulder == ButtonState.Pressed;
        var rightShoulder = pad.Buttons.RightShoulder == ButtonState.Pressed;
        var step = (rightShoulder && !previousRightShoulder ? 0.02f : 0f) -
                   (leftShoulder && !previousLeftShoulder ? 0.02f : 0f);
        previousLeftShoulder = leftShoulder;
        previousRightShoulder = rightShoulder;
        var stick = pad.ThumbSticks.Right.X;
        if (MathF.Abs(stick) > 0.2f)
            step += stick * 0.4f * Math.Clamp(frameTime, 0f, 0.05f);
        if (step != 0f) AdjustSliderSize(mapper, step);
    }

    private static bool SliderHit(object mapper, Vector2 position)
    {
        var type = mapper.GetType();
        var game = type.Assembly.GetType("ZombiesWP7.Game1", true)!;
        var scale = (float)game.GetField("MENUSCALE", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
        var center = ((Vector2)type.GetField("rOrig", InstanceFields)!.GetValue(mapper)! +
            new Vector2(0, 60)) * scale;
        return MathF.Abs(position.X - center.X) <= 58f * scale &&
            MathF.Abs(position.Y - center.Y) <= 18f * scale;
    }

    private static bool SetSliderSize(object mapper, Vector2 position)
    {
        if (!SliderHit(mapper, position) && !sliderDragging) return false;
        var type = mapper.GetType();
        var game = type.Assembly.GetType("ZombiesWP7.Game1", true)!;
        var scale = (float)game.GetField("MENUSCALE", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
        var center = ((Vector2)type.GetField("rOrig", InstanceFields)!.GetValue(mapper)! +
            new Vector2(0, 60)) * scale;
        var size = Math.Clamp(((position.X - center.X) / scale + 50f) / 200f, 0f, 0.5f);
        ApplySliderSize(mapper, size);
        return true;
    }

    private static void AdjustSliderSize(object mapper, float change)
    {
        var type = mapper.GetType();
        var game = type.Assembly.GetType("ZombiesWP7.Game1", true)!;
        var manager = game.GetField("custom", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
        var index = (int)type.GetField("curPic", InstanceFields)!.GetValue(mapper)!;
        if (!(bool)manager.GetType().GetMethod("GetSrcEnabled", InstanceFields)!.Invoke(manager, [index])!) return;
        var custom = manager.GetType().GetMethod("GetCustomImageFromSrcIdx", InstanceFields)!.Invoke(manager, [index]);
        if (custom is null) return;
        var size = (float)custom.GetType().GetField("size", InstanceFields)!.GetValue(custom)!;
        ApplySliderSize(mapper, Math.Clamp(size + change, 0f, 0.5f));
    }

    private static void ApplySliderSize(object mapper, float size)
    {
        var type = mapper.GetType();
        var game = type.Assembly.GetType("ZombiesWP7.Game1", true)!;
        var manager = game.GetField("custom", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(null)!;
        var index = (int)type.GetField("curPic", InstanceFields)!.GetValue(mapper)!;
        if (!(bool)manager.GetType().GetMethod("GetSrcEnabled", InstanceFields)!.Invoke(manager, [index])!) return;
        var custom = manager.GetType().GetMethod("GetCustomImageFromSrcIdx", InstanceFields)!.Invoke(manager, [index]);
        if (custom is null) return;
        var field = custom.GetType().GetField("size", InstanceFields)!;
        if (MathF.Abs((float)field.GetValue(custom)! - size) < 0.0005f) return;
        field.SetValue(custom, size);
        manager.GetType().GetMethod("ProcessCustom", InstanceFields)!.Invoke(manager, [index]);
        type.BaseType!.GetField("infos", InstanceFields)!.SetValue(mapper,
            new[] { new StringBuilder("Drag slider or use right stick / shoulder buttons for mask size.") });
    }

    // The phone UI only made the small USE label clickable. Make the image
    // itself select the source, as desktop players naturally expect.
    public static bool ClickMapperPicture(object mapper, Vector2 position)
    {
        LayoutMapper(mapper);
        var type = mapper.GetType();
        var game = type.Assembly.GetType("ZombiesWP7.Game1", throwOnError: true)!;
        var staticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var manager = game.GetField("custom", staticFields)!.GetValue(null)!;
        var index = (int)type.GetField("curPic", InstanceFields)!.GetValue(mapper)!;
        var texture = (Texture2D?)manager.GetType().GetMethod("GetSrcTexture", InstanceFields)!.Invoke(manager, [index]);
        if (texture is null) return false;
        var scale = (float)game.GetField("MENUSCALE", staticFields)!.GetValue(null)!;
        if (SetSliderSize(mapper, position)) return true;
        var center = (Vector2)type.GetField("orig", InstanceFields)!.GetValue(mapper)! * scale;
        var resolution = Math.Max(texture.Width, texture.Height) / 100f;
        var halfWidth = texture.Width / resolution * 0.75f * scale;
        var halfHeight = texture.Height / resolution * 0.75f * scale;
        if (MathF.Abs(position.X - center.X) > halfWidth ||
            MathF.Abs(position.Y - center.Y) > halfHeight) return false;
        var enabled = (bool)manager.GetType().GetMethod("GetSrcEnabled", InstanceFields)!.Invoke(manager, [index])!;
        if (!enabled)
            type.GetMethod("Click", InstanceFields)!.Invoke(mapper, ["use"]);
        else
        {
            // The original hit test uses unscaled pixels. Convert the click
            // to the source's 100-unit mask coordinates on every resolution.
            var custom = manager.GetType().GetMethod("GetCustomImageFromSrcIdx", InstanceFields)!.Invoke(manager, [index]);
            if (custom is null) return false;
            var relative = (position - center) / (1.5f * scale);
            var bounds = new Vector2(texture.Width / resolution, texture.Height / resolution);
            var point = Vector2.Clamp(bounds / 2f + relative, Vector2.Zero, bounds);
            custom.GetType().GetField("point", InstanceFields)!.SetValue(custom, point);
            manager.GetType().GetMethod("ProcessCustom", InstanceFields)!.Invoke(manager, [index]);
            type.BaseType!.GetField("infos", InstanceFields)!.SetValue(mapper,
                new[] { new StringBuilder("Monster mask positioned. Click USE to remove it.") });
        }
        return true;
    }

    public static void LoadRememberedFolder(object manager)
    {
        if ((int)manager.GetType().GetMethod("GetSrcImageCount", InstanceFields)!.Invoke(manager, null)! > 0)
            return;
        try
        {
            var folder = pictureFolder ?? (File.Exists(LegacyFolderFile)
                ? File.ReadAllText(LegacyFolderFile).Trim() : null);
            if (!string.IsNullOrWhiteSpace(folder) && Import(manager, folder))
                DesktopStorage.ReapplyCustom(manager);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not load picture folder: {error.Message}");
        }
    }
    private static bool Import(object manager, string folder)
    {
        if (!Directory.Exists(folder)) return false;
        var managerType = manager.GetType();
        var sourcesField = managerType.GetField("srcImages", InstanceFields)!;
        var device = (GraphicsDevice)managerType.GetField("gd", InstanceFields)!.GetValue(manager)!;
        var sources = (IList)Activator.CreateInstance(sourcesField.FieldType)!;
        var sourceType = sourcesField.FieldType.GenericTypeArguments[0];
        var constructor = sourceType.GetConstructor([typeof(string), typeof(Texture2D)])!;
        var imageIndex = sourceType.GetField("imgIdx", InstanceFields)!;
        var sourceId = sourceType.GetField("ID", InstanceFields)!;
        var sourceTexture = sourceType.GetField("texture", InstanceFields)!;
        var loaded = new List<Texture2D>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", new EnumerationOptions
                     { RecurseSubdirectories = true, IgnoreInaccessible = true })
                     .Where(file => Extensions.Contains(Path.GetExtension(file)))
                     .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
            {
                if (sources.Count == MaximumPictures) break;
                try
                {
                    if (new FileInfo(file).Length > 32 * 1024 * 1024) continue;
                    using var stream = File.OpenRead(file);
                    var thumbnail = Texture2D.FromStream(device, stream,
                        ThumbnailSize, ThumbnailSize, zoom: false);
                    loaded.Add(thumbnail);
                    SourceTextures.Add(thumbnail);
                    var id = "file:" + Convert.ToHexString(SHA256.HashData(
                        Encoding.UTF8.GetBytes(Path.GetFullPath(file).ToUpperInvariant()))).ToLowerInvariant();
                    sources.Add(constructor.Invoke([id, thumbnail]));
                }
                catch (Exception error)
                {
                    Trace.TraceWarning($"Skipping picture {file}: {error.Message}");
                }
            }
            if (sources.Count == 0) return false;

            var previous = (IList?)sourcesField.GetValue(manager);
            sourcesField.SetValue(manager, sources);
            var custom = (Array)managerType.GetField("customImg", InstanceFields)!.GetValue(manager)!;
            foreach (var image in custom)
            {
                var type = image!.GetType();
                if (!(bool)type.GetField("enabled", InstanceFields)!.GetValue(image)!) continue;
                var id = (string?)type.GetField("ID", InstanceFields)!.GetValue(image);
                var match = -1;
                for (var i = 0; i < sources.Count; i++)
                    if (string.Equals(id, (string?)sourceId.GetValue(sources[i]), StringComparison.Ordinal))
                    { match = i; break; }
                if (match < 0)
                {
                    type.GetField("enabled", InstanceFields)!.SetValue(image, false);
                    continue;
                }
                var slot = Array.IndexOf(custom, image);
                imageIndex.SetValue(sources[match], slot);
                type.GetField("srcIdx", InstanceFields)!.SetValue(image, match);
                type.GetMethod("Process", InstanceFields)!.Invoke(image, [sources[match], device]);
            }
            managerType.GetMethod("SetGameCustomMap", InstanceFields)!.Invoke(manager, null);
            if (previous is not null)
                foreach (var old in previous)
                    if (sourceTexture.GetValue(old) is Texture2D texture && OwnedTextures.Remove(texture))
                    {
                        SourceTextures.Remove(texture);
                        texture.Dispose();
                    }
            foreach (var texture in loaded) OwnedTextures.Add(texture);
            Trace.TraceInformation($"Imported {sources.Count} creator pictures from {folder}.");
            return true;
        }
        finally
        {
            if (sources.Count == 0)
                foreach (var texture in loaded) texture.Dispose();
        }
    }

}
