using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework.Graphics;

namespace Port.Art;

internal sealed record Pixels(int Width, int Height, byte[] Rgba);

internal static class TexturePixels
{
    private static readonly Assembly Fna = typeof(Texture2D).Assembly;
    internal static byte[] Expand(byte[] bytes)
    {
        if (bytes.Length < 10 || bytes[0] != 'X' || bytes[1] != 'N' || bytes[2] != 'B' ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(6, 4)) != bytes.Length)
            throw new InvalidDataException("Invalid XNB header.");
        if ((bytes[5] & 0x80) == 0) return bytes;
        var size = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10, 4));
        if (size is <= 0 or > 128 * 1024 * 1024) throw new InvalidDataException("XNB exceeds the import memory bound.");
        using var compressed = new MemoryStream(bytes, 14, bytes.Length - 14, writable: false);
        using var expanded = new MemoryStream(new byte[size], writable: true);
        var type = Fna.GetType("Microsoft.Xna.Framework.Content.LzxDecoder", true)!;
        var decoder = type.GetConstructor([typeof(int)])!.Invoke([16]);
        var decompress = type.GetMethod("Decompress", BindingFlags.Public | BindingFlags.Instance)!;
        int Read() => compressed.ReadByte() is var value && value >= 0 ? value : throw new EndOfStreamException();
        while (compressed.Position < compressed.Length)
        {
            var hi = Read(); var lo = Read();
            var block = hi * 256 + lo;
            var frame = 0x8000;
            if (hi == 0xff) { frame = lo * 256 + Read(); block = Read() * 256 + Read(); }
            if (block == 0 || frame == 0) break;
            var end = compressed.Position + block;
            if (end > compressed.Length || expanded.Position + frame > size ||
                (int)decompress.Invoke(decoder, [compressed, block, expanded, frame])! != 0)
                throw new InvalidDataException("XNB LZX block failed.");
            compressed.Position = end;
        }
        if (expanded.Position != size) throw new InvalidDataException("XNB expanded length differs.");
        var output = new byte[10 + size];
        Array.Copy(bytes, output, 6);
        output[5] &= 0x7f;
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(6, 4), output.Length);
        expanded.ToArray().CopyTo(output, 10);
        return output;
    }

    internal static Pixels Decode(byte[] bytes, JsonNode meta)
    {
        // Xbox inventory pins describe the expanded XNB; phone/Steam pins describe
        // their original files, which may themselves use XNB compression.
        var originalPinned = bytes.LongLength == meta["bytes"]!.GetValue<long>() && ArtData.Hash(bytes) == meta.Text("sha256");
        bytes = Expand(bytes);
        if (!originalPinned) ArtData.Checked(bytes, meta, meta.Text("path"));
        using var reader = new BinaryReader(new MemoryStream(bytes, 10, bytes.Length - 10), Encoding.UTF8);
        var count = reader.Read7BitEncodedInt();
        if (count is <= 0 or > 16) throw new InvalidDataException("Unexpected XNB readers.");
        var names = new List<string>();
        for (var i = 0; i < count; i++) { names.Add(reader.ReadString()); reader.ReadInt32(); }
        if (reader.Read7BitEncodedInt() != 0) throw new InvalidDataException("Shared texture resources are unsupported.");
        var root = reader.Read7BitEncodedInt();
        if (root < 1 || root > names.Count || names[root - 1].Split(',')[0] != "Microsoft.Xna.Framework.Content.Texture2DReader")
            throw new InvalidDataException("Expected a pinned Texture2D reader.");
        var format = reader.ReadInt32();
        var width = reader.ReadInt32(); var height = reader.ReadInt32(); var mips = reader.ReadInt32();
        if (width != meta.Number("width") || height != meta.Number("height") || mips != meta.Number("mipCount") ||
            width <= 0 || height <= 0 || (long)width * height > 16 * 1024 * 1024 || mips != 1)
            throw new InvalidDataException("Texture shape differs from its pin.");
        var surface = meta.Text("surfaceFormat");
        var expected = bytes[4] < 5 ? surface switch { "Color" => 1, "Dxt1" => 28, "Dxt3" => 30, "Dxt5" => 32, _ => -1 } :
            surface switch { "Color" => 0, "Dxt1" => 4, "Dxt3" => 5, "Dxt5" => 6, _ => -1 };
        if (format != expected) throw new InvalidDataException("Texture format differs from its pin.");
        var length = reader.ReadInt32();
        if (length <= 0 || length > 64 * 1024 * 1024) throw new InvalidDataException("Invalid texture byte length.");
        var data = reader.ReadBytes(length);
        if (data.Length != length) throw new EndOfStreamException();
        if (bytes[3] == 'x') data = Invoke("X360TexUtil", "Swap" + surface, surface == "Color" ? [data] : [data, width, height]);
        if (surface != "Color") data = DecodeDxt(data, width, height, surface);
        else if (bytes[4] < 5)
            for (var i = 0; i < data.Length; i += 4) (data[i], data[i + 2]) = (data[i + 2], data[i]);
        if (data.Length != width * height * 4) throw new InvalidDataException("Decoded texture byte length differs.");
        return new(width, height, data);
    }

    private static byte[] Invoke(string typeName, string name, object[] args) => (byte[])Fna.GetType(
        "Microsoft.Xna.Framework.Graphics." + typeName, true)!.GetMethod(name,
            BindingFlags.Static | BindingFlags.NonPublic, null, args.Select(x => x.GetType()).ToArray(), null)!.Invoke(null, args)!;

    private static byte[] DecodeDxt(byte[] data, int width, int height, string format)
    {
        // Match the desktop artwork recipe's RGB565 bit expansion. FNA's CPU
        // decoder uses a different rounding rule that shifts some channels by 1.
        var stride = format == "Dxt1" ? 8 : 16;
        if (data.Length != ((width + 3) / 4) * ((height + 3) / 4) * stride)
            throw new InvalidDataException("DXT byte length differs.");
        var output = new byte[width * height * 4];
        var colors = new byte[16]; var alpha = new byte[8];
        var offset = 0;
        for (var by = 0; by < height; by += 4)
        for (var bx = 0; bx < width; bx += 4, offset += stride)
        {
            var block = data.AsSpan(offset, stride);
            var color = block[(stride - 8)..];
            var first = BinaryPrimitives.ReadUInt16LittleEndian(color);
            var second = BinaryPrimitives.ReadUInt16LittleEndian(color[2..]);
            for (var index = 0; index < 2; index++)
            {
                var packed = index == 0 ? first : second;
                var r = packed >> 11; var g = packed >> 5 & 63; var b = packed & 31;
                colors[index * 4] = (byte)(r << 3 | r >> 2);
                colors[index * 4 + 1] = (byte)(g << 2 | g >> 4);
                colors[index * 4 + 2] = (byte)(b << 3 | b >> 2);
                colors[index * 4 + 3] = 255;
            }
            var four = first > second || format != "Dxt1";
            for (var c = 0; c < 3; c++)
            {
                colors[8 + c] = (byte)(four ? (2 * colors[c] + colors[4 + c]) / 3 : (colors[c] + colors[4 + c]) / 2);
                colors[12 + c] = (byte)(four ? (colors[c] + 2 * colors[4 + c]) / 3 : 0);
            }
            colors[11] = 255; colors[15] = four ? (byte)255 : (byte)0;
            var indices = BinaryPrimitives.ReadUInt32LittleEndian(color[4..]);
            var alphaBits = stride == 16 ? BinaryPrimitives.ReadUInt64LittleEndian(block) : 0;
            if (format == "Dxt5")
            {
                alpha[0] = block[0]; alpha[1] = block[1];
                var seven = alpha[0] > alpha[1]; var denominator = seven ? 7 : 5;
                for (var a = 2; a < (seven ? 8 : 6); a++)
                    alpha[a] = (byte)(((denominator + 1 - a) * alpha[0] + (a - 1) * alpha[1]) / denominator);
                if (!seven) { alpha[6] = 0; alpha[7] = 255; }
                alphaBits >>= 16;
            }
            for (var pixel = 0; pixel < 16; pixel++)
            {
                var x = bx + pixel % 4; var y = by + pixel / 4;
                if (x >= width || y >= height) continue;
                var target = (y * width + x) * 4;
                colors.AsSpan((int)((indices >> (pixel * 2)) & 3) * 4, 4).CopyTo(output.AsSpan(target));
                if (format == "Dxt3") output[target + 3] = (byte)(((alphaBits >> (pixel * 4)) & 15) * 17);
                if (format == "Dxt5") output[target + 3] = alpha[(alphaBits >> (pixel * 3)) & 7];
            }
        }
        return output;
    }

    internal static byte[] CropPng(Pixels image, int[] rect, byte[]? vignette)
    {
        ArtData.Bounds(rect, image.Width, image.Height);
        var width = rect[2] + 4; var height = rect[3] + 4;
        var data = new byte[height * (1 + width * 4)];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sx = rect[0] + Math.Clamp(x - 2, 0, rect[2] - 1);
            var sy = rect[1] + Math.Clamp(y - 2, 0, rect[3] - 1);
            var source = (sy * image.Width + sx) * 4;
            var target = y * (1 + width * 4) + 1 + x * 4;
            for (var c = 0; c < 4; c++) data[target + c] = c < 3 && vignette is not null
                ? (byte)MathF.Round(image.Rgba[source + c] * (vignette[sy * image.Width + sx] / 255f), MidpointRounding.ToEven)
                : image.Rgba[source + c];
        }
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8; header[9] = 6;
        Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(data);
        Chunk(output, "IDAT", compressed.ToArray());
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string kind, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length); output.Write(number);
        var name = Encoding.ASCII.GetBytes(kind); output.Write(name); output.Write(data);
        uint crc = 0xffffffff;
        foreach (var value in name.Concat(data))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, crc ^ 0xffffffff); output.Write(number);
    }
}
