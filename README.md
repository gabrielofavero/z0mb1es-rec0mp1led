# Z0MB1ES (rec0mp1led)

Welcome! This is a recompilation project for Z0MB1ES (on teh ph0ne), a Windows Phone 7 game that brought together three games by James Silva: I MAED A GAM3 W1TH Z0MB1ES 1N IT!!!1 (IMAGWZII), TIME VIKING, and ZOMBIES ENDLESS. The first two were also released as Xbox 360 Indie Games, while Endless was exclusive to Windows Phone.

The target platforms are desktop (Windows, macOS, and Linux) and mobile (iOS and Android). I don't plan to make console versions (Switch, PlayStation, or Xbox), but you are free to fork this project and do it if you wish.

Oh, and if you only want to play IMAGWZII, keep in mind there are brilliant, free versions available on [Steam (Windows / Linux)](https://store.steampowered.com/app/1800730/I_MAED_A_GAM3_W1TH_Z0MB1ES_1N_IT1/) and [Xbox One / Series](https://www.xbox.com/en-US/games/store/z0mb1es-remastered/9PDG3T78XMQR).

## Enhancements

The additions are pretty basic. They focus mainly on making the game accessible across devices:

* Multiple resolutions, including ultrawide resolutions on desktop.
* Mouse, keyboard, and controller support (the original game only supported touch).
* Optional imports of higher-resolution textures from the Steam and Xbox 360 games.
* A reduced-flashing setting for menus and gameplay (experimental; it may not cover everything).
* Adapted support for the Mega Game Creator 2000 tool/minigame.
* Offline leaderboards to compare your runs (adapted from the now-defunct Xbox LIVE leaderboards).
* Many layout adjustments for both desktop and mobile.
* Portrait mode. This is the only drastic change from the original design. I added it because many devices with large screens work well in portrait orientation, making gameplay more accessible. It is disabled by default; enable it in Settings if you want to try it out.

## How to run

Find published builds on the [Releases page](https://github.com/gabrielofavero/z0mb1es-rec0mp1led/releases). The Windows installer asks for the game assets during setup. Linux, macOS, and mobile builds ask for the assets on first launch, then prepare and store them on your device for future sessions.

You can also build the games from the terminal. See [Build and run](build-and-run.md) for instructions for every platform, including how to build for mobile with assets already included to skip on-device import.

## Asset swapping

Although the WP7 package is all you need to play, its artwork has half the resolution of other platforms. With that in mind, the installer can import from different supported copies:

* **IMAGWZII**: Steam (desktop only) or Xbox 360
* **TIME VIKING**: Xbox 360

Once imported, you can choose the artwork for each game in real time under **Settings → ASSETS**. If an enhanced texture isn't available, the game uses the original one. Many assets are exclusive to WP7, so some original / low res textures are unavoidable. I haven't added AI upscaling because I don't want to compromise the original artists' vision.

The table below shows the proportion of mapped visual elements that use imported artwork:

| Copies supplied         | IMAGWZII      | TIME VIKING   | ZOMBIES ENDLESS |
| ----------------------- | ------------- | ------------- | --------------- |
| IMAGWZII (Steam or 360) | 69% (120/175) | -             | 49% (113/230)   |
| TIME VIKING (360)       | -             | 57% (102/178) | 73% (168/230)   |
| Both                    | 69% (120/175) | 57% (102/178) | 74% (171/230)   |

## How is content handled?

No game code or assets are included in the repository or installer. The port patches and hooks into the code from the packages you provide and prepares its assets on your device.

I recreated the game's cover as an SVG and adapted it for the icon and installer art. Those images are based on the original cover; they aren't a copy-paste from the game's assets.

## Project status and future support

I currently consider this project complete. I don't have any additional features planned, and I've tested all intended platforms on real devices. I will continue to fix bugs reported in the [Issues section](https://github.com/gabrielofavero/z0mb1es-rec0mp1led/issues). Testing this many platforms as a single dev is bound to leave some bugs behind. I want everyone to have a good experience, so please report any problems you find and I'll do my best to fix them when possible.

## Flashing lights warning

This game uses flashing visuals as part of its original art style. **Settings → Graphics → Reduce Flashing** reduces some menu and gameplay effects, but flashing can still occur with the setting enabled. Windows setup can enable it for first launch; macOS, iOS, and Android offer the choice in a one-time warning when the app first opens.

The game is not recommended for people with photosensitive epilepsy or other sensitivity to flashing lights.

## License

The code I wrote for this port is licensed under the [GNU General Public License, version 2](LICENSE). The offline audio converter uses vendored [gameframe-wma](tools/audio-converter/vendor/gameframe-wma/LICENSE-APACHE) and [qoaudio](tools/audio-converter/vendor/qoaudio/LICENSE) code under their respective licenses. The original game and other third-party dependencies belong to their respective owners and have their own terms.

## Was AI used?

Yes. This is an AI-assisted project. I also make changes manually and apply my own software development skills throughout the process.
