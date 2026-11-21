using Foundation;
using Port.Prepare;

namespace Port.IosGame;

internal static class GameData
{
    internal static string Root => Path.Combine(NSFileManager.DefaultManager.GetUrls(
        NSSearchPathDirectory.ApplicationSupportDirectory, NSSearchPathDomain.User)[0].Path!, "Z0MB1ES-ios-game");
    internal static string Saves => Path.Combine(Root, "Saves");

}

internal static class GameTrace
{
    private static readonly object Gate = new();
    internal static string Path => System.IO.Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.MyDocuments), "ios-game-trace.txt");

    internal static string LastBoundary = "process-start";
    internal static void Write(string message)
    {
        if (!message.StartsWith("watchdog=", StringComparison.Ordinal)) Volatile.Write(ref LastBoundary, message);
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                if (File.Exists(Path) && new FileInfo(Path).Length > 4 * 1024 * 1024)
                    File.Move(Path, Path + ".previous.txt", overwrite: true);
                File.AppendAllText(Path, $"{DateTime.UtcNow:O} {message}\n");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    internal sealed class Writer : TextWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void WriteLine(string? value) { if (value is not null) GameTrace.Write(value); }
        public override void Write(string? value) { if (!string.IsNullOrEmpty(value)) GameTrace.Write(value); }
    }
}
