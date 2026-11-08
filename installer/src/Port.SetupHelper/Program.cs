using System.IO.Compression;
using Port.SetupHelper;

try
{
    if (args.Length == 3 && args[0] == "validate-xap" && args[1] == "--xap")
    {
        var identity = Port.Prepare.PreparationEngine.ValidateXap(args[2]);
        Console.WriteLine($"XAP_VALID version={identity.SupportedVersion} sha256={identity.XapSha256}");
        return 0;
    }
    if (args.Length == 3 && args[0] == "verify-installed" && args[1] == "--directory")
    {
        InstallerEngine.Verify(args[2]);
        Console.WriteLine("INSTALL_VERIFIED");
        return 0;
    }
    if (args.Length == 3 && args[0] == "rollback" && args[1] == "--destination")
    {
        InstallerEngine.Rollback(args[2]);
        Console.WriteLine("INSTALL_ROLLED_BACK");
        return 0;
    }
    if (args.Length == 3 && args[0] == "finalize" && args[1] == "--destination")
    {
        InstallerEngine.FinalizeInstall(args[2]);
        Console.WriteLine("INSTALL_FINALIZED");
        return 0;
    }
    if (args.Length == 5 && args[0] == "validate-art" && args[1] == "--kind" && args[3] == "--source")
    {
        Console.WriteLine("ART_VALID " + OptionalArt.ValidateSource(args[2], args[4]));
        return 0;
    }
    if (args.Length >= 7 && args[0] == "install" && args.Length % 2 == 1)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i += 2)
            if (!values.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Duplicate setup option.");
        if (!new[] { "--destination", "--payload", "--xap" }.All(values.ContainsKey) ||
            values.Keys.Except(new[] { "--destination", "--payload", "--xap", "--normal", "--normal-source",
                                       "--viking", "--viking-source", "--cancel-file" }).Any())
            throw new ArgumentException("Invalid setup options.");
        values.TryGetValue("--normal-source", out var normalSource);
        values.TryGetValue("--viking-source", out var vikingSource);
        if (normalSource == "") normalSource = null;
        if (vikingSource == "") vikingSource = null;
        var art = new ArtOptions(values.GetValueOrDefault("--normal", "keep"), normalSource,
            values.GetValueOrDefault("--viking", "keep"), vikingSource);
        InstallerEngine.Install(values["--destination"], values["--payload"], values["--xap"], art,
            values.GetValueOrDefault("--cancel-file"));
        Console.WriteLine("INSTALL_COMMITTED");
        return 0;
    }
    Console.Error.WriteLine("Usage: Port.SetupHelper validate-xap --xap <file.xap> | validate-art --kind i-maed-steam|i-maed-360|time-viking-360 --source <path> | verify-installed --directory <install> | rollback|finalize --destination <install> | install --destination <dir> --payload <dir> --xap <file.xap> [--normal keep|none|steam|360 --normal-source <path>] [--viking keep|none|360 --viking-source <path>]");
    return 2;
}
catch (Exception error)
{
    if (args.Length == 3 && args[0] == "validate-xap" && File.Exists(args[2]))
    {
        try
        {
            using var archive = ZipFile.OpenRead(args[2]);
            var entries = archive.Entries.Count;
            var bytes = archive.Entries.Sum(entry => entry.Length);
            Console.Error.WriteLine(entries == 127 && bytes == 76993533
                ? "XAP_VALIDATION_FAILED: This package resembles the supported version but its bytes differ; it may be damaged or modified."
                : "XAP_VALIDATION_FAILED: This is a readable XAP, but its version is unsupported.");
        }
        catch (InvalidDataException)
        {
            Console.Error.WriteLine("XAP_VALIDATION_FAILED: The XAP archive is corrupted or incomplete.");
        }
        catch (IOException)
        {
            Console.Error.WriteLine("XAP_VALIDATION_FAILED: The XAP could not be read.");
        }
    }
    else Console.Error.WriteLine($"{(args.Length > 0 && args[0] == "validate-art" ? "ART_VALIDATION_FAILED" : "SETUP_FAILED")} {error.GetType().Name}: {error.Message}");
    return 1;
}
