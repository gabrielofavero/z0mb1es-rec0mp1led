# Desktop saves and offline services

The desktop build runs the supported, user supplied XAP as a full game. It has one local profile named **Local Player** by default; this name is supplied by the port's gamer service bridge, not the original phone game. It can be changed under Settings → Gameplay → Player Name and is saved in `player-name.json` beside the desktop save. The local profile does not sign in to Xbox Live. The original trial check sees full mode. Phone marketplace and online service features are unavailable.

The original version 101 save layout remains in use: a version byte, profile kills and 20 achievement flags, six settings flags, and custom image records. The ten original serializers now take `Stream` rather than `IsolatedStorageFileStream`; their method bodies are retained. `StorageMgr.Read` and `StorageMgr.Write` call the authored desktop storage hook. The original trial guard on writes remains.

The save lives at `%LOCALAPPDATA%\Z0MB1ES on teh ph0ne\savegame` on Windows. The launcher accepts `--save-dir <directory>` for isolated development tests. Each write flushes a temporary file, atomically replaces the primary, and keeps the prior generation as `savegame.bak`. Reads validate the version and complete structure before calling the original serializers. A corrupt, truncated, or older primary falls back to the backup; successful recovery restores the primary. A missing or invalid pair starts with the game's defaults. The format has no checksum, so damage that still forms a structurally valid save cannot always be detected.

The local achievements screen displays the original 20 keys and reads earned flags from the game's profile. It shows zero Xbox Gamerscore and generic descriptions because the phone service's display metadata is unavailable offline. Locked entry labels and icons use a more readable opacity on desktop. Only the original `AchievementMgr.AwardAchievement`/`PlayerProfile.AddAchievable` path changes earned flags; calling the bridge award completion alone does not. The original profile serializer persists those flags. The leaderboard screen completes immediately with an empty list and an offline explanation; no online ratings are read or submitted. The local gamer reports `IsSignedInToLive == false`, which keeps the original game's online submission gate closed. The menu's Xbox Live sign-in prompt is bypassed for the offline desktop reader.

For a reproducible runtime check from a staged build:

```powershell
dotnet private/run/prompt10-ui-final/Port.Launcher.dll --run private/run/prompt10-ui-final/ZombiesWP7.dll --content private/run/prompt10-ui-final --frames 300 --windowed --probe-services --save-dir private/run/prompt10-save-final
```

Run twice. The first run should print `SAVE_WRITTEN` and two `RUN_SERVICE_SCREEN` lines, followed by `serviceVerified=True`. The second should also print `SAVE_LOADED`. The `Storage.Checks` executable covers fresh, round trip, truncated, old version, and backup recovery cases.
