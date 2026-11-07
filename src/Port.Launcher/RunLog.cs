using System.Text;

/// <summary>Copies every interactive run's console and error output to a readable file.</summary>
internal sealed class RunLog : IDisposable
{
    private readonly TextWriter originalOut;
    private readonly TextWriter originalError;
    private readonly StreamWriter file;

    private RunLog(StreamWriter file)
    {
        this.file = file;
        originalOut = Console.Out;
        originalError = Console.Error;
        var gate = new object();
        Console.SetOut(new TeeWriter(originalOut, file, gate));
        Console.SetError(new TeeWriter(originalError, file, gate));
    }

    public static RunLog Start()
    {
        var directories = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "logs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Z0MB1ES on teh ph0ne", "logs"),
            Path.Combine(Path.GetTempPath(), "Z0MB1ES logs")
        };
        Exception? lastError = null;
        foreach (var directory in directories)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var name = $"run-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}-{Guid.NewGuid():N}.txt";
                var path = Path.Combine(directory, name);
                var file = new StreamWriter(new FileStream(path, FileMode.CreateNew,
                    FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
                var log = new RunLog(file);
                Console.WriteLine($"RUN_LOG path={path}");
                return log;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                lastError = error;
            }
        }
        throw new IOException("Cannot create a run log in the launcher, app data, or temporary directory.", lastError);
    }

    public void Dispose()
    {
        Console.SetOut(originalOut);
        Console.SetError(originalError);
        file.Dispose();
    }

    private sealed class TeeWriter(TextWriter console, TextWriter file, object gate) : TextWriter
    {
        public override Encoding Encoding => console.Encoding;

        public override void Write(char value)
        {
            lock (gate) { console.Write(value); file.Write(value); }
        }

        public override void Write(string? value)
        {
            lock (gate) { console.Write(value); file.Write(value); }
        }

        public override void Write(ReadOnlySpan<char> buffer)
        {
            lock (gate) { console.Write(buffer); file.Write(buffer); }
        }

        public override void WriteLine(string? value)
        {
            lock (gate) { console.WriteLine(value); file.WriteLine(value); }
        }

        public override void Flush()
        {
            lock (gate) { console.Flush(); file.Flush(); }
        }
    }
}
