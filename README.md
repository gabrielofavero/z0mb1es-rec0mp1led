# Z0MB1ES (rec0mp1led)

Welcome! This is a recompilation project for Z0MB1ES (on teh ph0ne), a Windows Phone 7 game that brought together three games by James Silva: I MAED A GAM3 W1TH Z0MB1ES 1N IT!!!1, TIME VIKING, and ZOMBIES ENDLESS. The first two were also released as Xbox 360 Indie Games, while Endless was exclusive to Windows Phone.

The goal is to get the game working on desktop (Windows, Linux, and macOS) and, later on, mobile (iOS and Android). I'm not trying to expand it or turn it into something different from what the original developer intended. This is a port to help preserve the game.

If you only want to play I MAED, keep in mind that a brilliant, free version is available on [Steam](https://store.steampowered.com/app/1800730/I_MAED_A_GAM3_W1TH_Z0MB1ES_1N_IT1/) and [Xbox](https://www.xbox.com/en-US/games/store/z0mb1es-remastered/9PDG3T78XMQR).

The additions are pretty basic: mouse and keyboard controls, controller support, multiple resolutions, and optional higher-resolution artwork from Steam and Xbox 360 copies. Gameplay still runs the Windows Phone version underneath.

## How to install

Once a release is available, download the installer from the Releases page. It will ask for your copy of the game's XAP and handle the rest locally. You won't need to install development tools or copy game files yourself.

## Optional asset swapping

Although the WP7 package is all you need to play, its artwork is low resolution. The difference may be less noticeable on small screens, but it is clear compared with the official Xbox 360 and PC versions of I MAED.

With that in mind, the installer can import artwork from supported copies:

* **I MAED**: Steam or Xbox 360
* **TIME VIKING**: Xbox 360

You can then choose the artwork for each game under **Settings → ASSETS**. Unmatched art still comes from the phone package. The percentages below count mapped visual elements that use imported artwork:

| Copies supplied                    | I MAED        | TIME VIKING   | ZOMBIES ENDLESS |
| ---------------------------------- | ------------- | ------------- | --------------- |
| I MAED (Steam)                     | 69% (120/175) | -             | 49% (113/230)   |
| I MAED (360)                       | 69% (120/175) | -             | 49% (113/230)   |
| TIME VIKING (360)                  | -             | 57% (102/178) | 73% (168/230)   |
| I MAED (Steam) + TIME VIKING (360) | 69% (120/175) | 57% (102/178) | 74% (171/230)   |
| I MAED (360) + TIME VIKING (360)   | 69% (120/175) | 57% (102/178) | 74% (171/230)   |

For I MAED, you'll need to choose between the Xbox 360 and PC. Both cover the same number of mapped visual elements, so I recommend Steam because it is free and easy to get. Endless is an exclusive mobile mode, but most of the visual elements are available on the other games.

## How is proprietary content handled?

No original game code or assets are included in the repository or installer. The port patches and hooks into the code from the XAP you provide, and prepares its assets on your machine.

I recreated the game's cover as an SVG and adapted it for the icon and installer art. Those images are based on the original cover; they aren't a replacement for the game's assets.

## License

The code I wrote for this port is licensed under the [GNU General Public License, version 2](LICENSE). The separately bundled FFmpeg converter is built under LGPL 2.1 or later, with its [matching source and license delivery instructions](installer/ffmpeg/README.md). The original game and other third-party dependencies belong to their respective owners and have their own terms.

## Was AI used?

Yes. This is an AI-assisted project.

If you're interested in the technical side, the [docs](docs/README.md) cover the port, its compatibility work, and what has been checked so far.
