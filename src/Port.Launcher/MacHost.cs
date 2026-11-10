using System.Diagnostics;
using System.Security.Cryptography;
using Port.Host;
using Port.Prepare;

internal static class MacHost
{
    public static async Task<int> StartAsync()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("The macOS host requires macOS.");
            return 2;
        }
        var program = AppContext.BaseDirectory;
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home)) throw new IOException("macOS home folder is unavailable.");
            var data = Path.Combine(home, "Library", "Application Support", "z0mb1es-rec0mp1led");
            var assetRoot = Path.Combine(data, "assets");
            var saveRoot = Path.Combine(data, "saves");
            var pictureRoot = Path.Combine(data, "pictures");
            Directory.CreateDirectory(data);
            var tools = new GamePreparationTools(
                Path.Combine(program, "tools", "inspect_assembly"),
                Path.Combine(program, "tools", "abi", "abi_check"), program,
                Path.Combine(program, "config", "supported-versions.json"),
                Path.Combine(program, "FNA.dll"), Path.Combine(program, "Port.Bridge.dll"),
                Path.Combine(program, "Port.Hooks.dll"),
                Path.Combine(program, "tools", "ffmpeg"), ConverterHash(program));
            IOptionalArtImporter? art = null;
            var artTool = Path.Combine(program, "tools", "Port.AssetImporter");
            if (File.Exists(artTool))
                art = new ExternalOptionalArtImporter(artTool,
                    Path.Combine(program, "tools", "decode_360_xnb"),
                    Path.Combine(program, "FNA.dll"), Path.Combine(data, "art-work"));
            var store = new PersistentAssetStore(assetRoot, saveRoot,
                (xap, work, progress, token) => GamePreparationPipeline.Prepare(
                    xap, work, tools, progress, cancellationToken: token), art);
            var view = new MacImportView(program, art is not null);
            var launchFailure = false;
            await AssetSessionCoordinator.RunAsync(store, view, view, async (active, token) =>
            {
                var start = new ProcessStartInfo(Path.Combine(program, "Z0MB1ES"))
                {
                    UseShellExecute = false,
                    WorkingDirectory = program
                };
                start.Environment["DYLD_LIBRARY_PATH"] = program + ":" +
                    (Environment.GetEnvironmentVariable("DYLD_LIBRARY_PATH") ?? "");
                foreach (var argument in new[] { "--run-managed", active.DllPath,
                             "--content", active.ContentRoot, "--save-dir", saveRoot,
                             "--pictures-dir", pictureRoot, "--art-store", active.ArtRoot })
                    start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new IOException("Could not start the game.");
                await process.WaitForExitAsync(token);
                if (process.ExitCode is 0 or 10) return process.ExitCode == 10;
                launchFailure = true;
                view.Error("The game could not start. See the app's log for details.");
                return false;
            });
            return launchFailure ? 1 : 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            try { _ = await MacDialogs.ShowAsync(program, CancellationToken.None, "error", error.Message); }
            catch (Exception dialogError) { Console.Error.WriteLine(dialogError); }
            return 1;
        }
    }

    private static string ConverterHash(string program)
    {
        var hash = File.ReadAllText(Path.Combine(program, "config", "macos-converter.sha256"))
            .Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid packaged converter hash.");
        using var input = File.OpenRead(Path.Combine(program, "tools", "ffmpeg"));
        if (!Convert.ToHexString(SHA256.HashData(input)).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Packaged macOS converter differs from its manifest.");
        return hash.ToLowerInvariant();
    }
}

internal sealed class MacImportView(string program, bool optionalArt) : IGameFilePicker, IAssetImportView
{
    private Task<string?> Show(CancellationToken token, string action, params string[] args) =>
        MacDialogs.ShowAsync(program, token, action, args);

    private async Task<bool> Ask(CancellationToken token, string message) =>
        await Show(token, "question", message) is not null;

    public async Task<ImportChoices?> ChooseAsync(bool hasValidInstall, CancellationToken token)
    {
        if (!hasValidInstall)
        {
            if (!await Ask(token, "Import Z0MB1ES (on teh ph0ne) from your Windows Phone XAP to start playing?")) return null;
            if (!optionalArt) return new ImportChoices(true, false, null, false);
            var normal = await Show(token, "optional-art",
                "Add artwork from I MAED A GAM3 W1TH Z0MB1ES 1N IT!!!? You can skip this and add it later in Settings.") is not null;
            var kind = normal ? await ChooseNormalKind(token) : null;
            var viking = await Show(token, "optional-art",
                "Add artwork from Time Viking? You can skip this and add it later in Settings.") is not null;
            return new ImportChoices(true, kind is not null, kind, viking);
        }
        var selection = await Show(token, "sources");
        if (string.IsNullOrEmpty(selection)) return null;
        var choices = selection.Split('\n');
        var phone = choices.Contains("Z0MB1ES phone XAP");
        var normalSource = optionalArt && choices.Contains("I MAED artwork");
        var vikingSource = optionalArt && choices.Contains("Time Viking artwork");
        if (!phone && !normalSource && !vikingSource) return null;
        var normalKind = normalSource ? await ChooseNormalKind(token) : null;
        if (normalKind is null) normalSource = false;
        if (!phone && !normalSource && !vikingSource) return null;
        return new ImportChoices(phone, normalSource, normalKind, vikingSource);
    }

    private async Task<string?> ChooseNormalKind(CancellationToken token) =>
        await Show(token, "normal-kind") switch
        {
            "Steam directory" => "i-maed-steam",
            "Xbox 360 LIVE package" => "i-maed-360",
            _ => null
        };

    public Task<string?> PickGamePackageAsync(CancellationToken token) => Show(token, "xap");
    public Task<string?> PickOptionalArtAsync(string sourceKind, CancellationToken token) =>
        Show(token, sourceKind);

    public Task<bool> ConfirmAsync(AssetImportPreview preview, CancellationToken token) =>
        Ask(token, $"Source: {preview.PhoneVersion}\n" +
            $"Selected packages: {preview.SourceBytes / (1024 * 1024)} MiB\n" +
            $"Estimated peak storage: {preview.EstimatedPeakBytes / (1024 * 1024)} MiB\n" +
            $"Available: {preview.AvailableBytes / (1024 * 1024)} MiB\n\nImport these assets?");

    public void Progress(string stage) => _ = Show(CancellationToken.None, "progress", stage)
        .GetAwaiter().GetResult();
    public void Error(string message) => _ = Show(CancellationToken.None, "error", message)
        .GetAwaiter().GetResult();
}
