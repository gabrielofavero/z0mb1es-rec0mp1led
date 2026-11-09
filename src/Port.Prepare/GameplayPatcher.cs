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
    private const string DebugHooksHash = "a3d593da7e7e00dba15e4b7e95053084bbcaa04ff69493b31f3ad8b150cd4afb";
    private const string ReleaseHooksHash = "3f1ea01ade0989545a8bf1335ea7c3475bc3b024823545eef267306a035e3ee4";
    private const string CurrentDebugHooksHash = "bb1b1d27e24a45c777536dd517ad0ff0293a5a6d497779509aacd4f408b60ddc";
    private const string CurrentReleaseHooksHash = "c1381d8520fc42ec89c41a5b45ec05caed93185cf18a9f23219d45bdb31c11a6";
    private const string LatestReleaseHooksHash = "bc9f9041175ca757e1285b6cd1f0244ba62f1aadc893ab5396f4a2508034e723";
    private const string RestartReleaseHooksHash = "dd257b258593fbf90688246cd293886e1816bc650401f46aa58eaae1521b9af1";
    private const string EdgeArtReleaseHooksHash = "3c924ed10f6a4bbfdf520366f06db470fc9886fadd9b79618da449314390024f";
    private const string VersionReleaseHooksHash = "5dffb36be4520249eb8f330a6b41a69b09b982e81bc8b1bbd60bc7ef5dc25d83";
    private const string CreditsScrollHooksHash = "b1f077e36eb6cad92b7ea51c955c668561cb9e45ce92f1f59a4ce77473c8f36e";
    private const string PortCreditHooksHash = "18232ffefca16d60f25252ec1456ff59120209ee8908084ca97a98466a038973";
    private const string PictureButtonHooksHash = "2082c82f97da16019bf715a2b8fce8852c916d458cece54a9e223356b0c816a9";
    private const string InstallerFoldersHooksHash = "cb8f71229484884eb4100d9056b8ef1a705df35e0439d0fdaa43d5314e337149";
    private const string DocumentsSavesHooksHash = "d19ee6e0a935b7cf5c923217063bced1668747bf2967be5f00e93f032f7e201a";
    private const string CenteredPicturesHooksHash = "2c2492eb771da5e9f5f28c15175dd4d37bb4e0fd1dc0d14cd5831c226e616071";
    private const string ReducedFlashFocusHooksHash = "c5cfccbb70dd61e038207ca039eced399621a9c12bd32f167c923d9b8a4b578f";
    private const string CreatorQualityHooksHash = "5418daaabbebe5089d525d5a000927dc4d83f23e231691e9af14a9fa0d5628ca";
    private const string ResolutionHooksHash = "ec882ec8281665391fe3bd4d44aceaa11f11b5e693957ffbcb80836cb3b30771";
    private const string RowNavigationHooksHash = "90e53a6843ac9682512d80c15ca86fc72b38329c00febb80c0933497941c9b4b";
    private const string InlineResolutionHooksHash = "c2414dc0ee553fdd4ab96e7bca9d62bba1f73b30ba86c0dac6390c1540725e90";
    private const string SessionHooksHash = "3329508664cfe110ffc9c8a6592d725858a70790bef6702e18aace4ddddf8558";
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
    private const string ZombiePlayHash = "d0177b7359c8ade10af5385212220c7445c69442e39b5dd83a38f86a92090e60";
    private const string VikingPlayHash = "eb57c16e78c4c7e00a6b42d688ca93f171d03458ef52691ecc147008f899067e";
    private const string MapInitHash = "7ebf709bfc179137a64d31f9352ef699431a9cc641120a6e2020b25e1ca1989f";
    private const string ParticleInitHash = "7d93b83ca79874f59fdbcc044e5f3b38409179463404e57b098da5517dcd0803";
    private const int MethodToken = 0x06000152;
    private const int TouchMethodToken = 0x0600007a;
    private const int CallOffset = 0x0015;
    private const int AssetLoadCallCount = 63;
    private const int AssetDrawCallCount = 170;
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
            VerifyBody(pe, 0x0600000d, ZombiePlayHash);
            VerifyBody(pe, 0x0600002f, VikingPlayHash);
            VerifyBody(pe, 0x06000091, MapInitHash);
            VerifyBody(pe, 0x060002a1, ParticleInitHash);
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
        if (hooksHash is not (DebugHooksHash or ReleaseHooksHash or CurrentDebugHooksHash or CurrentReleaseHooksHash or LatestReleaseHooksHash or RestartReleaseHooksHash or EdgeArtReleaseHooksHash or VersionReleaseHooksHash or CreditsScrollHooksHash or PortCreditHooksHash or PictureButtonHooksHash or InstallerFoldersHooksHash or DocumentsSavesHooksHash or CenteredPicturesHooksHash or ReducedFlashFocusHooksHash or CreatorQualityHooksHash or ResolutionHooksHash or RowNavigationHooksHash or InlineResolutionHooksHash or SessionHooksHash) ||
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
                PatchAchievementAwards(module, hooks);
                PatchOfflineLeaderboards(module);
                CenterLeaderboards(module, hooks);
                PatchLocalScoreSubmission(module, hooks);
                BrightenLockedAchievements(module);
                SpaceAchievementList(module);
                AddPortCredit(module, hooks);
                DisableTouchPadsByDefault(module);
                PatchTouchOverlays(module, hooks);
                PatchDisplay(module, hooks);
                PatchSpriteBatches(module, hooks);
                PatchReducedFlashing(module, hooks);
                PatchMenuVisuals(module, hooks);
                PatchOtherFlashing(module, hooks);
                PatchPictures(module, hooks);
                PatchHelpAndOptionsInfo(module, hooks);
                PatchAssetDraw(module, hooks);
                PatchRunRestart(module);
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
                VerifyCenteredLeaderboards(check);
                VerifyAchievementAwards(check);
                VerifyLockedAchievements(check);
                VerifyAchievementListSpacing(check);
                VerifyPortCredit(check);
                VerifyTouchPadDefault(check);
                VerifyTouchOverlays(check);
                VerifyDisplay(check);
                VerifySpriteBatches(check);
                VerifyReducedFlashing(check);
                VerifyMenuVisuals(check);
                VerifyOtherFlashing(check);
                VerifyPictures(check);
                VerifyHelpAndOptionsInfo(check);
                VerifyAssetDraw(check);
                VerifyRunRestart(check);
            }
            var outputHash = Hash(stageDll);
            var patch = new PatchRecord("gameplay-call", OriginalHash, MethodSignature,
                OriginalMethodBodyHash, $"IL_0015:call Port.Hooks.DesktopMediaSources.GetAvailableMediaSources() [hooksSha256={hooksHash}]",
                "one call target is Port.Hooks; original method control flow is preserved");
            var manifest = prior with
            {
                ToolVersion = "0.14.0",
                PreparedSha256 = outputHash,
                Plan = "pinned ABI rewrite plus guarded desktop media, input, storage, local achievements and offline leaderboards",
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
                        "ZombiesWP7.xdktools.AchievementMgr::AwardAchievement(int)", OriginalHash,
                        "route original AddAchievable through Port.Hooks.DesktopAchievements",
                        "new local awards show a notice and save immediately; earned flags on load are not announced"),
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
                        "draw on a full-resolution scene target through transformed SpriteBatch calls and route native Display page actions to Port.Hooks.DesktopDisplay",
                        "original game coordinates and Settings actions remain intact while imported art retains source detail"),
                    new PatchRecord("gameplay-call", OriginalHash,
                        "IMAK3Z0MB1EGAEM.map.MapMan::DrawClassicMapOverlay(SpriteBatch)",
                        OriginalHash,
                        "replace both Endless room-mask right-edge constants with the current logical scene width",
                        "unrevealed rooms remain dark across ultrawide scenes"),
                    new PatchRecord("gameplay-call", OriginalHash,
                        "0x06000103 ZombiesWP7.menu.MenuItem::Draw()",
                        MenuItemDrawHash,
                        "tint focused menu items through Port.Hooks.MenuVisuals",
                        "reduced flashing uses steady focus colors"),
                    new PatchRecord("gameplay-call", OriginalHash,
                        "Viking_x86.vikinggame.world.WorldTools::DrawLightRow(SpriteBatch,int,Vector2)",
                        OriginalHash,
                        "map strobe rows to steady colors through Port.Hooks.DesktopDisplay when reduced flashing is enabled",
                        "other light rows retain their original color"),
                    new PatchRecord("gameplay-call", OriginalHash,
                        "pinned random-tint and Metal background draw sites",
                        OriginalHash,
                        "route menu text, flash characters, selected gameplay tints, and Metal background beat reads through Port.Hooks.MenuVisuals",
                        "Reduce Flashing steadies these visuals; the original draw path remains when it is off"),
                    new PatchRecord("gameplay-call", OriginalHash,
                        "0x060002cc IMAK3Z0MB1EGAEM.menu.CustomMapper::Init()",
                        CustomMapperInitHash,
                        "load the configured picture folder into the creator source list",
                        "the creator uses imported thumbnails through its original source list"),
                    new PatchRecord("gameplay-call", OriginalHash,
                        "0x060001f9 IMAK3Z0MB1EGAEM.menu.HelpAndOptions::.ctor()",
                        HelpAndOptionsCtorHash,
                        "show the launcher build version through Port.Hooks.DesktopDisplay.BuildVersionInfo",
                        "the Help and Options info block contains only the current build version"),
                    new PatchRecord("gameplay-call", OriginalHash,
                        "0x06000133 IMAK3Z0MB1EGAEM.menu.Credits::.ctor()",
                        OriginalHash,
                        "insert Port made by Gabriel Favero through Port.Hooks.DesktopCredits",
                        "the original credit lines and their spacing remain in order"),
                    new PatchRecord("gameplay-il", OriginalHash,
                        "0x0600000d ZombieGame::Play(bool); 0x0600002f VikingGame::Play(); EndGameYouSure::Click(string)",
                        ZombiePlayHash + "/" + VikingPlayHash + "/" + MapInitHash + "/" + ParticleInitHash,
                        "stop both clocks and music when a run ends; select the correct clock and reset each mode's world before Play",
                        "replays and mode switches begin fresh runs without changing saved profile data"),
                    new PatchRecord("gameplay-call", OriginalHash,
                        "pinned ContentManager.Load<Texture2D> and SpriteBatch.Draw call sites",
                        OriginalHash,
                        "route phone texture paths and source rectangles through Port.Hooks.AssetDraw",
                        "phone logical geometry and Original fallback remain in effect")]
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

    private static void PatchAchievementAwards(ModuleDefinition module, ModuleDefinition hooks)
    {
        var award = module.Types.Single(type => type.FullName == "ZombiesWP7.xdktools.AchievementMgr")
            .Methods.Single(method => method.Name == "AwardAchievement" && method.Parameters.Count == 1 &&
                method.Parameters[0].ParameterType.FullName == "System.Int32");
        var calls = award.Body.Instructions.Where(instruction =>
            instruction.OpCode == OpCodes.Callvirt && instruction.Operand is MethodReference method &&
            method.FullName == "System.Void ZombiesWP7.store.PlayerProfile::AddAchievable(System.Int32)").ToArray();
        if (calls.Length != 1)
            throw new PreparationException("Original achievement profile award call differs.");
        var target = hooks.Types.Single(type => type.FullName == "Port.Hooks.DesktopAchievements")
            .Methods.Single(method => method.Name == "AddAchievable" && method.IsStatic &&
                method.Parameters.Count == 2 && method.Parameters[0].ParameterType.FullName == "System.Object" &&
                method.Parameters[1].ParameterType.FullName == "System.Int32");
        calls[0].OpCode = OpCodes.Call;
        calls[0].Operand = module.ImportReference(target);
    }

    private static void VerifyAchievementAwards(ModuleDefinition module)
    {
        var award = module.Types.Single(type => type.FullName == "ZombiesWP7.xdktools.AchievementMgr")
            .Methods.Single(method => method.Name == "AwardAchievement" && method.Parameters.Count == 1);
        if (award.Body.Instructions.Count(instruction => instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodReference method &&
            method.FullName == "System.Void Port.Hooks.DesktopAchievements::AddAchievable(System.Object,System.Int32)") != 1)
            throw new PreparationException("Achievement award hook postcondition failed.");
    }

    private static void PatchOfflineLeaderboards(ModuleDefinition module)
    {
        var main = module.Types.Single(type => type.FullName == "Viking_x86.main.MainMenu")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000081);
        var board = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.Leaderboards")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000072);
        ReplaceSigninRead(main, 0x05fe);
        ReplaceSigninRead(board, 0x0205);
        ReplaceText(board, 0x0170, "You are not yet ranked", "No saved runs yet");
        ReplaceText(board, 0x017e, "on this leaderboard.", "Play a game to add one.");
    }

    private static void PatchLocalScoreSubmission(ModuleDefinition module, ModuleDefinition hooks)
    {
        var method = module.Types.Single(type => type.FullName == "ZombiesWP7.xdktools.LeaderboardMgr")
            .Methods.Single(candidate => candidate.Name == "Write" && candidate.Parameters.Count == 2);
        var target = hooks.Types.Single(type => type.FullName == "Port.Hooks.DesktopLeaderboards")
            .Methods.Single(candidate => candidate.Name == "Record");
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        var il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldarg_1));
        il.Append(il.Create(OpCodes.Ldarg_2));
        il.Append(il.Create(OpCodes.Call, module.ImportReference(target)));
        il.Append(il.Create(OpCodes.Ret));
    }

    private static void VerifyOfflineLeaderboards(ModuleDefinition module)
    {
        var main = module.Types.Single(type => type.FullName == "Viking_x86.main.MainMenu")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000081);
        var board = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.Leaderboards")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000072);
        // The centering call inserted into Update moves later IL offsets by five bytes.
        foreach (var (method, offset) in new[] { (main, 0x05fe), (board, 0x020a) })
            if (method.Body.Instructions.Single(item => item.Offset == offset).OpCode != OpCodes.Ldc_I4_3)
                throw new PreparationException("Offline leaderboard sign-in UI postcondition failed.");
        foreach (var (offset, text) in new[] { (0x0175, "No saved runs yet"), (0x0183, "Play a game to add one.") })
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

    private static void SpaceAchievementList(ModuleDefinition module)
    {
        var achievements = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.Achievements");
        ReplaceUniqueFloat(achievements.Methods.Single(method => method.Name == "Update"), 90f, 112f);
        ReplaceUniqueFloat(achievements.Methods.Single(method => method.IsConstructor && !method.IsStatic), 1240f, 1262f);
    }

    private static void VerifyAchievementListSpacing(ModuleDefinition module)
    {
        var achievements = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.Achievements");
        if (!achievements.Methods.Single(method => method.Name == "Update").Body.Instructions.Any(item =>
                item.OpCode == OpCodes.Ldc_R4 && item.Operand is float value && value == 112f) ||
            !achievements.Methods.Single(method => method.IsConstructor && !method.IsStatic).Body.Instructions.Any(item =>
                item.OpCode == OpCodes.Ldc_R4 && item.Operand is float value && value == 1262f))
            throw new PreparationException("Achievement list spacing postcondition failed.");
    }

    private static void AddPortCredit(ModuleDefinition module, ModuleDefinition hooks)
    {
        var constructor = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.Credits")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000133);
        var instructions = constructor.Body.Instructions;
        var size = instructions.Single(item => item.Offset == 0x0012);
        var originalCredit = instructions.Single(item => item.Offset == 0x0043);
        var nextCredit = instructions.Single(item => item.Offset == 0x005d);
        var store = instructions.Single(item => item.Offset == 0x065c);
        if (size.OpCode != OpCodes.Ldc_I4_S || size.Operand is not sbyte count || count != 115 ||
            originalCredit.OpCode != OpCodes.Ldstr || originalCredit.Operand is not "and Ska Studios!" ||
            nextCredit.OpCode != OpCodes.Ldstr || nextCredit.Operand is not "Ska Studios is" ||
            store.OpCode != OpCodes.Stfld || store.Operand is not FieldReference field ||
            field.FullName != "System.Text.StringBuilder[] IMAK3Z0MB1EGAEM.menu.MenuLevel::infos" ||
            store.Previous?.OpCode != OpCodes.Ldloc_0)
            throw new PreparationException("Credits insertion anchor differs from its guard.");
        var hook = hooks.Types.Single(type => type.FullName == "Port.Hooks.DesktopCredits")
            .Methods.Single(method => method.Name == "AddPortCredit" && method.IsStatic &&
                method.Parameters.Count == 1 &&
                method.Parameters[0].ParameterType.FullName == "System.Text.StringBuilder[]" &&
                method.ReturnType.FullName == "System.Text.StringBuilder[]");
        constructor.Body.GetILProcessor().InsertBefore(store,
            constructor.Body.GetILProcessor().Create(OpCodes.Call, module.ImportReference(hook)));
    }

    private static void VerifyPortCredit(ModuleDefinition module)
    {
        var constructor = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.Credits")
            .Methods.Single(method => method.Name == ".ctor" && method.Parameters.Count == 0);
        var store = constructor.Body.Instructions.Single(item => item.OpCode == OpCodes.Stfld &&
            item.Operand is FieldReference field && field.Name == "infos");
        if (store.Previous?.OpCode != OpCodes.Call ||
            store.Previous.Operand is not MethodReference hook ||
            hook.DeclaringType.FullName != "Port.Hooks.DesktopCredits" ||
            hook.Name != "AddPortCredit" ||
            store.Previous.Previous?.OpCode != OpCodes.Ldloc_0)
            throw new PreparationException("Credits insertion postcondition failed.");
    }

    private static void CenterLeaderboards(ModuleDefinition module, ModuleDefinition hooks)
    {
        var board = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.Leaderboards");
        var display = hooks.Types.Single(type => type.FullName == "Port.Hooks.DesktopDisplay");
        var hud = module.ImportReference(display.Methods.Single(method => method.Name == "CenterLeaderboardHudX"));
        var row = module.ImportReference(display.Methods.Single(method => method.Name == "CenterLeaderboardRowX"));
        InsertAfterUniqueFloat(board.Methods.Single(method => method.Name == "InitHUDItems"), 30f, hud);
        InsertAfterUniqueFloat(board.Methods.Single(method => method.Name == "InitHUDItems"), 450f, hud);
        InsertAfterUniqueFloat(board.Methods.Single(method => method.Name == "Update"), 80f, row);
    }

    private static void VerifyCenteredLeaderboards(ModuleDefinition module)
    {
        var board = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.Leaderboards");
        foreach (var (name, hook, count) in new[]
        {
            ("InitHUDItems", "CenterLeaderboardHudX", 2),
            ("Update", "CenterLeaderboardRowX", 1)
        })
        {
            var method = board.Methods.Single(candidate => candidate.Name == name);
            if (method.Body.Instructions.Count(item => item.Operand is MethodReference target &&
                    target.DeclaringType.FullName == "Port.Hooks.DesktopDisplay" && target.Name == hook) != count)
                throw new PreparationException($"Leaderboard {name} centering postcondition failed.");
        }
    }

    private static void InsertAfterUniqueFloat(Mono.Cecil.MethodDefinition method, float value, MethodReference target)
    {
        var matches = method.Body.Instructions.Where(item =>
            item.OpCode == OpCodes.Ldc_R4 && item.Operand is float found && found == value).ToArray();
        if (matches.Length != 1 || matches[0].Next?.OpCode != OpCodes.Ldc_R4)
            throw new PreparationException($"Leaderboard coordinate {value} differs in {method.FullName}.");
        method.Body.GetILProcessor().InsertAfter(matches[0],
            method.Body.GetILProcessor().Create(OpCodes.Call, target));
    }

    private static void ReplaceUniqueFloat(Mono.Cecil.MethodDefinition method, float oldValue, float newValue)
    {
        var matches = method.Body.Instructions.Where(item =>
            item.OpCode == OpCodes.Ldc_R4 && item.Operand is float value && value == oldValue).ToArray();
        if (matches.Length != 1)
            throw new PreparationException($"Expected one {oldValue} constant in {method.FullName}.");
        matches[0].Operand = newValue;
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

    private static void PatchTouchOverlays(ModuleDefinition module, ModuleDefinition hooks)
    {
        var touch = module.Types.Single(type => type.FullName == "ZombiesWP7.TouchMgr");
        var draw = touch.Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000079);
        var pad = touch.Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000078);
        var inputHook = hooks.Types.Single(type => type.FullName == "Port.Hooks.DesktopInput");
        var displayHook = hooks.Types.Single(type => type.FullName == "Port.Hooks.DesktopDisplay");
        var showPause = module.ImportReference(inputHook.Methods.Single(method => method.Name == "ShouldShowTouchPause"));
        var scalePad = module.ImportReference(displayHook.Methods.Single(method => method.Name == "ScaleTouchpad"));
        var instructions = draw.Body.Instructions;
        Mono.Cecil.Cil.Instruction At(int offset) => instructions.Single(item => item.Offset == offset);
        if (At(0x000a).Operand is not FieldReference showPads || showPads.Name != "showTouchPads" ||
            At(0x000f).OpCode != OpCodes.Brtrue_S || At(0x0011).OpCode != OpCodes.Ret ||
            At(0x0021).OpCode != OpCodes.Ldarg_0 || At(0x0060).Operand is not MethodReference pauseText ||
            pauseText.Name != "DrawString" || At(0x0065).OpCode != OpCodes.Ldsfld ||
            At(0x0065).Operand is not FieldReference player || player.Name != "player" ||
            At(0x006a).Operand is not FieldReference settings || settings.Name != "settings")
            throw new PreparationException("Touch overlay draw anchors differ from their guard.");

        // Keep the original touchpad preference, but let the pause glyph draw
        // independently whenever direct touch was the last input source.
        At(0x0011).OpCode = OpCodes.Nop;
        var il = draw.Body.GetILProcessor();
        var padGuard = At(0x0065);
        var firstGuard = il.Create(OpCodes.Ldsfld, player);
        il.InsertBefore(At(0x0021), il.Create(OpCodes.Call, showPause));
        il.InsertBefore(At(0x0021), il.Create(OpCodes.Brfalse, firstGuard));
        il.InsertBefore(padGuard, firstGuard);
        il.InsertBefore(padGuard, il.Create(OpCodes.Ldfld, settings));
        il.InsertBefore(padGuard, il.Create(OpCodes.Ldfld, showPads));
        il.InsertBefore(padGuard, il.Create(OpCodes.Brtrue, padGuard));
        il.InsertBefore(padGuard, il.Create(OpCodes.Ret));

        foreach (var (method, offset, value) in new[]
        {
            (pad, 0x0088, 2f), (pad, 0x00ac, 5f), (draw, 0x0116, 0.75f)
        })
        {
            var constant = method.Body.Instructions.Single(item => item.Offset == offset);
            if (constant.OpCode != OpCodes.Ldc_R4 || constant.Operand is not float found || found != value)
                throw new PreparationException($"Touchpad scale anchor differs at IL_{offset:x4}.");
            method.Body.GetILProcessor().InsertAfter(constant,
                method.Body.GetILProcessor().Create(OpCodes.Call, scalePad));
        }
    }

    private static void VerifyTouchOverlays(ModuleDefinition module)
    {
        var touch = module.Types.Single(type => type.FullName == "ZombiesWP7.TouchMgr");
        var draw = touch.Methods.Single(method => method.Name == "DrawThumbpads");
        var pad = touch.Methods.Single(method => method.Name == "DrawThumbpad");
        if (draw.Body.Instructions.Single(item => item.Offset == 0x0011).OpCode != OpCodes.Nop ||
            draw.Body.Instructions.Count(item => item.Operand is MethodReference target &&
                target.FullName == "System.Boolean Port.Hooks.DesktopInput::ShouldShowTouchPause()") != 1 ||
            draw.Body.Instructions.Count(item => item.Operand is MethodReference target &&
                target.Name == "ScaleTouchpad" && target.DeclaringType.FullName == "Port.Hooks.DesktopDisplay") != 1 ||
            pad.Body.Instructions.Count(item => item.Operand is MethodReference target &&
                target.Name == "ScaleTouchpad" && target.DeclaringType.FullName == "Port.Hooks.DesktopDisplay") != 2)
            throw new PreparationException("Touch overlay postcondition failed.");
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

        var overlay = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.map.MapMan")
            .Methods.Single(method => method.Name == "DrawClassicMapOverlay" && method.Parameters.Count == 1);
        var rightEdge = overlay.Body.Instructions.Where(item =>
            item.OpCode == OpCodes.Ldc_R4 && item.Operand is float value && value == 800f).ToArray();
        if (rightEdge.Length != 2 ||
            rightEdge[0].Next is not { } comparison ||
            comparison.OpCode != OpCodes.Ble_Un_S && comparison.OpCode != OpCodes.Ble_Un ||
            rightEdge[1].Next?.OpCode != OpCodes.Stfld ||
            rightEdge[1].Next?.Operand is not FieldReference field ||
            field.FullName != "System.Single Microsoft.Xna.Framework.Vector2::X")
            throw new PreparationException("Original Endless mask right edge differs.");
        foreach (var edge in rightEdge)
        {
            edge.OpCode = OpCodes.Call;
            edge.Operand = Target("EndlessMaskRightEdge", 0);
        }

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

        var itemClick = module.Types.Single(type => type.FullName == "ZombiesWP7.menu.MenuItem")
            .Methods.Single(method => method.Name == "TryClick" && method.Parameters.Count == 2);
        if (itemClick.ReturnType.FullName != "System.Boolean" ||
            itemClick.Parameters[0].ParameterType.FullName != "Microsoft.Xna.Framework.Vector2" ||
            itemClick.Parameters[1].ParameterType.FullName != "IMAK3Z0MB1EGAEM.menu.MenuLevel")
            throw new PreparationException("Original MenuItem.TryClick signature differs.");
        var itemClickFirst = itemClick.Body.Instructions.First();
        il = itemClick.Body.GetILProcessor();
        il.InsertBefore(itemClickFirst, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(itemClickFirst, il.Create(OpCodes.Call, Target("IsStaticPlayerNameLabel", 1)));
        il.InsertBefore(itemClickFirst, il.Create(OpCodes.Brfalse, itemClickFirst));
        il.InsertBefore(itemClickFirst, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(itemClickFirst, il.Create(OpCodes.Ret));
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
        var overlay = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.map.MapMan")
            .Methods.Single(method => method.Name == "DrawClassicMapOverlay" && method.Parameters.Count == 1);
        if (overlay.Body.Instructions.Count(item => item.Operand is MethodReference method &&
                method.DeclaringType.FullName == "Port.Hooks.DesktopDisplay" &&
                method.Name == "EndlessMaskRightEdge") != 2 ||
            overlay.Body.Instructions.Any(item => item.OpCode == OpCodes.Ldc_R4 &&
                item.Operand is float value && value == 800f))
            throw new PreparationException("Endless mask right edge postcondition failed.");
        if (click.Body.Instructions.Count(item => item.Operand is MethodReference method &&
                method.DeclaringType.FullName == "Port.Hooks.DesktopDisplay" && method.Name == "HandleSettingsClick") != 1)
            throw new PreparationException("Settings display entry postcondition failed.");
        if (cancel.Body.Instructions.Count(item => item.Operand is MethodReference method &&
                method.DeclaringType.FullName == "Port.Hooks.DesktopDisplay" && method.Name == "HandleSettingsCancel") != 1)
            throw new PreparationException("Settings display cancel postcondition failed.");
        var itemClick = module.Types.Single(type => type.FullName == "ZombiesWP7.menu.MenuItem")
            .Methods.Single(method => method.Name == "TryClick" && method.Parameters.Count == 2);
        if (itemClick.Body.Instructions.Count(item => item.Operand is MethodReference method &&
                method.DeclaringType.FullName == "Port.Hooks.DesktopDisplay" && method.Name == "IsStaticPlayerNameLabel") != 1)
            throw new PreparationException("Static player name label postcondition failed.");
    }

    private static void PatchSpriteBatches(ModuleDefinition module, ModuleDefinition hooks)
    {
        var display = hooks.Types.Single(type => type.FullName == "Port.Hooks.DesktopDisplay");
        var patched = 0;
        foreach (var method in AllTypes(module.Types).SelectMany(type => type.Methods).Where(method => method.HasBody))
        foreach (var instruction in method.Body.Instructions)
        {
            if (instruction.OpCode != OpCodes.Callvirt || instruction.Operand is not MethodReference call ||
                call.DeclaringType.FullName != "Microsoft.Xna.Framework.Graphics.SpriteBatch" ||
                call.Name != "Begin") continue;
            var target = display.Methods.SingleOrDefault(candidate => candidate.Name == "Begin" &&
                candidate.IsStatic && candidate.Parameters.Count == call.Parameters.Count + 1 &&
                candidate.Parameters[0].ParameterType.FullName == call.DeclaringType.FullName &&
                candidate.Parameters.Skip(1).Select(parameter => parameter.ParameterType.FullName)
                    .SequenceEqual(call.Parameters.Select(parameter => parameter.ParameterType.FullName)));
            if (target is null)
                throw new PreparationException($"Unsupported SpriteBatch.Begin signature: {call.FullName}");
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = module.ImportReference(target);
            patched++;
        }
        if (patched == 0) throw new PreparationException("No game SpriteBatch.Begin calls were patched.");
    }

    private static void VerifySpriteBatches(ModuleDefinition module)
    {
        var calls = AllTypes(module.Types).SelectMany(type => type.Methods)
            .Where(method => method.HasBody).SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand).OfType<MethodReference>().ToArray();
        if (calls.Any(call => call.DeclaringType.FullName == "Microsoft.Xna.Framework.Graphics.SpriteBatch" &&
                call.Name == "Begin") ||
            !calls.Any(call => call.DeclaringType.FullName == "Port.Hooks.DesktopDisplay" && call.Name == "Begin"))
            throw new PreparationException("SpriteBatch scene transform postcondition failed.");
    }

    private static void PatchMenuVisuals(ModuleDefinition module, ModuleDefinition hooks)
    {
        var visuals = hooks.Types.Single(type => type.FullName == "Port.Hooks.MenuVisuals");
        MethodReference Target(string name) => module.ImportReference(visuals.Methods.Single(method =>
            method.Name == name && method.IsStatic));
        var itemDraw = module.Types.Single(type => type.FullName == "ZombiesWP7.menu.MenuItem")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000103);
        foreach (var offset in new[] { 0x00c7, 0x0173 })
        {
            var color = itemDraw.Body.Instructions.SingleOrDefault(item => item.Offset == offset);
            if (color?.OpCode != OpCodes.Newobj || color.Operand is not MethodReference constructor ||
                constructor.FullName != "System.Void Microsoft.Xna.Framework.Color::.ctor(System.Single,System.Single,System.Single,System.Single)" ||
                color.Next?.OpCode != OpCodes.Ldc_I4_2)
                throw new PreparationException($"Menu item focus color anchor differs at IL_{offset:x4}.");
            var il = itemDraw.Body.GetILProcessor();
            il.InsertAfter(color, il.Create(OpCodes.Ldarg_0));
            il.InsertAfter(color.Next, il.Create(OpCodes.Call, Target("Highlight")));
        }
    }

    // Pin every changed color constructor to the supported phone assembly. These
    // calls generate a fresh tint each frame or alternate a spawn tint rapidly.
    private static readonly (string Type, string Method, int[] Offsets, string Hook)[] FlashColors =
    [
        ("IMAK3Z0MB1EGAEM.menu.Menu", "Draw", [0x00e7, 0x0157, 0x01c2, 0x0232, 0x029d], "Steady"),
        ("IMAK3Z0MB1EGAEM.particles.particles.Score", "Draw", [0x0046], "Steady"),
        ("Viking_x86.particles.pickups.Score", "Draw", [0x0040], "Steady"),
        ("IMAK3Z0MB1EGAEM.particles.particles.Dying", "Draw", [0x003e], "Steady"),
        ("Viking_x86.vikinggame.world.WorldTools", "DrawText", [0x003e], "Steady"),
        ("Viking_x86.vikinggame.world.SpaceTools", "DrawTechnoLines", [0x016a], "Steady"),
        ("Viking_x86.vikinggame.world.EmoTools", "DrawEmoBlast", [0x020d], "Steady"),
        ("IMAK3Z0MB1EGAEM.character.Monster", "Draw", [0x0395, 0x0463], "Steady"),
        ("IMAK3Z0MB1EGAEM.character.Monster", "Draw", [0x05a1], "SteadySpawn")
    ];

    private static void PatchOtherFlashing(ModuleDefinition module, ModuleDefinition hooks)
    {
        var visuals = hooks.Types.Single(type => type.FullName == "Port.Hooks.MenuVisuals");
        foreach (var (typeName, name, offsets, hookName) in FlashColors)
        {
            var method = module.Types.Single(type => type.FullName == typeName)
                .Methods.Single(candidate => candidate.Name == name && candidate.Body.Instructions.Any(i => i.Offset == offsets[0]));
            var target = module.ImportReference(visuals.Methods.Single(candidate => candidate.Name == hookName));
            var il = method.Body.GetILProcessor();
            foreach (var offset in offsets)
            {
                var instruction = method.Body.Instructions.SingleOrDefault(item => item.Offset == offset);
                if (instruction?.OpCode != OpCodes.Newobj || instruction.Operand is not MethodReference constructor ||
                    constructor.FullName != "System.Void Microsoft.Xna.Framework.Color::.ctor(System.Single,System.Single,System.Single,System.Single)")
                    throw new PreparationException($"Flashing color anchor differs at {typeName}::{name} IL_{offset:x4}.");
                il.InsertAfter(instruction, il.Create(OpCodes.Call, target));
            }
        }
        var mapText = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.map.MapMan")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000097);
        var mapColor = mapText.Body.Instructions.SingleOrDefault(item => item.Offset == 0x004d);
        if (mapColor?.OpCode != OpCodes.Ldloc_0 || mapColor.Next?.OpCode != OpCodes.Ldc_I4_2)
            throw new PreparationException("Map light text color anchor differs.");
        mapText.Body.GetILProcessor().InsertAfter(mapColor, mapText.Body.GetILProcessor().Create(OpCodes.Call,
            module.ImportReference(visuals.Methods.Single(method => method.Name == "Steady"))));
        var mapDraw = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.map.MapMan")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000093);
        var steadyBeat = module.ImportReference(visuals.Methods.Single(method => method.Name == "SteadyMetalBeat"));
        foreach (var offset in new[] { 0x04e3, 0x04f1, 0x0502, 0x0513, 0x0526, 0x05f1 })
        {
            var fieldLoad = mapDraw.Body.Instructions.SingleOrDefault(item => item.Offset == offset);
            if (fieldLoad?.OpCode != OpCodes.Ldfld || fieldLoad.Operand is not FieldReference field ||
                field.DeclaringType.FullName != "IMAK3Z0MB1EGAEM.director.BaseTimeMgr" ||
                field.Name != (offset is 0x0526 or 0x05f1 ? "octobeat" : "beat"))
                throw new PreparationException($"Metal background beat anchor differs at IL_{offset:x4}.");
            mapDraw.Body.GetILProcessor().InsertAfter(fieldLoad,
                mapDraw.Body.GetILProcessor().Create(OpCodes.Call, steadyBeat));
        }
        var text = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.Text")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x060002f8);
        if (text.Parameters.Count != 7 || text.Parameters[6].Name != "flashChar")
            throw new PreparationException("Flashing text character signature differs.");
        var first = text.Body.Instructions.First();
        var processor = text.Body.GetILProcessor();
        var flashChar = text.Parameters[6];
        processor.InsertBefore(first, processor.Create(OpCodes.Ldarg_S, flashChar));
        processor.InsertBefore(first, processor.Create(OpCodes.Call,
            module.ImportReference(visuals.Methods.Single(method => method.Name == "FlashCharacter"))));
        processor.InsertBefore(first, processor.Create(OpCodes.Starg_S, flashChar));
    }

    private static void VerifyOtherFlashing(ModuleDefinition module)
    {
        var mapText = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.map.MapMan")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000097);
        if (mapText.Body.Instructions.Count(item => item.Operand is MethodReference target &&
                target.DeclaringType.FullName == "Port.Hooks.MenuVisuals" && target.Name == "Steady") != 1)
            throw new PreparationException("Reduced flashing map text hook failed.");
        var mapDraw = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.map.MapMan")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x06000093);
        if (mapDraw.Body.Instructions.Count(item => item.Operand is MethodReference target &&
                target.DeclaringType.FullName == "Port.Hooks.MenuVisuals" && target.Name == "SteadyMetalBeat") != 6)
            throw new PreparationException("Reduced flashing Metal background hook failed.");
        foreach (var (typeName, name, offsets, hookName) in FlashColors)
        {
            var methods = module.Types.Single(type => type.FullName == typeName).Methods
                .Where(method => method.Name == name && method.HasBody).ToArray();
            if (methods.Sum(method => method.Body.Instructions.Count(item => item.Operand is MethodReference target &&
                    target.DeclaringType.FullName == "Port.Hooks.MenuVisuals" && target.Name == hookName)) != offsets.Length)
                throw new PreparationException($"Reduced flashing hook failed for {typeName}::{name}.");
        }
        var text = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.Text")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x060002f8);
        if (text.Body.Instructions.Count(item => item.Operand is MethodReference target &&
                target.DeclaringType.FullName == "Port.Hooks.MenuVisuals" && target.Name == "FlashCharacter") != 1)
            throw new PreparationException("Reduced flashing text character hook failed.");
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
            method.Name == name && method.IsStatic));
        var mapper = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.CustomMapper")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x060002cc);
        var first = mapper.Body.Instructions.First();
        if (first.OpCode != OpCodes.Ldsfld || first.Operand is not FieldReference gameCustom ||
            gameCustom.FullName != "ZombiesWP7.custom.CustomMgr ZombiesWP7.Game1::custom")
            throw new PreparationException("CustomMapper.Init picture picker anchor differs.");
        var il = mapper.Body.GetILProcessor();
        il.InsertBefore(first, il.Create(OpCodes.Ldsfld, gameCustom));
        il.InsertBefore(first, il.Create(OpCodes.Call, Target("LoadRememberedFolder")));

        var mapperType = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.CustomMapper");
        var draw = mapperType.Methods.Single(method => method.Name == "Draw");
        var drawIl = draw.Body.GetILProcessor();
        var drawFirst = draw.Body.Instructions.First();
        drawIl.InsertBefore(drawFirst, drawIl.Create(OpCodes.Ldarg_0));
        drawIl.InsertBefore(drawFirst, drawIl.Create(OpCodes.Call, Target("LayoutMapper")));

        var click = mapperType.Methods.Single(method => method.Name == "CustomClick");
        var clickIl = click.Body.GetILProcessor();
        var clickFirst = click.Body.Instructions.First();
        clickIl.InsertBefore(clickFirst, clickIl.Create(OpCodes.Ldarg_0));
        clickIl.InsertBefore(clickFirst, clickIl.Create(OpCodes.Ldarg_1));
        clickIl.InsertBefore(clickFirst, clickIl.Create(OpCodes.Call, Target("ClickMapperPicture")));
        clickIl.InsertBefore(clickFirst, clickIl.Create(OpCodes.Brfalse, clickFirst));
        clickIl.InsertBefore(clickFirst, clickIl.Create(OpCodes.Ret));

        var image = module.Types.Single(type => type.FullName == "ZombiesWP7.custom.CustomImg");
        var process = image.Methods.Single(method => method.Name == "Process");
        if (process.Parameters.Count != 2 || process.Parameters[1].ParameterType.FullName !=
            "Microsoft.Xna.Framework.Graphics.GraphicsDevice")
            throw new PreparationException("Custom image processing signature differs.");
        process.Body = new Mono.Cecil.Cil.MethodBody(process);
        var processIl = process.Body.GetILProcessor();
        processIl.Append(processIl.Create(OpCodes.Ldarg_0));
        processIl.Append(processIl.Create(OpCodes.Ldarg_1));
        processIl.Append(processIl.Create(OpCodes.Ldarg_2));
        processIl.Append(processIl.Create(OpCodes.Call, Target("ProcessCustomImage")));
        processIl.Append(processIl.Create(OpCodes.Ret));

    }

    private static void VerifyPictures(ModuleDefinition module)
    {
        var mapper = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.CustomMapper");
        var init = mapper.Methods.Single(candidate => candidate.Name == "Init");
        if (init.Body.Instructions.Count(item => item.Operand is MethodReference target &&
            target.DeclaringType.FullName == "Port.Hooks.DesktopPictures" &&
            target.Name == "LoadRememberedFolder") != 1)
            throw new PreparationException("Picture folder load hook postcondition failed.");
        foreach (var (methodName, hookName) in new[] { ("Draw", "LayoutMapper"),
                     ("CustomClick", "ClickMapperPicture") })
            if (mapper.Methods.Single(candidate => candidate.Name == methodName).Body.Instructions
                .Count(item => item.Operand is MethodReference target &&
                    target.DeclaringType.FullName == "Port.Hooks.DesktopPictures" &&
                    target.Name == hookName) != 1)
                throw new PreparationException($"Picture mapper {hookName} hook postcondition failed.");
        var image = module.Types.Single(type => type.FullName == "ZombiesWP7.custom.CustomImg")
            .Methods.Single(method => method.Name == "Process");
        if (image.Body.Instructions.Count(item => item.Operand is MethodReference target &&
            target.DeclaringType.FullName == "Port.Hooks.DesktopPictures" &&
            target.Name == "ProcessCustomImage") != 1)
            throw new PreparationException("High resolution picture processing hook postcondition failed.");
    }

    private static void PatchHelpAndOptionsInfo(ModuleDefinition module, ModuleDefinition hooks)
    {
        var constructor = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.HelpAndOptions")
            .Methods.Single(method => method.MetadataToken.ToInt32() == 0x060001f9);
        var instructions = constructor.Body.Instructions;
        var loadThis = instructions.Single(item => item.Offset == 0x0082);
        var size = instructions.Single(item => item.Offset == 0x0083);
        var array = instructions.Single(item => item.Offset == 0x0084);
        var store = instructions.Single(item => item.Offset == 0x00a5);
        if (loadThis.OpCode != OpCodes.Ldarg_0 || size.OpCode != OpCodes.Ldc_I4_2 ||
            array.OpCode != OpCodes.Newarr ||
            array.Operand is not Mono.Cecil.TypeReference arrayType || arrayType.FullName != "System.Text.StringBuilder" ||
            store.OpCode != OpCodes.Stfld || store.Operand is not FieldReference field ||
            field.FullName != "System.Text.StringBuilder[] IMAK3Z0MB1EGAEM.menu.MenuLevel::infos" ||
            !instructions.Any(item => item.Offset == 0x008c && item.Operand is string version && version == "Version 1.0") ||
            !instructions.Any(item => item.Offset == 0x0099 && item.Operand is string contact &&
                contact == "Contact us: support@ska-studios.com"))
            throw new PreparationException("Help and Options info block differs from its guard.");
        var hook = hooks.Types.Single(type => type.FullName == "Port.Hooks.DesktopDisplay")
            .Methods.Single(method => method.Name == "BuildVersionInfo" && method.IsStatic &&
                method.Parameters.Count == 0 && method.ReturnType.FullName == "System.Text.StringBuilder[]");
        size.OpCode = OpCodes.Call;
        size.Operand = module.ImportReference(hook);
        var il = constructor.Body.GetILProcessor();
        for (var instruction = array; instruction != store;)
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
        var instructions = constructor.Body.Instructions;
        if (instructions[^4].OpCode != OpCodes.Ldarg_0 ||
            instructions[^3].OpCode != OpCodes.Call ||
            instructions[^3].Operand is not MethodReference hook ||
            hook.DeclaringType.FullName != "Port.Hooks.DesktopDisplay" ||
            hook.Name != "BuildVersionInfo" ||
            instructions[^2].OpCode != OpCodes.Stfld ||
            instructions[^2].Operand is not FieldReference field || field.Name != "infos" ||
            instructions[^1].OpCode != OpCodes.Ret ||
            instructions.Any(item => item.Operand is string value &&
                (value == "Version 1.0" || value == "Contact us: support@ska-studios.com")))
            throw new PreparationException("Help and Options build version hook postcondition failed.");
    }

    private static IEnumerable<Mono.Cecil.TypeDefinition> AllTypes(IEnumerable<Mono.Cecil.TypeDefinition> roots)
    {
        foreach (var type in roots)
        {
            yield return type;
            foreach (var nested in AllTypes(type.NestedTypes)) yield return nested;
        }
    }

    private static void PatchAssetDraw(ModuleDefinition module, ModuleDefinition hooks)
    {
        var asset = hooks.Types.Single(type => type.FullName == "Port.Hooks.AssetDraw");
        var load = module.ImportReference(asset.Methods.Single(method => method.Name == "LoadTexture"));
        var wrappers = asset.Methods.Where(method => method.Name == "Draw" && method.IsStatic).ToArray();
        var loads = 0;
        var draws = 0;
        foreach (var method in AllTypes(module.Types).SelectMany(type => type.Methods).Where(method => method.HasBody))
        {
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is not MethodReference target ||
                    (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt)) continue;
                if (target.DeclaringType.FullName == "Microsoft.Xna.Framework.Content.ContentManager" &&
                    target.Name == "Load" && target is GenericInstanceMethod generic &&
                    generic.GenericArguments.Count == 1 &&
                    generic.GenericArguments[0].FullName == "Microsoft.Xna.Framework.Graphics.Texture2D" &&
                    target.Parameters.Count == 1 && target.Parameters[0].ParameterType.FullName == "System.String")
                {
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = load;
                    loads++;
                }
                else if (target.DeclaringType.FullName == "Microsoft.Xna.Framework.Graphics.SpriteBatch" &&
                    target.Name == "Draw")
                {
                    var wrapper = wrappers.SingleOrDefault(candidate =>
                        candidate.Parameters.Count == target.Parameters.Count + 1 &&
                        candidate.Parameters[0].ParameterType.FullName == target.DeclaringType.FullName &&
                        candidate.Parameters.Skip(1).Select(p => p.ParameterType.FullName)
                            .SequenceEqual(target.Parameters.Select(p => p.ParameterType.FullName)));
                    if (wrapper is null)
                        throw new PreparationException($"No art draw wrapper for {target.FullName} in {method.FullName}.");
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = module.ImportReference(wrapper);
                    draws++;
                }
            }
        }
        if (loads != AssetLoadCallCount || draws != AssetDrawCallCount)
            throw new PreparationException($"Phone art call coverage changed: loads={loads} draws={draws}.");
        Console.WriteLine($"ART_PATCH loads={loads} draws={draws}");
    }

    private static void VerifyAssetDraw(ModuleDefinition module)
    {
        var methods = AllTypes(module.Types).SelectMany(type => type.Methods).Where(method => method.HasBody).ToArray();
        var calls = methods.SelectMany(method => method.Body.Instructions)
            .Where(item => item.Operand is MethodReference).Select(item => (MethodReference)item.Operand).ToArray();
        var loads = calls.Count(method => method.DeclaringType.FullName == "Port.Hooks.AssetDraw" && method.Name == "LoadTexture");
        var draws = calls.Count(method => method.DeclaringType.FullName == "Port.Hooks.AssetDraw" && method.Name == "Draw");
        if (loads != AssetLoadCallCount || draws != AssetDrawCallCount || calls.Any(method =>
            method.DeclaringType.FullName == "Microsoft.Xna.Framework.Graphics.SpriteBatch" && method.Name == "Draw") ||
            calls.Any(method => method.DeclaringType.FullName == "Microsoft.Xna.Framework.Content.ContentManager" &&
                method.Name == "Load" && method is GenericInstanceMethod generic &&
                generic.GenericArguments.Count == 1 &&
                generic.GenericArguments[0].FullName == "Microsoft.Xna.Framework.Graphics.Texture2D"))
            throw new PreparationException($"Art draw hook postcondition failed: loads={loads} draws={draws}.");
    }

    private static void PatchRunRestart(ModuleDefinition module)
    {
        var zombie = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.ZombieGame");
        var viking = module.Types.Single(type => type.FullName == "Viking_x86.VikingGame");
        var timers = module.Types.Single(type => type.FullName == "Viking_x86.director.TimeMgr");
        var map = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.map.MapMan");
        var chars = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.character.CharMan");
        var particles = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.particles.ParticleMan");
        var endless = module.Types.Single(type => type.FullName == "Viking_x86.zomb1es.endless.EndlessUpdate");
        var zombieTimer = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.director.ZombieTimeMgr");
        var vikingTimer = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.director.VikingTimeMgr");
        var music = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.audio.Music");
        var endGame = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.EndGameYouSure");
        var baseTimer = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.director.BaseTimeMgr");
        var zombiePlay = zombie.Methods.Single(method => method.Name == "Play" && method.Parameters.Count == 1);
        var vikingPlay = viking.Methods.Single(method => method.Name == "Play" && method.Parameters.Count == 0);
        var endClick = endGame.Methods.Single(method => method.Name == "Click" && method.Parameters.Count == 1);
        if (zombiePlay.MetadataToken.ToInt32() != 0x0600000d ||
            vikingPlay.MetadataToken.ToInt32() != 0x0600002f ||
            zombiePlay.Body.Instructions[0].OpCode != OpCodes.Ldc_I4_0 ||
            vikingPlay.Body.Instructions[0].OpCode != OpCodes.Ldarg_0 ||
            endClick.Body.Instructions[0].OpCode != OpCodes.Ldarg_1)
            throw new PreparationException("Run restart Play anchors differ from the supported game.");

        static void Prepend(Mono.Cecil.MethodDefinition method, params Instruction[] instructions)
        {
            var il = method.Body.GetILProcessor();
            var first = method.Body.Instructions[0];
            foreach (var instruction in instructions) il.InsertBefore(first, instruction);
        }
        static Mono.Cecil.MethodDefinition Ctor(Mono.Cecil.TypeDefinition type) =>
            type.Methods.Single(method => method.IsConstructor && !method.IsStatic && method.Parameters.Count == 0);
        static Mono.Cecil.FieldDefinition Field(Mono.Cecil.TypeDefinition type, string name) =>
            type.Fields.Single(field => field.Name == name);
        static Mono.Cecil.MethodDefinition Method(Mono.Cecil.TypeDefinition type, string name) =>
            type.Methods.Single(method => method.Name == name && method.Parameters.Count == 0);

        var particleInit = Method(particles, "Init");
        var particleIl = particleInit.Body.GetILProcessor();
        var particleReturn = particleInit.Body.Instructions.Last();
        particleIl.InsertBefore(particleReturn, Instruction.Create(OpCodes.Ldc_I4_0));
        particleIl.InsertBefore(particleReturn, Instruction.Create(OpCodes.Stsfld, Field(particles, "addIdx")));
        particleIl.InsertBefore(particleReturn, Instruction.Create(OpCodes.Ldc_I4_0));
        particleIl.InsertBefore(particleReturn, Instruction.Create(OpCodes.Stsfld, Field(particles, "count")));

        var mapInit = Method(map, "Init");
        var mapIl = mapInit.Body.GetILProcessor();
        var mapReturn = mapInit.Body.Instructions.Last();
        foreach (var name in new[] { "frame", "brite", "goalBrite" })
        {
            mapIl.InsertBefore(mapReturn, Instruction.Create(OpCodes.Ldc_R4, 0f));
            mapIl.InsertBefore(mapReturn, Instruction.Create(OpCodes.Stsfld, Field(map, name)));
        }
        var vectorCtor = (MethodReference)zombie.Methods.Single(method => method.Name == "ThreadedLoadEndless")
            .Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Newobj &&
                instruction.Operand is MethodReference target &&
                target.DeclaringType.FullName == "Microsoft.Xna.Framework.Vector2" &&
                target.Parameters.Count == 2).Operand;

        // The original Play only hides actors and restarts music. Its static timeline,
        // particle pool, map lighting and Endless rooms still describe the previous run.
        Prepend(zombiePlay,
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stsfld, Field(timers, "time")),
            Instruction.Create(OpCodes.Ldsfld, Field(timers, "timeMgr")),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Newobj, Ctor(zombieTimer)),
            Instruction.Create(OpCodes.Stelem_Ref),
            Instruction.Create(OpCodes.Call, Method(chars, "Init")),
            Instruction.Create(OpCodes.Call, particleInit),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stsfld, Field(chars, "monsterCount")),
            Instruction.Create(OpCodes.Call, mapInit),
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Brfalse, zombiePlay.Body.Instructions[0]),
            Instruction.Create(OpCodes.Ldc_R4, 3686.4001f),
            Instruction.Create(OpCodes.Ldc_R4, 2764.8f),
            Instruction.Create(OpCodes.Newobj, vectorCtor),
            Instruction.Create(OpCodes.Stsfld, Field(map, "mapSize")),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldfld, Field(zombie, "Content")),
            Instruction.Create(OpCodes.Call, map.Methods.Single(method => method.Name == "ReadMap")),
            Instruction.Create(OpCodes.Newobj, Ctor(endless)),
            Instruction.Create(OpCodes.Stsfld, Field(zombie, "endlessUpdate")));

        // Viking resets its world and characters already, but kept its old clock and
        // spawner. Recreate those before the original reset and director startup.
        var spawner = module.Types.Single(type => type.FullName == "Viking_x86.vikinggame.SpawnMgr");
        Prepend(vikingPlay,
            Instruction.Create(OpCodes.Ldsfld, Field(timers, "timeMgr")),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Newobj, Ctor(vikingTimer)),
            Instruction.Create(OpCodes.Stelem_Ref),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Newobj, Ctor(spawner)),
            Instruction.Create(OpCodes.Stfld, Field(viking, "spawnMgr")),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldc_R4, 1f),
            Instruction.Create(OpCodes.Stfld, Field(viking, "initFrame")));

        // End Gam3 returns to a mode menu without ending the current music or
        // timeline. Stop the completed event before any other mode can start.
        var continueClick = endClick.Body.Instructions[0];
        var equals = endClick.Body.Instructions.Select(instruction => instruction.Operand)
            .OfType<MethodReference>().First(method => method.FullName ==
                "System.Boolean System.String::op_Equality(System.String,System.String)");
        Prepend(endClick,
            Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Ldstr, "End Gam3"),
            Instruction.Create(OpCodes.Call, equals),
            Instruction.Create(OpCodes.Brfalse, continueClick),
            Instruction.Create(OpCodes.Call, Method(music, "Stop")),
            Instruction.Create(OpCodes.Ldsfld, Field(timers, "timeMgr")),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Ldelem_Ref),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stfld, Field(baseTimer, "playMode")),
            Instruction.Create(OpCodes.Ldsfld, Field(timers, "timeMgr")),
            Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Ldelem_Ref),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stfld, Field(baseTimer, "playMode")),
            Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Stsfld, Field(timers, "time")));
    }

    private static void VerifyRunRestart(ModuleDefinition module)
    {
        var zombie = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.ZombieGame")
            .Methods.Single(method => method.Name == "Play" && method.Parameters.Count == 1);
        var viking = module.Types.Single(type => type.FullName == "Viking_x86.VikingGame")
            .Methods.Single(method => method.Name == "Play" && method.Parameters.Count == 0);
        var zombieCalls = zombie.Body.Instructions.Take(25).Select(instruction => instruction.Operand)
            .OfType<MethodReference>().Select(method => method.FullName).ToArray();
        var vikingCalls = viking.Body.Instructions.Take(10).Select(instruction => instruction.Operand)
            .OfType<MethodReference>().Select(method => method.FullName).ToArray();
        if (zombie.Body.Instructions[1].Operand is not FieldReference zombieSelector || zombieSelector.Name != "time" ||
            zombie.Body.Instructions[2].Operand is not FieldReference zombieClock || zombieClock.Name != "timeMgr" ||
            !zombieCalls.Any(call => call.Contains("CharMan::Init()")) ||
            !zombieCalls.Any(call => call.Contains("ParticleMan::Init()")) ||
            !zombieCalls.Any(call => call.Contains("MapMan::Init()")) ||
            !zombieCalls.Any(call => call.Contains("MapMan::ReadMap(")) ||
            !zombieCalls.Any(call => call.Contains("EndlessUpdate::.ctor()")) ||
            viking.Body.Instructions[0].Operand is not FieldReference vikingClock || vikingClock.Name != "timeMgr" ||
            !vikingCalls.Any(call => call.Contains("VikingTimeMgr::.ctor()")) ||
            !vikingCalls.Any(call => call.Contains("SpawnMgr::.ctor()")))
            throw new PreparationException("Run restart postcondition failed.");
        var endClick = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.menu.EndGameYouSure")
            .Methods.Single(method => method.Name == "Click" && method.Parameters.Count == 1);
        var cleanup = endClick.Body.Instructions.Take(20).ToArray();
        if (!cleanup.Any(instruction => instruction.Operand is MethodReference method &&
                method.FullName.Contains("Music::Stop()")) ||
            cleanup.Count(instruction => instruction.Operand is FieldReference field &&
                field.Name == "playMode" && instruction.OpCode == OpCodes.Stfld) != 2 ||
            cleanup.Count(instruction => instruction.Operand is FieldReference field &&
                field.Name == "time" && instruction.OpCode == OpCodes.Stsfld) != 1)
            throw new PreparationException("Run end cleanup postcondition failed.");
        var particleInit = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.particles.ParticleMan")
            .Methods.Single(method => method.Name == "Init");
        var mapInit = module.Types.Single(type => type.FullName == "IMAK3Z0MB1EGAEM.map.MapMan")
            .Methods.Single(method => method.Name == "Init");
        if (particleInit.Body.Instructions.Count(instruction => instruction.OpCode == OpCodes.Stsfld &&
                instruction.Operand is FieldReference field && field.Name is "addIdx" or "count") != 2 ||
            mapInit.Body.Instructions.Count(instruction => instruction.OpCode == OpCodes.Stsfld &&
                instruction.Operand is FieldReference field && field.Name is "frame" or "brite" or "goalBrite") != 3)
            throw new PreparationException("Run counters postcondition failed.");
    }
}
