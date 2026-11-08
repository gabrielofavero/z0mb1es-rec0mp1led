using System.Text;

/// <summary>Copies every interactive run's console and error output to a readable file.</summary>
internal sealed class RunLog : IDisposable
{
    private const int MaxFiles = 8;
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);
    private readonly TextWriter originalOut;
    private readonly TextWriter originalError;
    private readonly TextWriter file;

    private RunLog(TextWriter file)
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
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Z0MB1ES on teh ph0ne", "logs"),
            Path.Combine(Path.GetTempPath(), "Z0MB1ES logs")
        };
        Exception? lastError = null;
        foreach (var directory in directories)
        {
            try
            {
                Directory.CreateDirectory(directory);
                Prune(directory, MaxFiles - 1);
                var name = $"run-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}-{Environment.ProcessId}-{Guid.NewGuid():N}.txt";
                var path = Path.Combine(directory, name);
                var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew,
                    FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
                var file = new BoundedWriter(writer, MaxFileBytes);
                var log = new RunLog(file);
                Console.WriteLine($"RUN_LOG path={path}");
                return log;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                lastError = error;
            }
        }
        throw new IOException("Cannot create a run log in app data or the temporary directory.", lastError);
    }

    private static void Prune(string directory, int keep)
    {
        try
        {
            var files = new DirectoryInfo(directory).EnumerateFiles("run-*.txt")
                .OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
            var retained = 0;
            long retainedBytes = 0;
            foreach (var candidate in files)
            {
                if (DateTime.UtcNow - candidate.LastWriteTimeUtc <= MaxAge &&
                    candidate.Length <= MaxFileBytes && retained < keep &&
                    retainedBytes + candidate.Length <= keep * MaxFileBytes)
                {
                    retained++;
                    retainedBytes += candidate.Length;
                    continue;
                }
                try { candidate.Delete(); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        Console.SetOut(originalOut);
        Console.SetError(originalError);
        file.Dispose();
    }

    private sealed class BoundedWriter(StreamWriter writer, long limit) : TextWriter
    {
        private const string Marker = "\n[Log truncated at 2 MiB]\n";
        private long remaining = limit;
        private bool truncated;
        public override Encoding Encoding => writer.Encoding;

        public override void Write(char value) => Write(value.ToString().AsSpan());
        public override void Write(string? value) { if (value is not null) Write(value.AsSpan()); }
        public override void Write(ReadOnlySpan<char> buffer)
        {
            if (truncated || buffer.IsEmpty) return;
            var bytes = Encoding.GetByteCount(buffer);
            if (bytes <= remaining)
            {
                writer.Write(buffer);
                remaining -= bytes;
                return;
            }
            var available = Math.Max(0, remaining - Encoding.GetByteCount(Marker));
            var low = 0;
            var high = buffer.Length;
            while (low < high)
            {
                var middle = low + (high - low + 1) / 2;
                if (Encoding.GetByteCount(buffer[..middle]) <= available) low = middle;
                else high = middle - 1;
            }
            if (low > 0) writer.Write(buffer[..low]);
            writer.Write(Marker);
            truncated = true;
        }
        public override void Flush() => writer.Flush();
        protected override void Dispose(bool disposing) { if (disposing) writer.Dispose(); base.Dispose(disposing); }
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
