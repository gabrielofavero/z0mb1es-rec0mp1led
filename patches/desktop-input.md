# Guarded desktop input adaptation

For the supported original DLL (SHA-256 `7b819cb562826ae4d2592ff741877e76da0e57bfbe48192aabb79319fdb36823`), `ZombiesWP7.TouchMgr.Update()` has token `0x0600007a` and original IL body SHA-256 `5d5aea53560307207b175bab14d39138756165598972d7604491f4cd44d7bfc6`. Four call operands are changed after the pinned ABI rewrite:

| Original IL offset | Original call | Authored replacement |
| --- | --- | --- |
| `0x00d2` | `GamePad.GetState(PlayerIndex)` | `DesktopInput.GetGamePadState(PlayerIndex)` |
| `0x017b` | `TouchPanel.GetState()` | `DesktopInput.GetState()` |
| `0x0785` | `TouchPanel.get_IsGestureAvailable()` | `DesktopInput.get_IsGestureAvailable()` |
| `0x078f` | `TouchPanel.ReadGesture()` | `DesktopInput.ReadGesture()` |

The original method's instructions and branches stay in place. `GameplayPatcher` checks the XAP/DLL, original method token/body, exact call offsets and signatures, prior prepared manifest, and `Port.Hooks` hash `a90344b9a3fa20d4c6c5ee42c6bacb02f32716b5b7504bad57680bd573ac8905`; it reopens the result to verify the new targets. Unsupported or tampered input fails before a final output directory is created. The original DLL is unchanged.

The hook retains real FNA touches and gestures. It adds mouse contacts and taps on menus, directional right-stick contacts from playfield clicks, keyboard/controller virtual stick contacts during play, and menu navigation from the D-pad or left stick. Menu navigation follows the original item's screen position, highlights the focused item, and routes A through the original click path. B sends one original Back event for menu cancellation; Start does the same during play for pause. The original `TouchMgr` still decides menu clicks, movement, aim, pause, and back effects. See [desktop input](../docs/runtime/desktop-input.md) for observations and limits.

The original `ZombiesWP7.player.Settings::.ctor()` body is guarded by SHA-256 `87b74f99337495a86a9b364e8005b62b386334765edce14db0cf606714e66b3e`. Its `showTouchPads` initializer changes from true to false. Saved settings still load through the original reader.
