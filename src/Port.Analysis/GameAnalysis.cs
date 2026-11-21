namespace Port.Analysis;

/// <summary>Metadata-only analysis. Explicit runtime inputs make this usable on iOS,
/// without subprocesses, a desktop TPA catalog, or loading the game as executable code.</summary>
public static class GameAnalysis
{
    public static int InspectCommand(string[] args) => EvidenceJson.Run(args);
    public static int AbiCommand(string[] args) => AbiCheck.Run(args);

    public static void WriteInventory(string xap, string versions, string output)
    {
        if (EvidenceJson.RunChecked(["--xap", xap, "--output", output, "--versions", versions]) != 0)
            throw new InvalidDataException("Game metadata inspection failed.");
    }

    // A return value of 1 can still write a complete report with guarded rewrites;
    // the preparer separately requires its exact reviewed SHA-256 before use.
    public static int WriteAbiReport(string inventory, string originalDll, string fna,
        string bridge, string output, string versions, IReadOnlyList<string> runtimeAssemblies,
        string targetRuntime)
    {
        if (runtimeAssemblies.Count == 0 || string.IsNullOrWhiteSpace(targetRuntime))
            throw new ArgumentException("An explicit target runtime catalog and identity are required.");
        return AbiCheck.Check(inventory, originalDll, fna, bridge, output, versions,
            runtimeAssemblies, targetRuntime);
    }
}
