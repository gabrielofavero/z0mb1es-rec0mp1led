using Microsoft.Xna.Framework.Media;

namespace Port.Hooks;

public static class MusicPlayback
{
    public static MediaState GetState(Song song)
    {
        var state = MediaPlayer.State;
        // Some converted Vorbis streams decode fewer samples than their declared
        // length. FAudio then misses END_OF_STREAM and stays Playing in silence.
        // FNA's playback position excludes pauses and resets on Stop; allow a
        // small margin before handing completion to the game's original logic.
        // Play(Song) stores a clone in Queue whose Duration is initially zero;
        // use the game's loaded Song, on which FNA records the decoded duration.
        if (state == MediaState.Playing && song is not null && song == MediaPlayer.Queue.ActiveSong &&
            song.Duration > TimeSpan.Zero &&
            MediaPlayer.PlayPosition >= song.Duration + TimeSpan.FromMilliseconds(250))
        {
            MediaPlayer.Stop();
            return MediaState.Stopped;
        }
        return state;
    }
}
