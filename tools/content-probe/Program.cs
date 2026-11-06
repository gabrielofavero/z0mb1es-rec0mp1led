using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;
using Port.Content;

var exerciseAudio = args.Length > 0 && args[^1] == "--exercise-audio";
var positionalCount = exerciseAudio ? args.Length - 1 : args.Length;
if ((positionalCount != 4 && positionalCount != 6) || args[0] != "--root" || args[2] != "--report" ||
    (positionalCount == 6 && args[4] != "--evidence"))
{
    Console.Error.WriteLine("Usage: content-probe --root <extracted-xap-directory> --report <private-report.json> [--evidence <inventory.json>] [--exercise-audio]");
    return 2;
}

try
{
    var root = Path.GetFullPath(args[1]);
    var report = Path.GetFullPath(args[3]);
    var privateRoot = Path.GetFullPath("private") + Path.DirectorySeparatorChar;
    if (!report.StartsWith(privateRoot, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Probe report must be written under ignored private/.");
    var assets = ContentInventory.Read(root);
    using var game = new ProbeGame(Path.Combine(root, "Content"), assets, exerciseAudio);
    game.Run();
    var candidates = positionalCount == 6 ? ReadCandidates(args[5], root) : [];
    var result = new { schemaVersion = 1, assets = game.Results, pathCandidates = candidates,
        audioChecks = game.AudioChecks };
    Directory.CreateDirectory(Path.GetDirectoryName(report)!);
    File.WriteAllText(report, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    var failures = game.Results.Count(row => row.Status == "failed") +
        game.AudioChecks.Count(row => !row.Passed);
    Console.WriteLine($"CONTENT_PROBE xnb={game.Results.Count(row => row.Asset.Kind == "xnb")} failures={failures} report={report}");
    return failures == 0 ? 0 : 1;
}
catch (Exception error)
{
    Console.Error.WriteLine($"CONTENT_PROBE_FATAL {error.GetType().Name}: {error.Message}");
    return 1;
}

static List<PathCandidate> ReadCandidates(string evidencePath, string root)
{
    using var evidence = JsonDocument.Parse(File.ReadAllText(evidencePath));
    var rows = new List<PathCandidate>();
    foreach (var candidate in evidence.RootElement.GetProperty("assetCandidates").EnumerateArray())
    {
        var literal = candidate.GetProperty("literal").GetString()!;
        if (!literal.Contains('/') || literal.StartsWith("Readz", StringComparison.Ordinal) ||
            literal.StartsWith("Loadzored", StringComparison.Ordinal) ||
            rows.Any(row => row.Literal == literal))
            continue;
        var normalized = literal.Replace('/', Path.DirectorySeparatorChar);
        var contentPath = Path.Combine(root, "Content", normalized);
        var packagePath = Path.Combine(root, normalized);
        string status;
        if (Path.Exists(contentPath) || Path.Exists(contentPath + ".xnb") || Path.Exists(packagePath))
            status = "present";
        else if (literal.EndsWith("/", StringComparison.Ordinal))
            status = "unresolved-prefix";
        else if (Directory.Exists(Path.GetDirectoryName(contentPath)) &&
                 Directory.EnumerateFiles(Path.GetDirectoryName(contentPath)!,
                     Path.GetFileName(contentPath) + "*.xnb").Any())
            status = "dynamic-stem";
        else
            status = "unresolved";
        rows.Add(new PathCandidate(literal, status, candidate.GetProperty("source").GetString()!));
    }
    return rows.OrderBy(row => row.Literal, StringComparer.Ordinal).ToList();
}

internal sealed record PathCandidate(string Literal, string Status, string Source);

internal sealed record ProbeRow(ContentAsset Asset, string Status, string? Detail, int? Width, int? Height,
    double? DurationSeconds);
internal sealed record AudioCheck(string Action, bool Passed, string Detail);

internal sealed class ProbeGame : Game
{
    private readonly IReadOnlyList<ContentAsset> assets;
    private readonly GraphicsDeviceManager graphics;
    private readonly bool exerciseAudio;
    private readonly List<SoundEffect> effects = [];
    private readonly List<Song> songs = [];
    private int audioStep;
    public List<ProbeRow> Results { get; } = [];
    public List<AudioCheck> AudioChecks { get; } = [];

    public ProbeGame(string contentRoot, IReadOnlyList<ContentAsset> assets, bool exerciseAudio)
    {
        this.assets = assets;
        this.exerciseAudio = exerciseAudio;
        graphics = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 320, PreferredBackBufferHeight = 240 };
        Content.RootDirectory = contentRoot;
        Window.Title = "FNA content probe";
    }

    protected override void LoadContent()
    {
        foreach (var asset in assets)
        {
            if (asset.Kind != "xnb")
            {
                Results.Add(new ProbeRow(asset, "inventory-only", null, null, null, null));
                continue;
            }
            var name = asset.Path["Content/".Length..^".xnb".Length];
            try
            {
                if (asset.Reader?.Contains("SpriteFontReader", StringComparison.Ordinal) == true)
                {
                    var font = Content.Load<SpriteFont>(name);
                    Results.Add(new ProbeRow(asset, "loaded", $"lineSpacing={font.LineSpacing}; glyphs={font.Characters.Count}", null, null, null));
                }
                else if (asset.Reader?.Contains("Texture2DReader", StringComparison.Ordinal) == true)
                {
                    var texture = Content.Load<Texture2D>(name);
                    Results.Add(new ProbeRow(asset, "loaded", null, texture.Width, texture.Height, null));
                }
                else if (asset.Reader?.Contains("SoundEffectReader", StringComparison.Ordinal) == true)
                {
                    var sound = Content.Load<SoundEffect>(name);
                    if (exerciseAudio) effects.Add(sound);
                    Results.Add(new ProbeRow(asset, "loaded", null, null, null, sound.Duration.TotalSeconds));
                }
                else if (asset.Reader?.Contains("SongReader", StringComparison.Ordinal) == true)
                {
                    var song = Content.Load<Song>(name);
                    MediaPlayer.Play(song);
                    var state = MediaPlayer.State;
                    MediaPlayer.Stop();
                    var playable = state == MediaState.Playing && song.Duration.TotalSeconds > 0;
                    if (exerciseAudio) songs.Add(song);
                    Results.Add(new ProbeRow(asset, playable ? "loaded" : "failed",
                        $"playbackState={state}; decodedDurationSeconds={song.Duration.TotalSeconds}",
                        null, null, song.Duration.TotalSeconds));
                }
                else
                    Results.Add(new ProbeRow(asset, "failed", "Unknown primary XNB reader", null, null, null));
            }
            catch (Exception error)
            {
                var cause = error.GetBaseException();
                Results.Add(new ProbeRow(asset, "failed", $"{cause.GetType().Name}: {cause.Message}", null, null, null));
            }
        }
        if (!exerciseAudio) Exit();
    }

    protected override void Update(GameTime gameTime)
    {
        if (!exerciseAudio) return;
        if (audioStep == 0)
        {
            Check("audio-inventory", effects.Count == 19 && songs.Count == 3,
                $"effects={effects.Count} songs={songs.Count}");
            Check("media-control", MediaPlayer.GameHasControl,
                $"gameHasControl={MediaPlayer.GameHasControl}");
            foreach (var effect in effects)
            {
                using var instance = effect.CreateInstance();
                instance.Play();
                Check("sfx-play", instance.State == SoundState.Playing, effect.Name ?? "unnamed");
                instance.Stop();
            }
            if (effects.Count > 0)
            {
                using var first = effects[0].CreateInstance();
                using var second = effects[0].CreateInstance();
                first.Play();
                second.Play();
                Check("sfx-overlap", first.State == SoundState.Playing &&
                    second.State == SoundState.Playing, effects[0].Name ?? "unnamed");
                first.Stop();
                second.Stop();
            }
        }
        else if (songs.Count > 0)
        {
            var songIndex = (audioStep - 1) / 4;
            var phase = (audioStep - 1) % 4;
            if (songIndex < songs.Count)
            {
                var song = songs[songIndex];
                if (phase == 0)
                {
                    MediaPlayer.Play(song);
                    Check("song-play", MediaPlayer.State == MediaState.Playing, song.Name);
                }
                else if (phase == 1)
                {
                    MediaPlayer.Pause();
                    Check("song-pause", MediaPlayer.State == MediaState.Paused, song.Name);
                }
                else if (phase == 2)
                {
                    MediaPlayer.Resume();
                    Check("song-resume", MediaPlayer.State == MediaState.Playing, song.Name);
                }
                else if (songIndex + 1 < songs.Count)
                {
                    MediaPlayer.Stop();
                    Check("song-stop", MediaPlayer.State == MediaState.Stopped, song.Name);
                }
                else
                {
                    Check("quit-during-playback", MediaPlayer.State == MediaState.Playing, song.Name);
                    Exit();
                }
            }
        }
        audioStep++;
    }

    private void Check(string action, bool passed, string detail)
    {
        AudioChecks.Add(new AudioCheck(action, passed, detail));
        Console.WriteLine($"AUDIO_CHECK action={action} passed={passed} detail={detail}");
    }
}
