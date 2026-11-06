# Z0MB1ES (rec0mp1led)

Welcome in! This is a recompilation project for Z0MB1ES (on teh ph0ne), a Windows Phone 7 game that brought together three games by James Silva: I MAED A GAM3 W1TH Z0MB1ES 1N IT!!!1, TIME VIKING, and ZOMBIES ENDLESS. The first two were also released as Xbox 360 Indie Games, while Endless was exclusive to Windows Phone.

The goal is to get the game working on desktop (Windows, Linux, and macOS) and, later on, mobile (iOS and Android). I'm not trying to expand it or turn it into something different from what the original developer intended. This is a port to help preserve the game.

If you only want to play I MAED, keep in mind there's a brilliant, free version on [Steam](https://store.steampowered.com/app/1800730/I_MAED_A_GAM3_W1TH_Z0MB1ES_1N_IT1/) and [Xbox](https://www.xbox.com/en-US/games/store/z0mb1es-remastered/9PDG3T78XMQR).

The additions are pretty basic: mouse and keyboard controls, controller support, and support for different resolutions. I also plan to bring the Xbox Indie Games version into this project, so you can choose that version instead or play the mobile one with higher resolution assets.

## How to install

Once a release is available, download the installer from the Releases page. It will ask for your copy of the game's XAP and handle the rest locally. You won't need to install development tools or copy game files yourself.

The installer is built, but the final release checks are still in progress. See the [installer notes](installer/README.md) for the current status.

## How is proprietary content handled?

No original game code or assets are included in the repository or installer. The port patches and hooks into the code from the XAP you provide, and prepares its assets on your machine.

I recreated the game's cover as an SVG and adapted it for the icon and installer art. Those images are based on the original cover; they aren't a replacement for the game's assets.

## License

The code I wrote for this port is licensed under the [GNU General Public License, version 2](LICENSE). The original game and third-party dependencies belong to their respective owners and have their own terms.

## Was AI used?

Yes. This is an AI-assisted project.

If you're interested in the technical side, the [docs](docs/README.md) cover the port, its compatibility work, and what has been checked so far.
