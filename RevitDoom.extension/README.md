# RevitDoom: DOOM inside Revit (pyRevit)

RevitDoom plays DOOM in a window inside Revit, with sound effects. You start it from **DoomRevit ▸ Games ▸ DOOM**.

## Engine (v4)

v4 runs on **Managed Doom**, a full port of the DOOM engine to C# by Nobuaki Tanaka (GPL-2.0). It is compiled for **.NET 8**, which is the runtime Revit 2025 and newer use.

- **Pure managed code.** If the engine fails, the problem appears as a message in the console. Revit cannot crash or hang because of the game. This replaces the native doomgeneric DLL used in v1–v3, which hung in `P_Init` on Revit 2025.
- **Rendering and input.** The game runs at 35 game updates per second, the original DOOM rate. Each screen refresh is drawn with interpolation into a WPF bitmap at 640×400, shown at 4:3.
- **Sound.** Sound effects use positional stereo and play through the Windows waveOut API. If audio can't start, the game keeps running without sound. There is no music.
- **Saves and settings.** Savegames and `managed-doom.cfg` are stored in `%APPDATA%\RevitDoom\`.

**Requirements:** Revit 2025 or newer, with pyRevit (IronPython 2.7 or 3.4).

## Install

Copy `RevitDoom.extension` into a pyRevit extensions folder, then reload pyRevit.

- If you're **upgrading from v1–v3**, delete the old `bin\revitdoom.dll` and `bin\revitdoom.sym` from your copy. v4 doesn't use them.
- If you've **already played this Revit session**, restart Revit. The new engine only loads in a fresh session.

## WADs

- Freedoom: Phase 1 (BSD licence) is bundled in `wads/`.
- To play original DOOM, put `DOOM.WAD`, `DOOM2.WAD` or `DOOM1.WAD` into `wads\` or `%APPDATA%\RevitDoom\`. Original WADs take priority over Freedoom.
- **Shift+Click** the button to pick a WAD file yourself.

## Controls

| Action | Keys |
|---|---|
| Move | W/S or ↑/↓ |
| Strafe | A/D |
| Turn | ←/→, or the mouse (click the game first to capture it) |
| Fire | Ctrl or left mouse button |
| Use / open | Space or right mouse button |
| Walk | Shift. Always-run is on by default. |
| Strafe modifier | Alt |
| Weapons | 1–7 |
| Automap | Tab |
| Menu | Esc. This also releases the mouse. |
| Console | F12 or ` |

Cheats work, for example `iddqd` or `idkfa`.

## Console and logs

The console opens on its own while the game starts and whenever an error happens. Press F12 or ` to show or hide it at other times.

- **Status line.** Shows the game state, fps, tics, frames, whether sound is on, whether the mouse is captured, and the current stage.
- **Log lines.** `[host]` lines come from the pyRevit side. `[engine]` lines show each start-up step: loading the WAD, textures, sprites, the renderer, sound and the game.
- **Log files.** Everything is also written to `%APPDATA%\RevitDoom\host.log` and `doom.log`.

## Files

- `bin/RevitDoom.Managed.dll` contains the engine and host. The host loads a private copy each Revit session, so you can update the extension folder while Revit is open.
- `lib/revitdoom.py` holds the WPF window, console, key and mouse mapping, and the render loop.
- The C# sources and build notes are in `src/` in the repository (https://github.com/barkx/RevitDoom).

## Licence

- **Managed Doom and the RevitDoom host:** GPL-2.0. See `LICENSE-GPL2.txt` and `src/third-party-licenses` in the repository.
- **Freedoom:** BSD licence.
- DOOM is a trademark of id Software. This project is not affiliated with id Software.
