using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using ModuleDefinition = Mono.Cecil.ModuleDefinition;

namespace Port.Prepare;

/// <summary>Applies guarded desktop call adaptations to a verified prepared DLL.</summary>
public static class GameplayPatcher
{
    private const string OriginalHash = "7b819cb562826ae4d2592ff741877e76da0e57bfbe48192aabb79319fdb36823";
    private const string DebugHooksHash = "745208ed8aa8d5f1d3020b00826666da74d4c25c7c0ca442a0649fd6480704dc";
    private const string ReleaseHooksHash = "03dcef0d5c08f59911d5bd4def52663678863f3da85dd8188e226463ed2646df";
    private const string OriginalMethodBodyHash = "54407b733f8c2d7cdc67534f66899e99191731e2e0beeed8481bc673ff54f425";
    private const string TouchBodyHash = "5d5aea53560307207b175bab14d39138756165598972d7604491f4cd44d7bfc6";
    private const string StorageWriteHash = "887e2f79ecea6543a5ba56aecd08b4c6c82dcda8b790ee81a3e8e5a568249433";
    private const string StorageReadHash = "22a81b5a27a6ae99a20b31601d17b87e6fed1c576da135ea43065e0927de6d01";
    private const string MainMenuUpdateHash = "98bef27d1b1ed69161d3bc16472a891174b87f023e936b929e7d5ec7b19fd0b2";
    private const string LeaderboardsUpdateHash = "5b5d6dcc387237ee80f60ec5e599714c31c4a6b877ecd0b4e1ab6138d3df2a19";
    private const string MenuItemDrawHash = "f72bc14c85ee9de04d11c79cb67437d6d77fcf857ac560f8581e0f7a402fb550";
    private const string PlayerSettingsCtorHash = "87b74f99337495a86a9b364e8005b62b386334765edce14db0cf606714e66b3e";
    private const string GameDrawHash = "9a07780c3bcab593902543edd0b930e4920c380ba40f03d9f77bf6c609700680";
    private const string SettingsClickHash = "562c747623dfa56402f3b050d2d113fb7092e50fb23aa43c9e59978be5a7ebca";
    private const string SettingsCancelHash = "f9603f155bd0fa415014acf3d4f0fb7960ade1f793ccc1ea7060968645d162bd";
    private const int MethodToken = 0x06000152;
    private const int TouchMethodToken = 0x0600007a;
    private const int CallOffset = 0x0015;
    private const string MethodSignature = "0x06000152 System.Void ZombiesWP7.custom.CustomMgr::Init(Microsoft.Xna.Framework.Graphics.GraphicsDevice,Microsoft.Xna.Framework.Content.ContentManager)";

    public static RewriteResult Apply(string originalDll, string preparedDll, string hooksDll, string outputDirectory)
    {
        var final = Path.GetFullPath(outputDirectory);
        var privateRoot = Path.GetFullPath(Path.Combine("private", "prepared"));
        if (!final.StartsWith(privateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            Path.Exists(final))
            throw new PreparationException("Hook output must be a new directory under private/prepared/.");
        if (Hash(originalDll) != OriginalHash)
            throw new PreparationException("Hook source DLL does not match the supported XAP.");
        using (var pe = new PEReader(File.OpenRead(originalDll)))
        {
            VerifyBody(pe, MethodToken, OriginalMethodBodyHash);
            VerifyBody(pe, TouchMethodToken, TouchBodyHash);
            VerifyBody(pe, 0x06000123, StorageWriteHash);
            VerifyBody(pe, 0x06000124, StorageReadHash);
            VerifyBody(pe, 0x06000081, MainMenuUpdateHash);
            VerifyBody(pe, 0x06000072, LeaderboardsUpdateHash);
            VerifyBody(pe, 0x06000103, MenuItemDrawHash);
            VerifyBody(pe, 0x06000251, PlayerSettingsCtorHash);
            VerifyBody(pe, 0x060000cf, GameDrawHash);
            VerifyBody(pe, 0x060002c4, SettingsClickHash);
            VerifyBody(pe, 0x060002c5, SettingsCancelHash);
        }
        var priorManifest = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(preparedDll))!, "patch-manifest.json");
        var prior = JsonSerializer.Deserialize<PatchManifest>(File.ReadAllText(priorManifest),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new PreparationException("Prepared patch manifest is invalid.");
        var preparedHash = Hash(preparedDll);
        if (prior.SourceSha256 != OriginalHash || prior.PreparedSha256 != preparedHash ||
            prior.Patches.Any(patch => patch.Kind == "gameplay-call"))
            throw new PreparationException("Prepared DLL does not match its original guarded rewrite.");
        var hooksHash = Hash(hooksDll);
        if (hooksHash is not (DebugHooksHash or ReleaseHooksHash) ||
            System.Reflection.AssemblyName.GetAssemblyName(hooksDll).Name != "Port.Hooks")
            throw new PreparationException("Hook target assembly identity differs.");

        var parent = Path.GetDirectoryName(final)!;
        Directory.CreateDirectory(parent);
        var stage = Path.Combine(parent, $".{Path.GetFileName(final)}.stage-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stage);
            var stageDll = Path.Combine(stage, "ZombiesWP7.dll");
            using (var module = ModuleDefinition.ReadModule(preparedDll, new ReaderParameters { InMemory = true }))
            using (var hooks = ModuleDefinition.ReadModule(hooksDll, new ReaderParameters { InMemory = true }))
            {
                var method = module.Types.Single(type => type.FullName == "ZombiesWP7.custom.CustomMgr")
                    .Methods.Single(candidate => candidate.MetadataToken.ToInt32() == MethodToken);
                if (method.FullName != MethodSignature[11..] || !method.HasBody)
                    throw new PreparationException("Prepared hook method signature differs from its guard.");
                var calls = method.Body.Instructions.Where(instruction => instruction.Offset == CallOffset).ToArray();
                if (calls.Length != 1 || calls[0].OpCode != OpCodes.Call ||
                    calls[0].Operand is not MethodReference oldTarget ||
                    oldTarget.DeclaringType.FullName != "Microsoft.Xna.Framework.Media.MediaSource" ||
                    oldTarget.Name != "GetAvailableMediaSources" ||
                    oldTarget.Parameters.Count != 0 ||
                    oldTarget.ReturnType.FullName != "System.Collections.Generic.IList`1<Microsoft.Xna.Framework.Media.MediaSource>")
                    throw new PreparationException("Media source call anchor differs from its guard.");
                var newTarget = hooks.Types.Single(type => type.FullName == "Port.Hooks.DesktopMediaSources")
                    .Methods.Single(candidate => candidate.Name == "GetAvailableMediaSources" &&
                        candidate.IsStatic && candidate.Parameters.Count == 0 &&
                        candidate.ReturnType.FullName == oldTarget.ReturnType.FullName);
                calls[0].Operand = module.ImportReference(newTarget);

                var touch = module.Types.Single(type => type.FullName == "ZombiesWP7.TouchMgr")
                    .Methods.Single(candidate => candidate.MetadataToken.ToInt32() == TouchMethodToken);
                if (touch.FullName != "System.Void ZombiesWP7.TouchMgr::Update()" || !touch.HasBody)
                    throw new PreparationException("Touch update signature differs from its guard.");
                ReplaceTouchCall(module, hooks, touch, 0x00d2, "Microsoft.Xna.Framework.Input.GamePad", "GetState", "GetGamePadState");
                ReplaceTouchCall(module, hooks, touch, 0x017b, "Microsoft.Xna.Framework.Input.Touch.TouchPanel", "GetState", "GetState");
                ReplaceTouchCall(module, hooks, touch, 0x0785, "Microsoft.Xna.Framework.Input.Touch.TouchPanel", "get_IsGestureAvailable", "get_IsGestureAvailable");
                ReplaceTouchCall(module, hooks, touch, 0x078f, "Microsoft.Xna.Framework.Input.Touch.TouchPanel", "ReadGesture", "ReadGesture");
                PatchStorage(module, hooks);
                PatchOfflineLeaderboards(module);
                BrightenLockedAchievements(module);
                DisableTouchPadsByDefault(module);
                PatchDisplay(module, hooks);
                module.Write(stageDll, new WriterParameters { WriteSymbols = false });
            }
            using (var check = ModuleDefinition.ReadModule(stageDll))
            {
                var method = check.Types.Single(type => type.FullName == "ZombiesWP7.custom.CustomMgr")
                    .Methods.Single(candidate => candidate.Name == "Init");
                var call = method.Body.Instructions.Single(instruction => instruction.Offset == CallOffset);
                if (call.Operand is not MethodReference target ||
                    target.DeclaringType.FullName != "Port.Hooks.DesktopMediaSources" ||
                    target.Name != "GetAvailableMediaSources")
                    throw new PreparationException("Hook call postcondition failed.");
                var touch = check.Types.Single(type => type.FullName == "ZombiesWP7.TouchMgr")
                    .Methods.Single(candidate => candidate.Name == "Update");
                foreach (var offset in new[] { 0x00d2, 0x017b, 0x0785, 0x078f })
                {
                    var adapted = touch.Body.Instructions.Single(instruction => instruction.Offset == offset);
                    if (adapted.Operand is not MethodReference input ||
                        input.DeclaringType.FullName != "Port.Hooks.DesktopInput")
                        throw new PreparationException($"Desktop input postcondition failed at IL_{offset:x4}.");
                }
                var storage = check.Types.Single(type => type.FullName == "ZombiesWP7.storage.StorageMgr");
                foreach (var name in new[] { "Read", "Write" })
                {
                    var body = storage.Methods.Single(candidate => candidate.Name == name).Body;
                    if (!body.Instructions.Any(item => item.Operand is MethodReference call &&
                        call.DeclaringType.FullName == "Port.Hooks.DesktopStorage" && call.Name == name))
                        throw new PreparationException($"Desktop storage {name} postcondition failed.");
                }
                if (check.Types.SelectMany(type => type.Methods).Count(method => method.Parameters.Count == 1 &&
                    method.Parameters[0].ParameterType.FullName == "System.IO.Stream" &&
                    StorageSerializers.Contains(method.DeclaringType.FullName)) != 10)
                    throw new PreparationException("Desktop serializer signatures did not update.");
                VerifyOfflineLeaderboards(check);
                VerifyLockedAchievements(check);
                VerifyTouchPadDefault(check);
                VerifyDisplay(check);
            }
            var outputHash = Hash(stageDll);
            var patch = new PatchRecord("gameplay-call", OriginalHash, MethodSignature,
                OriginalMethodBodyHash, $"IL_0015:call Port.Hooks.DesktopMediaSources.GetAvailableMediaSources() [hooksSha256={hooksHash}]",
                "one call target is Port.Hooks; original method control flow is preserved");
            var manifest = prior with
            {
                ToolVersion = "0.11.0",
                PreparedSha256 = outputHash,
                Plan = "pinned ABI rewrite plus guarded desktop media, input, storage and offline leaderboards",
                Patches = [.. prior.Patches, patch,
                    new PatchRecord("gameplay-call", OriginalHash,
                        "0x0600007a System.Void ZombiesWP7.TouchMgr::Update()", TouchBodyHash,
                        $"IL_00d2,017b,0785,078f:call Port.Hooks.DesktopInput [hooksSha256={hooksHash}]",
                        "four input call targets are Port.Hooks; original method control flow is preserved"),
                    new PatchRecord("gameplay-call", OriginalHash,
                        "0x06000123/0124 ZombiesWP7.storage.StorageMgr::Write/Read()",
                        StorageWriteHash + "/" + StorageReadHash,
                        $"two methods call Port.Hooks.DesktopStorage; ten serializer parameters use Stream [hooksSha256={hooksHash}]",
                        "original version-101 serializers remain in the prepared game"),
                    new PatchRecord("gameplay-call", OriginalHash,
                        "0x06000081 Viking_x86.main.MainMenu::Update(); 0x06000072 IMAK3Z0MB1EGAEM.menu.Leaderboards::Update()",
                        MainMenuUpdateHash + "/" + LeaderboardsUpdateHash,
                        "desktop local profile enters the leaderboard reader; offline explanatory text replaces phone sign-in/rank text",
                        "local leaderboard menu stays active with a completed empty reader; original online submission gate is unchanged"),
                    new PatchRecord("gameplay-call", OriginalHash,
                        "0x06000103 System.Void ZombiesWP7.menu.MenuItem::Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch,System.Single,Microsoft.Xna.Framework.Vector2)",
                        MenuItemDrawHash,
                        "two locked-achievement icon/name opacity multipliers change from 0.4 to 0.7",
                        "locked entries stay dimmer than earned entries and are readable on desktop"),
                    new PatchRecord("gameplay-call", OriginalHash,
                        "0x06000251 System.Void ZombiesWP7.player.Settings::.ctor()",
                        PlayerSettingsCtorHash,
                        "IL_0001:ldc.i4.0 initializes showTouchPads to false",
                        "saved player preferences still load normally"),
                    new PatchRecord("gameplay-call", OriginalHash,
                        "0x060000cf ZombiesWP7.Game1::Draw(); 0x060002c4/2c5 IMAK3Z0MB1EGAEM.menu.Settings::Click/Cancel",
                        GameDrawHash + "/" + SettingsClickHash + "/" + SettingsCancelHash,
                        "draw through a 600x360 logical target and route native Display page actions to Port.Hooks.DesktopDisplay",
                        "original game coordinates and Settings actions remain intact")]
            };
            File.WriteAllText(Path.Combine(stage, "patch-manifest.json"),
                JsonSerializer.Serialize(manifest,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }) + "\n");
            Directory.Move(stage, final);
            return new RewriteResult(final, outputHash);
        }
        catch
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            throw;
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Hex(SHA256.HashData(stream));
    }
    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    private static void VerifyBody(PEReader pe, int token, string expected)
    {
        var definition = pe.GetMetadataReader().GetMethodDefinition(
            MetadataTokens.MethodDefinitionHandle(token & 0x00ffffff));
        var body = pe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes()
            ?? throw new PreparationException($"Original method 0x{token:x8} body is missing.");
        if (Hex(SHA256.HashData(body)) != expected)
            throw new PreparationException($"Original method 0x{token:x8} body differs from its guard.");
    }

    private static void ReplaceTouchCall(ModuleDefinition module, ModuleDefinition hooks,
        Mono.Cecil.MethodDefinition method, int offset, string oldType, string oldName, string newName)
    {
        var instruction = method.Body.Instructions.SingleOrDefault(item => item.Offset == offset);
        if (instruction?.OpCode != OpCodes.Call || instruction.Operand is not MethodReference old ||
            old.DeclaringType.FullName != oldType || old.Name != oldName)
            throw new PreparationException($"Desktop input call anchor differs at IL_{offset:x4}.");
        var replacement = hooks.Types.Single(type => type.FullName == "Port.Hooks.DesktopInput")
            .Methods.Single(candidate => candidate.Name == newName && candidate.IsStatic &&
                candidate.ReturnType.FullName == old.ReturnType.FullName &&
                candidate.Parameters.Count == old.Parameters.Count &&
                candidate.Parameters.Select(parameter => parameter.ParameterType.FullName)
                    .SequenceEqual(old.Parameters.Select(parameter => parameter.ParameterType.FullName)));
        instruction.Operand = module.ImportReference(replacement);
    }

    private static readonly HashSet<string> StorageSerializers =
    [
        "ZombiesWP7.custom.CustomMgr", "ZombiesWP7.custom.CustomImg",
        "ZombiesWP7.store.PlayerProfile", "ZombiesWP7.player.Settings", "ZombiesWP7.player.Player"
    ];

    private static void PatchStorage(ModuleDefinition module, ModuleDefinition hooks)
    {
        var serializerMethods = module.Types.SelectMany(type => type.Methods)
            .Where(method => StorageSerializers.Contains(method.DeclaringType.FullName) &&
                method.Name is "Read" or "Write" && method.Parameters.Count == 1 &&
                method.Parameters[0].ParameterType.FullName == "System.IO.IsolatedStorage.IsolatedStorageFileStream")
            .ToArray();
        if (serializerMethods.Length != 10)
            throw new PreparationException("Original serializer signature set differs.");
        var streamType = module.ImportReference(typeof(Stream));
        foreach (var method in serializerMethods) method.Parameters[0].ParameterType = streamType;
        foreach (var reference in module.GetMemberReferences().OfType<MethodReference>())
            if (StorageSerializers.Contains(reference.DeclaringType.FullName) &&
                reference.Name is "Read" or "Write" && reference.Parameters.Count == 1 &&
                reference.Parameters[0].ParameterType.FullName == "System.IO.IsolatedStorage.IsolatedStorageFileStream")
                reference.Parameters[0].ParameterType = streamType;

        var storage = module.Types.Single(type => type.FullName == "ZombiesWP7.storage.StorageMgr");
        var game = module.Types.Single(type => type.FullName == "ZombiesWP7.Game1");
        var fields = new[] { "profile", "player", "custom" }.Select(name =>
            game.Fields.Single(field => field.Name == name)).ToArray();
        var hook = hooks.Types.Single(type => type.FullName == "Port.Hooks.DesktopStorage");
        foreach (var name in new[] { "Write", "Read" })
        {
            var method = storage.Methods.Single(candidate => candidate.Name == name &&
                candidate.Parameters.Count == 0 && candidate.ReturnType.FullName == "System.Void");
            var target = hook.Methods.Single(candidate => candidate.Name == name && candidate.IsStatic &&
                candidate.Parameters.Count == 3 && candidate.Parameters.All(parameter =>
                    parameter.ParameterType.FullName == "System.Object"));
            method.Body = new Mono.Cecil.Cil.MethodBody(method);
            var il = method.Body.GetILProcessor();
            var end = il.Create(OpCodes.Ret);
            if (name == "Write")
            {
                var trial = module.Types.Single(type => type.FullName == "ZombiesWP7.xdktools.TrialMgr")
                    .Methods.Single(candidate => candidate.Name == "IsTrial" && candidate.IsStatic);
                il.Append(il.Create(OpCodes.Call, trial));
                il.Append(il.Create(OpCodes.Brtrue_S, end));
            }
            foreach (var field in fields) il.Append(il.Create(OpCodes.Ldsfld, field));
            il.Append(il.Create(OpCodes.Call, module.ImportReference(target)));
            il.Append(end);
        }
    }

    private static void PatchOfflineLeaderboards(ModuleDefinition module)
    {
        var main = module.Types.Single(type => type.FullName == "Viking_x86.main.MainMenu")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000081);
        var board = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.Leaderboards")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000072);
        ReplaceSigninRead(main, 0x05fe);
        ReplaceSigninRead(board, 0x0205);
        ReplaceText(board, 0x0170, "You are not yet ranked", "Online leaderboards");
        ReplaceText(board, 0x017e, "on this leaderboard.", "are unavailable offline.");
    }

    private static void VerifyOfflineLeaderboards(ModuleDefinition module)
    {
        var main = module.Types.Single(type => type.FullName == "Viking_x86.main.MainMenu")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000081);
        var board = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.Leaderboards")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000072);
        foreach (var (method, offset) in new[] { (main, 0x05fe), (board, 0x0205) })
            if (method.Body.Instructions.Single(item => item.Offset == offset).OpCode != OpCodes.Ldc_I4_3)
                throw new PreparationException("Offline leaderboard sign-in UI postcondition failed.");
        foreach (var (offset, text) in new[] { (0x0170, "Online leaderboards"), (0x017e, "are unavailable offline.") })
            if (board.Body.Instructions.Single(item => item.Offset == offset).Operand as string != text)
                throw new PreparationException("Offline leaderboard text postcondition failed.");
    }

    private static void ReplaceSigninRead(Mono.Cecil.MethodDefinition method, int offset)
    {
        var instruction = method.Body.Instructions.SingleOrDefault(item => item.Offset == offset);
        if (instruction?.OpCode != OpCodes.Ldsfld || instruction.Operand is not FieldReference field ||
            field.Name != "signinStatus" || field.DeclaringType.FullName != "ZombiesWP7.Game1")
            throw new PreparationException($"Leaderboard sign-in anchor differs at IL_{offset:x4}.");
        instruction.OpCode = OpCodes.Ldc_I4_3;
        instruction.Operand = null;
    }

    private static void ReplaceText(Mono.Cecil.MethodDefinition method, int offset, string oldText, string newText)
    {
        var instruction = method.Body.Instructions.SingleOrDefault(item => item.Offset == offset);
        if (instruction?.OpCode != OpCodes.Ldstr || instruction.Operand as string != oldText)
            throw new PreparationException($"Leaderboard text anchor differs at IL_{offset:x4}.");
        instruction.Operand = newText;
    }

    private static void BrightenLockedAchievements(ModuleDefinition module)
    {
        var draw = module.Types.Single(type => type.FullName == "ZombiesWP7.menu.MenuItem")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000103);
        foreach (var offset in new[] { 0x01ca, 0x0242 })
        {
            var instruction = draw.Body.Instructions.SingleOrDefault(item => item.Offset == offset);
            if (instruction?.OpCode != OpCodes.Ldc_R4 || instruction.Operand is not float old || old != 0.4f)
                throw new PreparationException($"Locked achievement opacity anchor differs at IL_{offset:x4}.");
            instruction.Operand = 0.7f;
        }
    }

    private static void VerifyLockedAchievements(ModuleDefinition module)
    {
        var draw = module.Types.Single(type => type.FullName == "ZombiesWP7.menu.MenuItem")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000103);
        foreach (var offset in new[] { 0x01ca, 0x0242 })
        {
            var instruction = draw.Body.Instructions.Single(item => item.Offset == offset);
            if (instruction.OpCode != OpCodes.Ldc_R4 || instruction.Operand is not float value || value != 0.7f)
                throw new PreparationException("Locked achievement contrast postcondition failed.");
        }
    }

    private static void DisableTouchPadsByDefault(ModuleDefinition module)
    {
        var ctor = module.Types.Single(type => type.FullName == "ZombiesWP7.player.Settings")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000251);
        var value = ctor.Body.Instructions.SingleOrDefault(item => item.Offset == 0x0001);
        if (value?.OpCode != OpCodes.Ldc_I4_1 || value.Next?.OpCode != OpCodes.Stfld ||
            value.Next.Operand is not FieldReference field || field.Name != "showTouchPads" ||
            field.DeclaringType.FullName != "ZombiesWP7.player.Settings")
            throw new PreparationException("Touch pad default anchor differs from its guard.");
        value.OpCode = OpCodes.Ldc_I4_0;
    }

    private static void VerifyTouchPadDefault(ModuleDefinition module)
    {
        var ctor = module.Types.Single(type => type.FullName == "ZombiesWP7.player.Settings")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000251);
        if (ctor.Body.Instructions.Single(item => item.Offset == 0x0001).OpCode != OpCodes.Ldc_I4_0)
            throw new PreparationException("Touch pad default postcondition failed.");
    }

    private static void PatchDisplay(ModuleDefinition module, ModuleDefinition hooks)
    {
        var display = hooks.Types.Single(type => type.FullName == "Port.Hooks.DesktopDisplay");
        MethodReference Target(string name, int arguments) => module.ImportReference(display.Methods.Single(method =>
            method.Name == name && method.IsStatic && method.Parameters.Count == arguments));

        var draw = module.Types.Single(type => type.FullName == "ZombiesWP7.Game1")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x060000cf);
        var begin = draw.Body.Instructions.SingleOrDefault(item => item.Offset == 0x002d);
        var end = draw.Body.Instructions.SingleOrDefault(item => item.Offset == 0x027c);
        if (begin?.OpCode != OpCodes.Ldarg_0 || end?.OpCode != OpCodes.Ldarg_0 ||
            end.Next?.OpCode != OpCodes.Ldarg_1 ||
            end.Next.Next?.Operand is not MethodReference baseDraw || baseDraw.FullName !=
                "System.Void Microsoft.Xna.Framework.Game::Draw(Microsoft.Xna.Framework.GameTime)")
            throw new PreparationException("Original display draw anchors differ.");
        var il = draw.Body.GetILProcessor();
        begin.OpCode = OpCodes.Call;
        begin.Operand = Target("BeginFrame", 0);
        il.InsertAfter(begin, il.Create(OpCodes.Ldarg_0));
        end.OpCode = OpCodes.Call;
        end.Operand = Target("EndFrame", 0);
        il.InsertAfter(end, il.Create(OpCodes.Ldarg_0));

        var click = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.Settings")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x060002c4);
        var first = click.Body.Instructions.First();
        if (first.OpCode != OpCodes.Ldarg_1 || click.Parameters.Count != 1 ||
            click.Parameters[0].ParameterType.FullName != "System.String")
            throw new PreparationException("Original Settings.Click anchor differs.");
        il = click.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(first, il.Create(OpCodes.Call, Target("HandleSettingsClick", 2)));
        il.InsertBefore(first, il.Create(OpCodes.Brfalse, first));
        il.InsertBefore(first, il.Create(OpCodes.Ret));

        var cancel = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.Settings")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x060002c5);
        var cancelFirst = cancel.Body.Instructions.First();
        if (cancelFirst.OpCode != OpCodes.Ldsfld || cancel.Parameters.Count != 0)
            throw new PreparationException("Original Settings.Cancel anchor differs.");
        il = cancel.Body.GetILProcessor();
        il.InsertBefore(cancelFirst, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(cancelFirst, il.Create(OpCodes.Call, Target("HandleSettingsCancel", 1)));
        il.InsertBefore(cancelFirst, il.Create(OpCodes.Brfalse, cancelFirst));
        il.InsertBefore(cancelFirst, il.Create(OpCodes.Ret));
    }

    private static void VerifyDisplay(ModuleDefinition module)
    {
        var draw = module.Types.Single(type => type.FullName == "ZombiesWP7.Game1")
            .Methods.Single(method => method.Name == "Draw");
        var click = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.Settings")
            .Methods.Single(method => method.Name == "Click");
        var cancel = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.Settings")
            .Methods.Single(method => method.Name == "Cancel");
        foreach (var name in new[] { "BeginFrame", "EndFrame" })
            if (draw.Body.Instructions.Count(item => item.Operand is MethodReference method &&
                method.DeclaringType.FullName == "Port.Hooks.DesktopDisplay" && method.Name == name) != 1)
                throw new PreparationException($"Display {name} postcondition failed.");
        if (click.Body.Instructions.Count(item => item.Operand is MethodReference method &&
                method.DeclaringType.FullName == "Port.Hooks.DesktopDisplay" && method.Name == "HandleSettingsClick") != 1)
            throw new PreparationException("Settings display entry postcondition failed.");
        if (cancel.Body.Instructions.Count(item => item.Operand is MethodReference method &&
                method.DeclaringType.FullName == "Port.Hooks.DesktopDisplay" && method.Name == "HandleSettingsCancel") != 1)
            throw new PreparationException("Settings display cancel postcondition failed.");
    }
}
