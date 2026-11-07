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
    private const string DebugHooksHash = "7a8240e565b5ff37b3400e3a4208375e98917a7d5ea7e52adfde4da06a22c23c";
    private const string ReleaseHooksHash = "8f293d7915897e7db900a6c506034faa0d3f082cba83f1846ec2fcdc15bf627c";
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
    private const string CustomMapperInitHash = "e810e1fbeb78b8cb9446befd86cb35dbb680eafd86ba622a7c8402c5f2849683";
    private const string HelpAndOptionsCtorHash = "43f6813612389d29eb7452179c2e01f10f06c73307142ba0e7f29a0730c1abf5";
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
            VerifyBody(pe, 0x060002cc, CustomMapperInitHash);
            VerifyBody(pe, 0x060001f9, HelpAndOptionsCtorHash);
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
                PatchReducedFlashing(module, hooks);
                PatchMenuVisuals(module, hooks);
                PatchPictures(module, hooks);
                RemoveHelpAndOptionsInfo(module);
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
                VerifyReducedFlashing(check);
                VerifyMenuVisuals(check);
                VerifyPictures(check);
                VerifyHelpAndOptionsInfo(check);
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
                        "original game coordinates and Settings actions remain intact"),
                    new PatchRecord("gameplay-call", OriginalHash,
                        "0x06000085 Viking_x86.main.MainMenu::Draw(); 0x06000103 ZombiesWP7.menu.MenuItem::Draw()",
                        MenuItemDrawHash,
                        "draw the animated title and tint focused menu items through Port.Hooks.MenuVisuals",
                        "reduced flashing uses steady title and focus colors"),
                    new PatchRecord("gameplay-call", OriginalHash,
                        "Viking_x86.vikinggame.world.WorldTools::DrawLightRow(SpriteBatch,int,Vector2)",
                        OriginalHash,
                        "map strobe rows to steady colors through Port.Hooks.DesktopDisplay when reduced flashing is enabled",
                        "other light rows retain their original color"),
                    new PatchRecord("gameplay-call", OriginalHash,
                        "0x060002cc IMAK3Z0MB1EGAEM.menu.CustomMapper::Init()",
                        CustomMapperInitHash,
                        "choose and load a desktop picture folder before opening the mapper",
                        "the creator uses imported thumbnails through its original source list"),
                    new PatchRecord("gameplay-il", OriginalHash,
                        "0x060001f9 IMAK3Z0MB1EGAEM.menu.HelpAndOptions::.ctor()",
                        HelpAndOptionsCtorHash,
                        "set the Help and Options info block to null, removing version and contact text",
                        "Help, Settings, and Credits menu items remain unchanged")]
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
        // Rewriting the focus-color calls moves offsets; identify the three
        // earned/description alpha multipliers by their surrounding operands.
        if (draw.Body.Instructions.Count(instruction =>
                instruction.OpCode == OpCodes.Ldc_R4 && instruction.Operand is float value && value == 0.7f &&
                instruction.Previous?.OpCode == OpCodes.Ldarg_2 && instruction.Next?.OpCode == OpCodes.Mul) != 3)
            throw new PreparationException("Locked achievement contrast postcondition failed.");
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

    private static void PatchMenuVisuals(ModuleDefinition module, ModuleDefinition hooks)
    {
        var visuals = hooks.Types.Single(type => type.FullName == "Port.Hooks.MenuVisuals");
        MethodReference Target(string name) => module.ImportReference(visuals.Methods.Single(method =>
            method.Name == name && method.IsStatic));
        var title = module.Types.Single(type => type.FullName == "Viking_x86.main.MainMenu")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000085);
        var sceneDraw = title.Body.Instructions.SingleOrDefault(item => item.Offset == 0x0037);
        if (sceneDraw?.Operand is not MethodReference sceneTarget ||
            sceneTarget.FullName != "System.Void SceneEdit.scene.SceneMgr::Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch)")
            throw new PreparationException("Main menu title draw anchor differs.");
        var il = title.Body.GetILProcessor();
        il.InsertAfter(sceneDraw, il.Create(OpCodes.Ldarg_1));
        il.InsertAfter(sceneDraw.Next, il.Create(OpCodes.Call, Target("DrawTitle")));

        var itemDraw = module.Types.Single(type => type.FullName == "ZombiesWP7.menu.MenuItem")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000103);
        foreach (var offset in new[] { 0x00c7, 0x0173 })
        {
            var color = itemDraw.Body.Instructions.SingleOrDefault(item => item.Offset == offset);
            if (color?.OpCode != OpCodes.Newobj || color.Operand is not MethodReference constructor ||
                constructor.FullName != "System.Void Microsoft.Xna.Framework.Color::.ctor(System.Single,System.Single,System.Single,System.Single)" ||
                color.Next?.OpCode != OpCodes.Ldc_I4_2)
                throw new PreparationException($"Menu item focus color anchor differs at IL_{offset:x4}.");
            il = itemDraw.Body.GetILProcessor();
            il.InsertAfter(color, il.Create(OpCodes.Ldarg_0));
            il.InsertAfter(color.Next, il.Create(OpCodes.Call, Target("Highlight")));
        }
    }

    private static void PatchReducedFlashing(ModuleDefinition module, ModuleDefinition hooks)
    {
        var world = module.Types.Single(type => type.FullName == "Viking_x86.vikinggame.world.WorldTools")
            .Methods.Single(method => method.Name == "DrawLightRow" && method.Parameters.Count == 3 &&
                method.Parameters[1].ParameterType.FullName == "System.Int32");
        var first = world.Body.Instructions.First();
        var target = hooks.Types.Single(type => type.FullName == "Port.Hooks.DesktopDisplay")
            .Methods.Single(method => method.Name == "SteadyLightRow" && method.Parameters.Count == 1);
        var il = world.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_2));
        il.InsertBefore(first, il.Create(OpCodes.Call, module.ImportReference(target)));
        il.InsertBefore(first, il.Create(OpCodes.Starg_S, world.Parameters[1]));
    }

    private static void VerifyReducedFlashing(ModuleDefinition module)
    {
        var world = module.Types.Single(type => type.FullName == "Viking_x86.vikinggame.world.WorldTools")
            .Methods.Single(method => method.Name == "DrawLightRow" && method.Parameters.Count == 3);
        if (world.Body.Instructions.Count(item => item.Operand is MethodReference method &&
                method.DeclaringType.FullName == "Port.Hooks.DesktopDisplay" && method.Name == "SteadyLightRow") != 1)
            throw new PreparationException("Reduced flashing light-row hook postcondition failed.");
    }

    private static void VerifyMenuVisuals(ModuleDefinition module)
    {
        foreach (var (typeName, name, hook, count) in new[]
        {
            ("Viking_x86.main.MainMenu", "Draw", "DrawTitle", 1),
            ("ZombiesWP7.menu.MenuItem", "Draw", "Highlight", 2)
        })
        {
            var method = module.Types.Single(type => type.FullName == typeName)
                .Methods.Single(candidate => candidate.Name == name);
            if (method.Body.Instructions.Count(item => item.Operand is MethodReference target &&
                target.DeclaringType.FullName == "Port.Hooks.MenuVisuals" && target.Name == hook) != count)
                throw new PreparationException($"Menu visual hook {hook} postcondition failed.");
        }
    }

    private static void PatchPictures(ModuleDefinition module, ModuleDefinition hooks)
    {
        var pictureHooks = hooks.Types.Single(type => type.FullName == "Port.Hooks.DesktopPictures");
        MethodReference Target(string name) => module.ImportReference(pictureHooks.Methods.Single(method =>
            method.Name == name && method.IsStatic && method.Parameters.Count == 2 &&
            method.Parameters.All(parameter => parameter.ParameterType.FullName == "System.Object")));
        var mapper = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.CustomMapper")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x060002cc);
        var first = mapper.Body.Instructions.First();
        if (first.OpCode != OpCodes.Ldsfld || first.Operand is not FieldReference gameCustom ||
            gameCustom.FullName != "ZombiesWP7.custom.CustomMgr ZombiesWP7.Game1::custom")
            throw new PreparationException("CustomMapper.Init picture picker anchor differs.");
        var il = mapper.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldsfld, gameCustom));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, Target("ChooseFolder")));
    }

    private static void VerifyPictures(ModuleDefinition module)
    {
        foreach (var (typeName, methodName, hookName) in new[]
        {
            ("IMAK3Z0MB1EGAEM.menu.CustomMapper", "Init", "ChooseFolder")
        })
        {
            var method = module.Types.Single(type => type.FullName == typeName)
                .Methods.Single(candidate => candidate.Name == methodName);
            if (method.Body.Instructions.Count(item => item.Operand is MethodReference target &&
                target.DeclaringType.FullName == "Port.Hooks.DesktopPictures" &&
                target.Name == hookName) != 1)
                throw new PreparationException($"Picture hook {hookName} postcondition failed.");
        }
    }

    private static void RemoveHelpAndOptionsInfo(ModuleDefinition module)
    {
        var constructor = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.HelpAndOptions")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x060001f9);
        var instructions = constructor.Body.Instructions;
        var loadThis = instructions.Single(item => item.Offset == 0x0082);
        var size = instructions.Single(item => item.Offset == 0x0083);
        var array = instructions.Single(item => item.Offset == 0x0084);
        var store = instructions.Single(item => item.Offset == 0x00a5);
        var end = instructions.Single(item => item.Offset == 0x00aa);
        if (loadThis.OpCode != OpCodes.Ldarg_0 || size.OpCode != OpCodes.Ldc_I4_2 ||
            array.OpCode != OpCodes.Newarr ||
            array.Operand is not Mono.Cecil.TypeReference arrayType || arrayType.FullName != "System.Text.StringBuilder" ||
            store.OpCode != OpCodes.Stfld || store.Operand is not FieldReference infoField ||
            infoField.FullName != "System.Text.StringBuilder[] IMAK3Z0MB1EGAEM.menu.MenuLevel::infos" ||
            end.OpCode != OpCodes.Ret ||
            !instructions.Any(item => item.Offset == 0x008c && item.Operand is string version && version == "Version 1.0") ||
            !instructions.Any(item => item.Offset == 0x0099 && item.Operand is string contact &&
                contact == "Contact us: support@ska-studios.com"))
            throw new PreparationException("Help and Options info block differs from its guard.");
        size.OpCode = OpCodes.Ldnull;
        size.Operand = null;
        array.OpCode = OpCodes.Stfld;
        array.Operand = infoField;
        var il = constructor.Body.GetILProcessor();
        for (var instruction = array.Next; instruction != end;)
        {
            var next = instruction.Next;
            il.Remove(instruction);
            instruction = next;
        }
    }

    private static void VerifyHelpAndOptionsInfo(ModuleDefinition module)
    {
        var constructor = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.HelpAndOptions")
            .Methods.Single(method => method.Name == ".ctor" && method.Parameters.Count == 0);
        var last = constructor.Body.Instructions;
        if (last[^4].OpCode != OpCodes.Ldarg_0 || last[^3].OpCode != OpCodes.Ldnull ||
            last[^2].OpCode != OpCodes.Stfld || last[^2].Operand is not FieldReference infoField ||
            infoField.Name != "infos" || last[^1].OpCode != OpCodes.Ret ||
            last.Any(item => item.Operand is string value &&
                (value == "Version 1.0" || value == "Contact us: support@ska-studios.com")))
            throw new PreparationException("Help and Options info removal postcondition failed.");
    }
}
