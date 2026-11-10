using System.Diagnostics;
using System.Security.Cryptography;
using Port.Host;
using Port.Prepare;

internal static class LinuxHost
{
    public static async Task<int> StartAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("The Linux host requires Linux.");
            return 2;
        }
        try
        {
            var program = AppContext.BaseDirectory;
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(dataHome) || !Path.IsPathFullyQualified(dataHome))
                dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            var data = Path.Combine(dataHome, "z0mb1es-rec0mp1led");
            var assetRoot = Path.Combine(data, "assets");
            var saveRoot = Path.Combine(data, "saves");
            var pictureRoot = Path.Combine(data, "pictures");
            Directory.CreateDirectory(data);
            var tools = new GamePreparationTools(
                Path.Combine(program, "tools", "inspect_assembly"),
                Path.Combine(program, "tools", "abi", "abi_check"),
                program,
                Path.Combine(program, "config", "supported-versions.json"),
                Path.Combine(program, "FNA.dll"), Path.Combine(program, "Port.Bridge.dll"),
                Path.Combine(program, "Port.Hooks.dll"),
                Path.Combine(program, "tools", "ffmpeg"), LinuxConverterHash(program));
            IOptionalArtImporter? art = null;
            var artTool = Path.Combine(program, "tools", "Port.AssetImporter");
            if (File.Exists(artTool))
                art = new ExternalOptionalArtImporter(artTool,
                    Path.Combine(program, "tools", "decode_360_xnb"),
                    Path.Combine(program, "FNA.dll"), Path.Combine(data, "art-work"));
            var store = new PersistentAssetStore(assetRoot, saveRoot,
                (xap, work, progress, token) => GamePreparationPipeline.Prepare(
                    xap, work, tools, progress, cancellationToken: token), art);
            using var view = new LinuxImportView(art is not null);
            var launchFailure = false;
            await AssetSessionCoordinator.RunAsync(store, view, view,
                async (active, token) =>
                {
                    view.FinishProgress();
                    var start = new ProcessStartInfo(Path.Combine(program, "Z0MB1ES"))
                    {
                        UseShellExecute = false,
                        WorkingDirectory = program
                    };
                    start.Environment["LD_LIBRARY_PATH"] = program + ":" +
                        (Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? "");
                    foreach (var argument in new[] { "--run-managed", active.DllPath,
                                 "--content", active.ContentRoot, "--save-dir", saveRoot,
                                 "--pictures-dir", pictureRoot, "--art-store", active.ArtRoot })
                        start.ArgumentList.Add(argument);
                    using var process = Process.Start(start)
                        ?? throw new IOException("Could not start the game.");
                    await process.WaitForExitAsync(token);
                    if (process.ExitCode is 0 or 10) return process.ExitCode == 10;
                    launchFailure = true;
                    view.Error("The game could not start. See the app's log for details.");
                    return false;
                });
            view.FinishProgress();
            return launchFailure ? 1 : 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            LinuxImportView.ShowFatal(error.Message);
            return 1;
        }
    }

    private static string LinuxConverterHash(string program)
    {
        var config = Path.Combine(program, "config", "linux-converter.sha256");
        var hash = File.ReadAllText(config).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid packaged converter hash.");
        var path = Path.Combine(program, "tools", "ffmpeg");
        using var stream = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Packaged Linux converter differs from its manifest.");
        return hash.ToLowerInvariant();
    }
}

internal sealed class LinuxImportView(bool optionalArt) : IGameFilePicker, IAssetImportView, IDisposable
{
    private Process? progress;

    public async Task<ImportChoices?> ChooseAsync(bool hasValidInstall, CancellationToken token)
    {
        FinishProgress();
        if (!hasValidInstall)
        {
            if (!await Question("Import the supported phone XAP to start playing?", "Choose XAP", token))
                return null;
            if (!optionalArt) return new ImportChoices(true, false, null, false);
            var addNormal = await Question("Add optional I MAED artwork now?", "Add artwork", token);
            var firstKind = addNormal ? await ChooseNormalKind(token) : null;
            var addViking = await Question("Add optional Time Viking artwork now?", "Add artwork", token);
            return new ImportChoices(true, firstKind is not null, firstKind, addViking);
        }
        var choices = await Dialog(token, "--list", "--checklist", "--title=Reimport assets",
            "--text=Select sources to replace or add. Your saves remain intact.",
            "--column=Import", "--column=Source", "--separator=|",
            "FALSE", "Phone XAP", "FALSE", "I MAED artwork", "FALSE", "Time Viking artwork");
        if (choices is null || choices.Length == 0) return null;
        var selected = choices.Split('|');
        var phone = selected.Contains("Phone XAP");
        var normal = selected.Contains("I MAED artwork") && optionalArt;
        var viking = selected.Contains("Time Viking artwork") && optionalArt;
        if (!phone && !normal && !viking) return null;
        var kind = normal ? await ChooseNormalKind(token) : null;
        if (normal && kind is null) return null;
        return new ImportChoices(phone, normal, kind, viking);
    }

    private static async Task<string?> ChooseNormalKind(CancellationToken token)
    {
        var choice = await Dialog(token, "--list", "--radiolist", "--title=I MAED source",
            "--text=Select the package you own.", "--column=Choose", "--column=Source",
            "TRUE", "Steam directory", "FALSE", "Xbox 360 LIVE package");
        return choice switch
        {
            "Steam directory" => "i-maed-steam",
            "Xbox 360 LIVE package" => "i-maed-360",
            _ => null
        };
    }

    public async Task<string?> PickGamePackageAsync(CancellationToken token) =>
        await Dialog(token, "--file-selection", "--title=Select supported phone XAP",
            "--file-filter=Phone package | *.xap");

    public async Task<string?> PickOptionalArtAsync(string sourceKind, CancellationToken token) =>
        await Dialog(token, "--file-selection", "--title=Select " + sourceKind + " source",
            sourceKind == "i-maed-steam" ? "--directory" : "--file-filter=Xbox LIVE package | *");

    public Task<bool> ConfirmAsync(AssetImportPreview preview, CancellationToken token) =>
        Question($"Source: {preview.PhoneVersion}\n" +
            $"Selected packages: {preview.SourceBytes / (1024 * 1024)} MiB\n" +
            $"Estimated peak storage: {preview.EstimatedPeakBytes / (1024 * 1024)} MiB\n" +
            $"Available: {preview.AvailableBytes / (1024 * 1024)} MiB\n\n" +
            "Import these assets?", "Confirm import", token);

    public void Progress(string stage)
    {
        if (progress is null)
        {
            var info = NewDialog("--progress", "--pulsate", "--no-cancel", "--auto-close",
                "--title=Importing game", "--text=Preparing private assets");
            info.RedirectStandardInput = true;
            progress = Process.Start(info) ?? throw new IOException("Could not show import progress.");
        }
        try
        {
            progress.StandardInput.WriteLine("# " + stage.Replace('-', ' '));
            progress.StandardInput.Flush();
        }
        catch (IOException) { } // A dismissed progress window does not cancel the transaction.
    }

    public void Error(string message)
    {
        FinishProgress();
        _ = Dialog(CancellationToken.None, "--error", "--title=Import failed", "--text=" + message)
            .GetAwaiter().GetResult();
    }

    public void FinishProgress()
    {
        if (progress is null) return;
        try { progress.StandardInput.WriteLine("100"); progress.StandardInput.Close(); }
        catch (IOException) { }
        progress.Dispose();
        progress = null;
    }

    public void Dispose() => FinishProgress();
    public static void ShowFatal(string message) =>
        _ = Dialog(CancellationToken.None, "--error", "--title=Z0MB1ES", "--text=" + message)
            .GetAwaiter().GetResult();

    private static async Task<bool> Question(string message, string title, CancellationToken token) =>
        await Dialog(token, "--question", "--title=" + title, "--text=" + message) is not null;

    private static ProcessStartInfo NewDialog(params string[] arguments)
    {
        var info = new ProcessStartInfo("zenity")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        return info;
    }

    private static async Task<string?> Dialog(CancellationToken token, params string[] arguments)
    {
        using var process = Process.Start(NewDialog(arguments))
            ?? throw new IOException("Could not open a Linux file dialog.");
        var output = process.StandardOutput.ReadToEndAsync(token);
        var error = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        var value = (await output).TrimEnd('\r', '\n');
        if (process.ExitCode == 0) return value;
        if (process.ExitCode == 1) return null;
        throw new IOException("Linux dialog failed: " + (await error).Trim());
    }
}
