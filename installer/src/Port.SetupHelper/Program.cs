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
    if (args.Length == 7 && args[0] == "install" && args[1] == "--destination" &&
        args[3] == "--payload" && args[5] == "--xap")
    {
        InstallerEngine.Install(args[2], args[4], args[6]);
        Console.WriteLine("INSTALL_COMMITTED");
        return 0;
    }
    Console.Error.WriteLine("Usage: Port.SetupHelper validate-xap --xap <file.xap> | verify-installed --directory <install> | install --destination <dir> --payload <dir> --xap <file.xap>");
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
    else Console.Error.WriteLine($"SETUP_FAILED {error.GetType().Name}: {error.Message}");
    return 1;
}
