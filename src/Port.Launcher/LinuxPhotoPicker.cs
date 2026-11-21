using System.Diagnostics;
using Port.Host;

internal sealed class LinuxPhotoPicker : IGamePhotoPicker
{
    public async Task<IReadOnlyList<string>> PickPhotosAsync(int maximumCount,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("zenity")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "--file-selection", "--multiple", "--separator=|",
                     "--title=Choose creator photos", "--file-filter=Images | *.png *.jpg *.jpeg" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
            ?? throw new IOException("Could not open the Linux photo picker.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode == 1) return [];
        if (process.ExitCode != 0)
            throw new IOException("Linux photo picker failed: " + (await error).Trim());
        return (await output).TrimEnd('\r', '\n').Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Take(maximumCount).ToArray();
    }
}
