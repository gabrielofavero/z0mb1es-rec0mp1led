using System.Diagnostics;

internal static class MacDialogs
{
    public static async Task<string?> ShowAsync(string program, CancellationToken token,
        string action, params string[] arguments)
    {
        var script = Path.Combine(program, "dialogs.applescript");
        if (!File.Exists(script)) throw new FileNotFoundException("macOS dialog script is missing.", script);
        var start = new ProcessStartInfo("/usr/bin/osascript")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(script);
        start.ArgumentList.Add(action);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start macOS dialog.");
        using var registration = token.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        if (process.ExitCode == 0)
        {
            var result = (await output).TrimEnd('\r', '\n');
            return result == "__CANCELLED__" ? null : result;
        }
        throw new IOException("macOS dialog failed: " + (await error).Trim());
    }
}
