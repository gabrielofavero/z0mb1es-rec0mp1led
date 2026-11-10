# Z0MB1ES (rec0mp1led)

Welcome! This is a recompilation project for Z0MB1ES (on teh ph0ne), a Windows Phone 7 game that brought together three games by James Silva: I MAED A GAM3 W1TH Z0MB1ES 1N IT!!!1 (IMAGWZII), TIME VIKING, and ZOMBIES ENDLESS. The first two were also released as Xbox 360 Indie Games, while Endless was exclusive to Windows Phone.

The goal is to get the game working on desktop (Windows, Linux, and macOS) and, later, mobile (iOS and Android). I'm not trying to expand it or turn it into something different from what the original developer intended. This is a port to help preserve the game.

If you only want to play IMAGWZII, keep in mind that a brilliant, free version is available on [Steam](https://store.steampowered.com/app/1800730/I_MAED_A_GAM3_W1TH_Z0MB1ES_1N_IT1/) and [Xbox](https://www.xbox.com/en-US/games/store/z0mb1es-remastered/9PDG3T78XMQR).

The additions are pretty basic. They focus mainly on making the game accessible across devices:

* Multiple resolutions, including ultrawide
* Mouse and keyboard controls for gameplay and menus
* Controller controls for gameplay and menus
* Optional higher-resolution textures import
* A reduced-flashing setting for menus and gameplay (experimental; it may not cover everything)
* Desktop support for Mega Game Creator 2000
* Online leaderboards converted to offline leaderboards

## Flashing lights warning

This game uses flashing visuals as part of its original art style. **Settings → Graphics → Reduce Flashing** reduces some menu and gameplay effects, but flashing can still occur with the setting enabled. The installer can enable it on first launch.

The game is not recommended for people with photosensitive epilepsy or other sensitivity to flashing lights.

## How to install

Once a release is available, download the installer from the Releases page. It will ask for your copy of the game's XAP and handle the rest locally. You won't need to install development tools or copy game files yourself.

Linux x64 `.deb` and `.rpm` packages are being tested. Their desktop entry opens a first-launch XAP importer, then stores prepared assets and saves in your user data directory. The [Linux package guide](installer/linux/README.md) explains the current local artifacts and remaining desktop checks; no Linux release has been published yet.

Setup also lets you choose installation, save, and picture folders. New saves default to `Documents\Z0MB1ES on teh ph0ne\Saves`; put PNG or JPEG images in the chosen picture folder to use them in Edit Monsters.

If you're interested in the technical side, the [docs](docs/README.md) cover the port, its compatibility work, and what has been checked so far.

## Optional asset swapping

Although the WP7 package is all you need to play, its artwork has half the resolution of the artwork on other platforms. The difference may be less noticeable on small screens, but it can be distracting on larger ones.

With that in mind, the installer can import artwork from supported copies:

* **IMAGWZII**: Steam or Xbox 360
* **TIME VIKING**: Xbox 360

You can then choose the artwork for each game under **Settings → ASSETS**. Unmatched art still comes from the phone package. The percentages below count mapped visual elements that use imported artwork:

| Copies supplied                      | IMAGWZII      | TIME VIKING   | ZOMBIES ENDLESS |
| ------------------------------------ | ------------- | ------------- | --------------- |
| IMAGWZII (Steam)                     | 69% (120/175) | -             | 49% (113/230)   |
| IMAGWZII (360)                       | 69% (120/175) | -             | 49% (113/230)   |
| TIME VIKING (360)                    | -             | 57% (102/178) | 73% (168/230)   |
| IMAGWZII (Steam) + TIME VIKING (360) | 69% (120/175) | 57% (102/178) | 74% (171/230)   |
| IMAGWZII (360) + TIME VIKING (360)   | 69% (120/175) | 57% (102/178) | 74% (171/230)   |

For IMAGWZII, you'll need to choose between the Xbox 360 and PC versions. Both cover the same number of mapped visual elements, so I recommend Steam because it is free and easy to get. ZOMBIES ENDLESS is exclusive to mobile, but most of its visual elements are available in the other games.

## How is proprietary content handled?

No original game code or assets are included in the repository or installer. The port patches and hooks into the code from the XAP you provide, and prepares its assets on your machine.

I recreated the game's cover as an SVG and adapted it for the icon and installer art. Those images are based on the original cover; they aren't a replacement for the game's assets.

## License

The code I wrote for this port is licensed under the [GNU General Public License, version 2](LICENSE). The separately bundled FFmpeg converter is built under LGPL 2.1 or later, with its matching source and license delivery instructions [here](installer/ffmpeg/README.md). The original game and other third-party dependencies belong to their respective owners and have their own terms.

## Was AI used?

Yes. This is an AI-assisted project.
