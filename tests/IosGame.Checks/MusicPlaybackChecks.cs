using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Media;
using Port.Hooks;

internal static class MusicPlaybackChecks
{
    // Supply an ignored, locally converted ~0.25 second Vorbis clip. Exercise the
    // real prepared Music methods and FNA clock without waiting five minutes.
    internal static void Run(string gamePath, string clipPath, string nativeDirectory)
    {
        NativeLibrary.SetDllImportResolver(typeof(Song).Assembly, (name, _, _) =>
        {
            var file = OperatingSystem.IsMacOS() ? $"lib{name}.0.dylib" :
                OperatingSystem.IsWindows() ? $"{name}.dll" : $"lib{name}.so.0";
            return NativeLibrary.Load(Path.Combine(Path.GetFullPath(nativeDirectory), file));
        });
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var game = Assembly.LoadFrom(Path.GetFullPath(gamePath));
        Type Type(string name) => game.GetType(name, true)!;
        var music = Type("IMAK3Z0MB1EGAEM.audio.Music");
        void Call(string name, params object[] args) => music.GetMethod(name, flags)!.Invoke(null, args);
        bool Failed() => (bool)music.GetField("mediafail", flags)!.GetValue(null)!;
        void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        var player = RuntimeHelpers.GetUninitializedObject(Type("ZombiesWP7.player.Player"));
        var settings = Activator.CreateInstance(Type("ZombiesWP7.player.Settings"))!;
        var bgm = settings.GetType().GetField("bgm", flags)!;
        bgm.SetValue(settings, true);
        player.GetType().GetField("settings", flags)!.SetValue(player, settings);
        Type("ZombiesWP7.Game1").GetField("player", flags)!.SetValue(null, player);
        using var song = (Song)Activator.CreateInstance(typeof(Song), flags, null,
            [Path.GetFullPath(clipPath), "music-check"], null)!;
        var bank = (Song[])music.GetField("bank", flags)!.GetValue(null)!;
        Array.Fill(bank, song);
        void Start(int index)
        {
            music.GetField("song", flags)!.SetValue(null, index);
            var timers = Type("Viking_x86.director.TimeMgr");
            timers.GetField("time", flags)!.SetValue(null, index == 1 ? 1 : 0);
            Call("Start");
            Require(MediaPlayer.State == MediaState.Playing && !Failed(), "Music did not start.");
        }
        try
        {
            Start(2);
            Require(song.Duration.TotalSeconds is > 0.1 and < 0.4,
                "Expected a ~0.25 second local Vorbis clip.");
            Thread.Sleep(80);
            Call("Pause");
            var pausedAt = MediaPlayer.PlayPosition;
            Thread.Sleep(650);
            Require(MusicPlayback.GetState(song) == MediaState.Paused && MediaPlayer.PlayPosition == pausedAt,
                "Paused wall time completed the song.");
            Call("Resume");
            Require(MusicPlayback.GetState(song) == MediaState.Playing, "Resume lost the remaining song time.");

            var loops = 0;
            var previous = MediaPlayer.State;
            var timeout = Stopwatch.StartNew();
            while (loops < 3 && timeout.Elapsed < TimeSpan.FromSeconds(5))
            {
                FrameworkDispatcher.Update();
                Call("Update", 2);
                var state = MediaPlayer.State;
                if (state == MediaState.Playing && previous == MediaState.Stopped) loops++;
                previous = state;
                Thread.Sleep(16);
            }
            Require(loops == 3 && !Failed(), "Endless did not repeat three times after resume.");
            Call("Stop");
            Thread.Sleep(600);
            Require(MusicPlayback.GetState(song) == MediaState.Stopped, "Explicit stop restarted music.");

            foreach (var index in new[] { 0, 1 })
            {
                Start(index);
                Thread.Sleep(650);
                Call("Update", index);
                Require(MediaPlayer.State == MediaState.Stopped && !Failed(), "Finite mode repeated its song or failed.");
            }
            bgm.SetValue(settings, false);
            Call("Start");
            Require(MediaPlayer.State == MediaState.Stopped, "Disabled BGM started playing.");
            Console.WriteLine("MUSIC_PLAYBACK_OK endlessLoops=3; pause/resume, stop, finite modes, and disabled BGM passed");
        }
        finally { MediaPlayer.Stop(); }
    }
}
