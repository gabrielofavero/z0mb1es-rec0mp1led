using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Port.Content;
using Port.Prepare;

namespace Port.IosProbe;

/// <summary>Early feasibility experiments; none of these results assert gameplay support.</summary>
public static class ExecutionProbe
{
    public static string RunGeneratedIl(string root)
    {
        Directory.CreateDirectory(root);
        var name = "AuthoredProbe_" + Guid.NewGuid().ToString("N");
        var path = Path.Combine(root, name + ".dll");
        // This assembly doesn't exist at app build time and cannot have an AOT image.
        using (var assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition(name, new Version(1, 0)), name, ModuleKind.Dll))
        {
            var module = assembly.MainModule;
            var type = new TypeDefinition("Authored", "Payload",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract |
                Mono.Cecil.TypeAttributes.Sealed, module.TypeSystem.Object);
            module.Types.Add(type);
            var method = new MethodDefinition("Run",
                Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static,
                module.TypeSystem.Int32);
            method.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
            type.Methods.Add(method);
            var il = method.Body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, module.ImportReference(
                typeof(Math).GetMethod(nameof(Math.Abs), [typeof(int)])!));
            il.Emit(OpCodes.Ldc_I4, 1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ret);
            assembly.Write(path);
        }
        // Exercise the same file-based Cecil write/read/rewrite mechanism needed by import.
        var patched = Path.Combine(root, name + ".prepared.dll");
        using (var assembly = AssemblyDefinition.ReadAssembly(path))
        {
            var method = assembly.MainModule.Types.Single(t => t.Name == "Payload").Methods.Single();
            var anchor = method.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldc_I4);
            if ((int)anchor.Operand != 1) throw new InvalidDataException("Authored IL guard failed.");
            anchor.Operand = 37;
            assembly.Write(patched);
        }
        File.Delete(path);
        var hash = Hash(patched);
        Execute(patched);
        // Publish the descriptor only after a successful call. An earlier working probe survives failure.
        var descriptor = Path.Combine(root, "payload.json");
        var temporary = descriptor + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Payload(Path.GetFileName(patched), hash)));
        File.Move(temporary, descriptor, overwrite: true);
        return $"generated-il=passed sha256={hash}; game-execution=unproven";
    }

    public static string ResumeGeneratedIl(string root)
    {
        var descriptor = Path.Combine(root, "payload.json");
        if (!File.Exists(descriptor)) return "persistent-il=not-imported";
        var payload = JsonSerializer.Deserialize<Payload>(File.ReadAllText(descriptor))
            ?? throw new InvalidDataException("Missing payload descriptor.");
        if (payload.File != Path.GetFileName(payload.File) || !payload.File.StartsWith("AuthoredProbe_", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid authored payload path.");
        var path = Path.Combine(root, payload.File);
        if (Hash(path) != payload.Sha256) throw new InvalidDataException("Persistent IL hash mismatch.");
        Execute(path);
        return "persistent-il=passed; game-execution=unproven";
    }

    public static string ImportXap(string xap, string root, IContentAudioConverter converter,
        Action<string> log)
    {
        // Reuse exact input guards; unsupported input makes no output directory.
        var identity = PreparationEngine.ValidateXap(xap);
        var work = Path.Combine(root, "attempt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var preparation = Path.Combine(work, "extraction");
            PreparationEngine.Prepare(new PreparationRequest(xap, preparation));
            log($"xap-extraction=passed sha256={identity.XapSha256}");
            var original = Path.Combine(preparation, "original-xap");
            // This proves Cecil can read and write the real package's metadata on the host.
            // It deliberately does not produce or claim an executable game preparation.
            using (var game = AssemblyDefinition.ReadAssembly(Path.Combine(original, "ZombiesWP7.dll")))
            {
                game.Write(Path.Combine(work, "game-metadata-roundtrip.dll"));
                log($"game-metadata-roundtrip=passed types={game.MainModule.Types.Count}");
            }
            ContentPreparer.Prepare(original, Path.Combine(work, "content"), converter,
                identity.XapSha256, p => log($"content={p.Stage} {p.Completed}/{p.Total}"));
            var files = Directory.EnumerateFiles(work, "*", SearchOption.AllDirectories)
                .Select(path => new ImportedFile(Path.GetRelativePath(work, path), Hash(path))).ToArray();
            var receipt = Path.Combine(root, "import-receipt.json");
            File.WriteAllText(receipt + ".tmp", JsonSerializer.Serialize(new ImportReceipt(
                Path.GetFileName(work), identity.XapSha256, identity.DllSha256, files)));
            File.Move(receipt + ".tmp", receipt, overwrite: true);
            log("three-song-conversion=passed; prepared-game-execution=NOT-TESTED (no iOS FNA host)");
            return work;
        }
        catch
        {
            Directory.Delete(work, recursive: true);
            throw;
        }
    }

    public static string ResumeImport(string root)
    {
        var receipt = Path.Combine(root, "import-receipt.json");
        if (!File.Exists(receipt)) return "persistent-import=not-imported";
        var imported = JsonSerializer.Deserialize<ImportReceipt>(File.ReadAllText(receipt))
            ?? throw new InvalidDataException("Missing import receipt.");
        if (!PreparationEngine.IsSupportedIdentity(imported.XapSha256, imported.DllSha256) ||
            imported.Attempt != Path.GetFileName(imported.Attempt) ||
            !imported.Attempt.StartsWith("attempt-", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid import receipt.");
        var work = Path.GetFullPath(Path.Combine(root, imported.Attempt));
        foreach (var file in imported.Files)
        {
            var path = Path.GetFullPath(Path.Combine(work, file.Path));
            if (!path.StartsWith(work + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                Hash(path) != file.Sha256)
                throw new InvalidDataException("Persistent import hash mismatch.");
        }
        return $"persistent-import=passed files={imported.Files.Length}; game-execution=unproven";
    }

    private static void Execute(string path)
    {
        var assembly = Assembly.LoadFrom(path);
        var method = assembly.GetType("Authored.Payload", throwOnError: true)!
            .GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
        if ((int)method.Invoke(null, [-5])! != 42 ||
            method.CreateDelegate<Func<int, int>>()(-5) != 42)
            throw new InvalidDataException("Loaded authored IL returned the wrong value.");
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed record Payload(string File, string Sha256);
    private sealed record ImportedFile(string Path, string Sha256);
    private sealed record ImportReceipt(string Attempt, string XapSha256, string DllSha256, ImportedFile[] Files);
}
