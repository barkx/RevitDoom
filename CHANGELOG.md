# Changelog

## [4.0.0] - 2026-10-06

### Added
- Positional stereo sound effects through waveOut.
- Console with status line and `[host]` / `[engine]` log lines; logs written to `%APPDATA%\RevitDoom\`.

### Changed
- Ribbon tab renamed to **DoomRevit** (Games ▸ DOOM).
- Engine replaced with **Managed Doom** (C#, .NET 8). The game runs as pure managed code, so engine errors show up in the console instead of crashing or hanging Revit.
- Rendering with interpolation into a WPF bitmap at 640×400, shown at 4:3.
- Savegames and `managed-doom.cfg` stored in `%APPDATA%\RevitDoom\`.

### Removed
- The native doomgeneric DLL (`revitdoom.dll`) used in v1–v3, which hung in `P_Init` on Revit 2025, together with its C sources and build scripts.

## [1.0.0 – 3.x]
- Early versions based on doomgeneric (native C DLL). Not included in this repository.
