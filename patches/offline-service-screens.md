# Offline service menu adaptation

The exact supported original DLL is guarded by its SHA-256 `7b819cb562826ae4d2592ff741877e76da0e57bfbe48192aabb79319fdb36823`. `GameplayPatcher` also checks the original method bodies for `Viking_x86.main.MainMenu.Update` (`98bef27d1b1ed69161d3bc16472a891174b87f023e936b929e7d5ec7b19fd0b2`), `IMAK3Z0MB1EGAEM.menu.Leaderboards.Update` (`5b5d6dcc387237ee80f60ec5e599714c31c4a6b877ecd0b4e1ab6138d3df2a19`), and `ZombiesWP7.menu.MenuItem.Draw` (`f72bc14c85ee9de04d11c79cb67437d6d77fcf857ac560f8581e0f7a402fb550`).

The desktop build has a local gamer but no Xbox Live session. Two guarded sign-in UI checks are made true only within the menu entry and lifetime logic, allowing the original leaderboard screen to use the bridge's completed empty reader. The separate online score submission gate remains unchanged and sees `IsSignedInToLive == false`. The empty-result text says online leaderboards are unavailable offline. Two locked-achievement icon/name opacity constants change from 0.4 to 0.7 so unearned entries remain distinct but readable. The patch verifies each instruction and text postcondition in the prepared DLL.

The diagnostic `--probe-services` run initializes both original screens, checks 20 achievement rows and the active empty leaderboard screen, and captures ignored BMP images for visual review.
