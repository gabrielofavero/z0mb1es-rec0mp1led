namespace Port.Hooks;

public static class DesktopLeaderboards
{
    public static void Record(int mode, long score) => Port.Bridge.LocalScores.Record(mode, score);
}
