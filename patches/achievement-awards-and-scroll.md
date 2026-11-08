# Local achievement awards and desktop scrolling

The pinned phone DLL already awards its 20 achievement flags at gameplay call sites. Its manager updates `PlayerProfile` but has no visible desktop notification. The guarded `AchievementMgr.AwardAchievement` call to `PlayerProfile.AddAchievable` now goes through `DesktopAchievements`, which calls the original method, detects a false-to-true flag change, saves the original version-101 profile immediately, and queues a four-second in-game notice. Loading earned flags does not produce notices or duplicate awards. The offline achievement reader uses the original keys with descriptive names and conditions; Xbox Gamerscore remains zero.

On the active Achievements menu, the desktop input hook changes the original `MenuLevel.scroll` field for mouse-wheel and right-stick vertical input. It clamps to the original `scrollMax`. Touch swipes and other menu controls still use their existing paths. The input check covers wheel direction, stick direction and dead zone, and scroll bounds.

An isolated Endless mode probe invoked the original round transition twice, logged unlocks for IDs 15 and 16, and wrote the save after each. A fresh process loaded that save and the Achievements screen reported 20 entries with 2 earned. This verifies the award and persistence path, while physical wheel and controller feel still require a hands-on check.
