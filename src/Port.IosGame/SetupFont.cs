using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Port.IosGame;

// Authored 5x7 glyphs. Setup has no dependency on imported fonts or textures.
internal sealed class SetupFont : IDisposable
{
    private readonly Texture2D atlas;
    private const string Glyphs = """
        A=0E11111F111111 B=1E11111E11111E C=0F10101010100F D=1E11111111111E
        E=1F10101E10101F F=1F10101E101010 G=0F10101711110F H=1111111F111111
        I=0E04040404040E J=0101010111110E K=11121418141211 L=1010101010101F
        M=111B1515111111 N=11191513111111 O=0E11111111110E P=1E11111E101010
        Q=0E11111115120D R=1E11111E141211 S=0F10100E01011E T=1F040404040404
        U=1111111111110E V=11111111110A04 W=11111115151B11 X=11110A040A1111
        Y=11110A04040404 Z=1F01020408101F
        0=0E11131519110E 1=040C040404040E 2=0E11010204081F 3=1E01010E01011E
        4=02060A121F0202 5=1F10101E01011E 6=0E10101E11110E 7=1F010204080808
        8=0E11110E11110E 9=0E11110F01010E
        .=00000000000C0C ,=00000000000C08 :=000C0C000C0C00 -=0000001F000000
        /=01010204081010 \=10100804020101 ?=0E110102040004 !=04040404040004
        (=02040808080402 )=08040202020408 +=0004041F040400 _=0000000000001F
        [=0E08080808080E ]=0E02020202020E '=04040800000000 %=19190204081313
        """;

    internal SetupFont(GraphicsDevice device)
    {
        var pixels = new Color[128 * 6 * 8];
        foreach (var item in Glyphs.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var glyph = item[0];
            var code = item[2..];
            if (code.Length != 14) throw new InvalidDataException("Invalid setup glyph: " + glyph);
            for (var y = 0; y < 7; y++)
            {
                var bits = byte.Parse(code.AsSpan(y * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                for (var x = 0; x < 5; x++)
                    if ((bits & (1 << (4 - x))) != 0) pixels[y * 128 * 6 + glyph * 6 + x] = Color.White;
            }
        }
        atlas = new Texture2D(device, 128 * 6, 8);
        atlas.SetData(pixels);
    }

    internal static List<string> Wrap(string text, float width, float scale)
    {
        var columns = Math.Max(1, (int)(width / (6 * scale)));
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Replace("→", "/").Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > columns) { lines.Add(line); line = ""; }
                var remaining = word;
                while (remaining.Length > columns)
                {
                    if (line.Length > 0) { lines.Add(line); line = ""; }
                    lines.Add(remaining[..columns]); remaining = remaining[columns..];
                }
                if (remaining.Length > 0) line += (line.Length == 0 ? "" : " ") + remaining;
            }
            lines.Add(line);
        }
        return lines;
    }

    internal void Draw(SpriteBatch batch, string line, Vector2 position, float scale, Color ink)
    {
        foreach (var value in line)
        {
            var ch = char.ToUpperInvariant(value);
            if (ch > 127) ch = '?';
            if (ch != ' ')
                batch.Draw(atlas, position, new Rectangle(ch * 6, 0, 5, 7), ink, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
            position.X += 6 * scale;
        }
    }

    public void Dispose() => atlas.Dispose();
}
