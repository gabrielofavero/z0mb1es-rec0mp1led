using System.Reflection;

namespace Port.Hooks;

/// <summary>Desktop file boundary for the original version-101 serializers.</summary>
public static class DesktopStorage
{
    private const byte Version = 101;
    private static string? rootOverride;
    private static byte[]? savedCustomBytes;
    private static bool customReapplied;
    public static string SaveDirectory => rootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Z0MB1ES on teh ph0ne");

    public static void Configure(string? directory)
    {
        rootOverride = directory is null ? null : Path.GetFullPath(directory);
        Port.Bridge.LocalScores.Configure(SaveDirectory);
        savedCustomBytes = null;
        customReapplied = false;
    }

    public static void ReapplyCustom(object custom)
    {
        if (customReapplied || savedCustomBytes is null) return;
        customReapplied = true;
        try
        {
            using var stream = new MemoryStream(savedCustomBytes, writable: false);
            Serialize(custom, "Read", stream);
            if (stream.Position != stream.Length) throw new InvalidDataException("Unconsumed custom save bytes.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            TargetInvocationException or ArgumentException)
        {
            Console.Error.WriteLine($"CUSTOM_SAVE_REAPPLY_SKIPPED reason={error.GetBaseException().GetType().Name}");
        }
    }

    public static void Write(object profile, object player, object custom)
    {
        using var memory = new MemoryStream();
        memory.WriteByte(Version);
        Serialize(profile, "Write", memory);
        Serialize(player, "Write", memory);
        Serialize(custom, "Write", memory);
        WriteBytes(memory.ToArray());
    }

    public static void Read(object profile, object player, object custom)
    {
        var path = Path.Combine(SaveDirectory, "savegame");
        var backup = path + ".bak";
        if (TryLoad(path, profile, player, custom)) return;
        if (TryLoad(backup, profile, player, custom))
        {
            var temp = path + ".recover-" + Guid.NewGuid().ToString("N");
            try
            {
                File.Copy(backup, temp);
                File.Move(temp, path, overwrite: true);
                Console.WriteLine("SAVE_RECOVERED backup");
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }

    private static bool TryLoad(string path, object profile, object player, object custom)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (!Valid(bytes, profile, custom)) throw new InvalidDataException("Invalid version-101 save structure.");
            using var stream = new MemoryStream(bytes, writable: false);
            stream.Position = 1;
            Serialize(profile, "Read", stream);
            Serialize(player, "Read", stream);
            var customStart = (int)stream.Position;
            Serialize(custom, "Read", stream);
            if (stream.Position != stream.Length) throw new InvalidDataException("Unconsumed save bytes.");
            savedCustomBytes = bytes[customStart..];
            Console.WriteLine($"SAVE_LOADED {Path.GetFileName(path)} bytes={bytes.Length}");
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidDataException or TargetInvocationException or ArgumentException)
        {
            Console.WriteLine($"SAVE_SKIPPED {Path.GetFileName(path)} reason={error.GetBaseException().GetType().Name}");
            return false;
        }
    }

    private static bool Valid(byte[] data, object profile, object custom)
    {
        var achievements = (bool[]?)profile.GetType().GetField("achievables")?.GetValue(profile);
        var customImages = (Array?)custom.GetType().GetField("customImg")?.GetValue(custom);
        if (achievements is null || customImages is null || data.Length < 6 + achievements.Length + 6 + customImages.Length ||
            data[0] != Version || data[1] != 4) return false;
        var position = 6;
        for (var i = 0; i < achievements.Length + 6; i++)
            if (data[position++] > 1) return false;
        foreach (var _ in customImages)
        {
            if (position >= data.Length || data[position] > 1) return false;
            if (data[position++] == 0) continue;
            if (position + 4 > data.Length) return false;
            var nameBytes = data[position + 3];
            position += 4;
            if (position + nameBytes > data.Length) return false;
            position += nameBytes;
        }
        return position == data.Length;
    }

    private static void Serialize(object target, string method, Stream stream)
    {
        var serializer = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, types: [typeof(Stream)], modifiers: null)
            ?? throw new MissingMethodException(target.GetType().FullName, method + "(Stream)");
        serializer.Invoke(target, [stream]);
    }

    public static void WriteBytes(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0 || data[0] != Version) throw new InvalidDataException("Expected version-101 save bytes.");
        var directory = SaveDirectory;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "savegame");
        var backup = path + ".bak";
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(data);
                file.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temp, path, backup);
            else File.Move(temp, path);
            Console.WriteLine($"SAVE_WRITTEN bytes={data.Length}");
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
