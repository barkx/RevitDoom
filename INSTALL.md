# Installing RevitDoom

## Requirements

| | |
|---|---|
| Revit | 2025 or newer (uses .NET 8) |
| pyRevit | 4.8 or newer, IronPython 2.7 or 3.4 engine |
| OS | Windows 10 or 11, 64-bit |

If you don't have pyRevit yet, install it from https://github.com/pyRevitLabs/pyRevit/releases and start Revit once so the **pyRevit** tab appears.

## Option A: Clone with git (easy to update)

```bat
cd C:\RevitExtensions
git clone https://github.com/barkx/RevitDoom.git
```

## Option B: Download a ZIP

1. On https://github.com/barkx/RevitDoom click **Code ▸ Download ZIP**.
2. Extract it, for example to `C:\RevitExtensions\RevitDoom`.
3. Make sure the folder contains `RevitDoom.extension` directly (not `RevitDoom-main\RevitDoom-main\…`).

> Windows may block files downloaded from the internet. Right-click the ZIP ▸ **Properties** ▸ tick **Unblock** before extracting, otherwise Revit can refuse to load the DLLs.

## Add the extension to pyRevit

1. Open Revit.
2. **pyRevit ▸ Settings ▸ Custom Extension Directories ▸ Add folder**.
3. Pick the repository folder, the one that **contains** `RevitDoom.extension` (for example `C:\RevitExtensions\RevitDoom`).
4. Click **Save Settings and Reload**.

A **DoomRevit** tab appears with a **Games** panel and a **DOOM** button.

*Alternative:* copy only the `RevitDoom.extension` folder into `%APPDATA%\pyRevit\Extensions\` and reload pyRevit.

## First run

Click **DOOM**. The console shows each start-up step, then the game starts with Freedoom. Click inside the game to capture the mouse; Esc releases it.

## Using the original DOOM

Copy `DOOM.WAD`, `DOOM2.WAD` or `DOOM1.WAD` (the shareware episode) into either:

- `RevitDoom.extension\wads\`, or
- `%APPDATA%\RevitDoom\`

Original WADs are preferred over Freedoom. **Shift+Click** the DOOM button to choose a WAD file yourself; that choice is remembered and used on later clicks until you pick another.

## Updating

- **git:** `git pull` in the repository folder.
- **ZIP:** replace the folder with the new download.

Then restart Revit. The engine DLL is loaded once per Revit session, so a new version only takes effect in a fresh session.

Upgrading from v1–v3: delete the old `bin\revitdoom.dll` and `bin\revitdoom.sym` if they are still in your copy.

## Uninstalling

1. **pyRevit ▸ Settings ▸ Custom Extension Directories**, remove the RevitDoom folder, **Save Settings and Reload**.
2. Delete the repository folder.
3. Optional: delete `%APPDATA%\RevitDoom\` (savegames, settings, logs).

## Troubleshooting

| Problem | Fix |
|---|---|
| No **DoomRevit** tab | Check that the folder you added *contains* `RevitDoom.extension`, then pyRevit ▸ Reload. |
| "Could not load file or assembly" | Unblock the downloaded ZIP (see above), or check that you're on Revit 2025+. Older Revit versions use .NET Framework and can't load the .NET 8 DLLs. |
| Game window opens but stays black | Open the console (F12) and check the last `[engine]` line. Logs are in `%APPDATA%\RevitDoom\host.log` and `doom.log`. |
| No sound | The game keeps running without sound if waveOut can't start. Check your default audio device. |
| A new version doesn't seem to load | Restart Revit; the engine only loads in a fresh session. |

When reporting a bug, please attach `host.log` and `doom.log` from `%APPDATA%\RevitDoom\` and your Revit and pyRevit versions.
