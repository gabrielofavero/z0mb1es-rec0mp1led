using System.Collections;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Port.Hooks;

public static partial class DesktopDisplay
{
    private sealed record AchievementRow(string Key, string Name, string Description, bool Earned, float Y, float Height);
    private sealed record AchievementList(Array Items, SpriteFont Font, Vector2 Size, Vector2 Margin,
        float Top, float Bottom, float Maximum, AchievementRow[] Rows);
    private static AchievementList? achievementList;
    private static RasterizerState? achievementClip;

    private static string WrapAchievement(SpriteFont font, string text, float scale, float width)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && font.MeasureString(next).X * scale > width)
            {
                lines.Add(line);
                line = word;
            }
            else line = next;
        }
        if (line.Length > 0) lines.Add(line);
        return string.Join('\n', lines);
    }

    private static AchievementList? PortraitAchievements(object? level)
    {
        if (sceneSize.Height <= sceneSize.Width || level?.GetType().Name != "Achievements" ||
            itemsField?.GetValue(level) is not Array items || gameFontField?.GetValue(null) is not SpriteFont font) return null;
        var margin = HudMargin;
        if (achievementList is { } cached && ReferenceEquals(cached.Items, items) && ReferenceEquals(cached.Font, font) &&
            cached.Size == SceneSize && cached.Margin == margin) return cached;
        var top = margin.Y + 88f;
        var bottom = Math.Min(sceneSize.Height - margin.Y - 24f, sceneSize.Height * 0.9f - 80f);
        var width = sceneSize.Width - 2f * Math.Max(24f, margin.X) - 68f;
        var rows = new List<AchievementRow>();
        var y = 0f;
        foreach (var item in items)
        {
            if (item is null) continue;
            var type = item.GetType();
            var name = (string)type.GetField("text", Fields)!.GetValue(item)!;
            if (name.EndsWith("(0G)", StringComparison.Ordinal)) name = name[..^4].TrimEnd();
            name = WrapAchievement(font, name, 1.05f, width);
            var description = WrapAchievement(font, (string)type.GetField("desc", Fields)!.GetValue(item)!, 0.85f, width);
            var height = Math.Max(56f, font.MeasureString(name).Y * 1.05f + 8f + font.MeasureString(description).Y * 0.85f) + 22f;
            rows.Add(new AchievementRow((string)type.GetField("aIDx", Fields)!.GetValue(item)!, name, description,
                (bool)type.GetField("earned", Fields)!.GetValue(item)!, y, height));
            y += height;
        }
        return achievementList = new AchievementList(items, font, SceneSize, margin, top, bottom,
            Math.Max(0, y - (bottom - top)), rows.ToArray());
    }

    private static float? PortraitAchievementScrollLimit(object? level) => PortraitAchievements(level)?.Maximum;

    public static bool DrawPortraitAchievements(object level, SpriteBatch sprite)
    {
        if (PortraitAchievements(level) is not { } list || game is null || scene is null) return false;
        var device = sprite.GraphicsDevice;
        var alpha = (float)level.GetType().GetField("alpha", Fields)!.GetValue(level)!;
        var scrollField = level.GetType().GetField("scroll", Fields)!;
        var scroll = Math.Clamp((float)scrollField.GetValue(level)!, 0, list.Maximum);
        scrollField.SetValue(level, scroll);
        sprite.End();
        if (overlayPixel is null)
        {
            overlayPixel = new Texture2D(device, 1, 1);
            overlayPixel.SetData([Color.White]);
        }
        Begin(sprite, SpriteSortMode.Deferred, BlendState.AlphaBlend);
        sprite.Draw(overlayPixel, new Rectangle(0, 0, sceneSize.Width, sceneSize.Height), Color.Black * (0.95f * alpha));
        drawArrowText!.Invoke(null, [sprite, "ach1evements", new Vector2(sceneSize.Width / 2f, list.Margin.Y + 25f),
            6.5f, Color.White * alpha, arrowCenter!]);
        sprite.End();
        var oldScissor = device.ScissorRectangle;
        var placement = Presentation(scene.Width, scene.Height, sceneSize);
        device.ScissorRectangle = new Rectangle(0, (int)(placement.Offset.Y + list.Top * placement.Scale),
            scene.Width, Math.Max(1, (int)((list.Bottom - list.Top) * placement.Scale)));
        achievementClip ??= new RasterizerState { CullMode = CullMode.None, ScissorTestEnable = true };
        Begin(sprite, SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp,
            DepthStencilState.None, achievementClip);
        var textures = (IDictionary)game.GetType().GetField("achieveTextures",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        var x = Math.Max(24f, list.Margin.X);
        foreach (var row in list.Rows)
        {
            var y = list.Top + row.Y - scroll;
            if (y + row.Height < list.Top || y > list.Bottom) continue;
            var tint = Color.White * (alpha * (row.Earned ? 1f : 0.7f));
            if (textures[row.Key] is Texture2D icon)
                sprite.Draw(icon, new Rectangle((int)x, (int)y + 2, 52, 52), tint);
            var text = new Vector2(x + 68f, y);
            sprite.DrawString(list.Font, row.Name, text, tint, 0, Vector2.Zero, 1.05f, SpriteEffects.None, 0);
            text.Y += list.Font.MeasureString(row.Name).Y * 1.05f + 8f;
            sprite.DrawString(list.Font, row.Description, text, Color.LightGray * alpha,
                0, Vector2.Zero, 0.85f, SpriteEffects.None, 0);
        }
        sprite.End();
        device.ScissorRectangle = oldScissor;
        Begin(sprite, SpriteSortMode.Deferred, BlendState.Additive);
        return true;
    }
}
