namespace Port.Bridge;

/// <summary>The phone game's 20 local achievement keys and conditions.</summary>
public static class AchievementCatalog
{
    public static readonly string[] Keys =
    [
        "im_100k", "im_million", "im_win", "im_invincible", "im_asteroids", "im_nomove", "im_laser",
        "tv_100k", "tv_million", "tv_win", "tv_tower", "tv_invincible", "ez_100k", "ez_million",
        "ez_oneroom", "ez_oneround", "ez_tworounds", "kill1000", "kill10000", "mega"
    ];

    public static readonly string[] Names =
    [
        "Zombies: 100,000 points", "Zombies: 1,000,000 points", "Zombies: win", "Zombies: win without dying",
        "Zombies: asteroid survivor", "Zombies: stand still", "Zombies: laser master",
        "Time Viking: 100,000 points", "Time Viking: 1,000,000 points", "Time Viking: win",
        "Time Viking: tall tower", "Time Viking: win without dying",
        "Endless: 100,000 points", "Endless: 1,000,000 points", "Endless: one room",
        "Endless: one round", "Endless: two rounds", "1,000 kills", "10,000 kills", "Custom monsters"
    ];

    public static readonly string[] Conditions =
    [
        "Score 100,000 in I MAED A GAM3.", "Score 1,000,000 in I MAED A GAM3.",
        "Win I MAED A GAM3.", "Win I MAED A GAM3 without dying.",
        "Survive the asteroid phase without shooting in space.", "Stand still for 60 seconds.",
        "Reach 20 laser flags.", "Score 100,000 in Time Viking.", "Score 1,000,000 in Time Viking.",
        "Win Time Viking.", "Build the tower above 6,000.", "Win Time Viking without dying.",
        "Score 100,000 in Endless.", "Score 1,000,000 in Endless.",
        "Leave the starting room during round one.", "Complete round one in Endless.",
        "Complete round two in Endless.", "Get 1,000 total kills.", "Get 10,000 total kills.",
        "Enable custom monsters and create at least one custom image."
    ];
}
