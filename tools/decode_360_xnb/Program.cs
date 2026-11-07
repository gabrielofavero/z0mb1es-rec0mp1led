using System.Buffers.Binary;
using System.Reflection;

if (args.Length != 3)
    throw new ArgumentException("Usage: decode_360_xnb <FNA.dll> <input-content-dir> <new-private-output-dir>");

var fna = Assembly.LoadFrom(Path.GetFullPath(args[0]));
var decoderType = fna.GetType("Microsoft.Xna.Framework.Content.LzxDecoder", throwOnError: true)!;
var constructor = decoderType.GetConstructor([typeof(int)])!;
var decompress = decoderType.GetMethod("Decompress", BindingFlags.Public | BindingFlags.Instance)!;
var inputRoot = Path.GetFullPath(args[1]);
var outputRoot = Path.GetFullPath(args[2]);
var privateRoot = Path.GetFullPath(Environment.GetEnvironmentVariable("PORT_ASSET_PRIVATE") ??
    Path.Combine(Environment.CurrentDirectory, "private"));
if (!outputRoot.StartsWith(privateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Decoded game bytes must stay in the ignored private/ directory.");
if (Directory.Exists(outputRoot))
    throw new IOException($"Output directory exists: {outputRoot}");
Directory.CreateDirectory(outputRoot);

foreach (var input in Directory.EnumerateFiles(inputRoot, "*.xnb", SearchOption.AllDirectories))
{
    var relative = Path.GetRelativePath(inputRoot, input);
    var output = Path.Combine(outputRoot, relative);
    var bytes = File.ReadAllBytes(input);
    if (bytes.Length < 10 || bytes[0] != 'X' || bytes[1] != 'N' || bytes[2] != 'B' ||
        BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(6, 4)) != bytes.Length)
        throw new InvalidDataException($"Invalid XNB: {relative}");

    byte[] payload;
    if ((bytes[5] & 0x80) == 0)
        payload = bytes[10..];
    else
    {
        var uncompressedSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10, 4));
        using var compressed = new MemoryStream(bytes, 14, bytes.Length - 14, writable: false);
        using var expanded = new MemoryStream(new byte[uncompressedSize], writable: true);
        var decoder = constructor.Invoke([16]);
        while (compressed.Position < compressed.Length)
        {
            var hi = compressed.ReadByte();
            var lo = compressed.ReadByte();
            if (hi < 0 || lo < 0) throw new EndOfStreamException(relative);
            var blockSize = (hi << 8) | lo;
            var frameSize = 0x8000;
            if (hi == 0xFF)
            {
                frameSize = (lo << 8) | compressed.ReadByte();
                blockSize = (compressed.ReadByte() << 8) | compressed.ReadByte();
            }
            if (blockSize == 0 || frameSize == 0) break;
            var blockEnd = compressed.Position + blockSize;
            var result = (int)decompress.Invoke(decoder, [compressed, blockSize, expanded, frameSize])!;
            if (result != 0) throw new InvalidDataException($"LZX error {result}: {relative}");
            compressed.Position = blockEnd;
        }
        if (expanded.Position != uncompressedSize)
            throw new InvalidDataException($"Decompressed size mismatch: {relative}");
        payload = expanded.ToArray();
    }

    var resultBytes = new byte[10 + payload.Length];
    Array.Copy(bytes, resultBytes, 6);
    resultBytes[5] &= 0x7F;
    BinaryPrimitives.WriteInt32LittleEndian(resultBytes.AsSpan(6, 4), resultBytes.Length);
    payload.CopyTo(resultBytes, 10);
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllBytes(output, resultBytes);
    Console.WriteLine($"{relative}: {bytes.Length} -> {resultBytes.Length}");
}
