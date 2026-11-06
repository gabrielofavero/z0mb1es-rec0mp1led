using Port.Hooks;

var directory = Directory.CreateTempSubdirectory("zombies-storage-check-");
try
{
    DesktopStorage.Configure(directory.FullName);
    var profile = new Profile();
    var player = new Player();
    var custom = new Custom();
    DesktopStorage.Read(profile, player, custom);
    if (File.Exists(Path.Combine(directory.FullName, "savegame"))) throw new Exception("Fresh read created a save.");

    profile.kills = 123;
    profile.achievables[2] = true;
    player.settings[4] = true;
    custom.customImg[1] = true;
    DesktopStorage.Write(profile, player, custom);
    var first = File.ReadAllBytes(Path.Combine(directory.FullName, "savegame"));
    if (first.Length != 41 || first[0] != 101) throw new Exception("Original save layout changed.");

    profile.kills = 456;
    DesktopStorage.Write(profile, player, custom);
    profile.kills = 0;
    profile.achievables[2] = false;
    player.settings[4] = false;
    custom.customImg[1] = false;
    DesktopStorage.Read(profile, player, custom);
    if (profile.kills != 456 || !profile.achievables[2] || !player.settings[4] || !custom.customImg[1])
        throw new Exception("Save round trip failed.");

    var path = Path.Combine(directory.FullName, "savegame");
    File.WriteAllBytes(path, [101, 4]);
    profile.kills = 0;
    DesktopStorage.Read(profile, player, custom);
    if (profile.kills != 123 || !File.ReadAllBytes(path).SequenceEqual(first))
        throw new Exception("Truncated save did not recover the previous generation.");

    File.WriteAllBytes(path, [100, .. first[1..]]);
    profile.kills = 0;
    DesktopStorage.Read(profile, player, custom);
    if (profile.kills != 123) throw new Exception("Old save version did not use the backup.");
    Console.WriteLine("STORAGE_CHECKS_OK fresh, byte layout, round trip, corrupt and old version recovery");
}
finally
{
    DesktopStorage.Configure(null);
    directory.Delete(recursive: true);
}

public sealed class Profile
{
    public int kills;
    public bool[] achievables = new bool[20];
    public void Write(Stream stream)
    {
        stream.WriteByte(4);
        stream.Write(BitConverter.GetBytes(kills));
        foreach (var flag in achievables) stream.WriteByte(flag ? (byte)1 : (byte)0);
    }
    public void Read(Stream stream)
    {
        if (stream.ReadByte() != 4) throw new InvalidDataException();
        var bytes = new byte[4];
        stream.ReadExactly(bytes);
        kills = BitConverter.ToInt32(bytes);
        for (var i = 0; i < achievables.Length; i++) achievables[i] = stream.ReadByte() == 1;
    }
}

public sealed class Player
{
    public bool[] settings = new bool[6];
    public void Write(Stream stream) { foreach (var flag in settings) stream.WriteByte(flag ? (byte)1 : (byte)0); }
    public void Read(Stream stream) { for (var i = 0; i < settings.Length; i++) settings[i] = stream.ReadByte() == 1; }
}

public sealed class Custom
{
    public bool[] customImg = new bool[5];
    public void Write(Stream stream)
    {
        foreach (var flag in customImg)
        {
            stream.WriteByte(flag ? (byte)1 : (byte)0);
            if (flag) stream.Write([20, 30, 100, 0]);
        }
    }
    public void Read(Stream stream)
    {
        for (var i = 0; i < customImg.Length; i++)
        {
            customImg[i] = stream.ReadByte() == 1;
            if (customImg[i]) { var payload = new byte[4]; stream.ReadExactly(payload); }
        }
    }
}
