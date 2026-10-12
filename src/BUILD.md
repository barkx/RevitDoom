# Building from source

The prebuilt DLL is in `RevitDoom.extension/bin/`. You only need this if you want to change the C# code.

**You need:** the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

## RevitDoom.Managed.dll (DOOM)

The engine is Managed Doom by Nobuaki Tanaka (https://github.com/sinshu/managed-doom, commit 9365696, GPL-2.0). It is compiled for net8.0 without its Silk/OpenGL/OpenAL front-end. The RevitDoom host files in `host/` replace that front-end.

1. Clone managed-doom and copy `ManagedDoom/src` to `src/mdsrc/`. Delete `mdsrc/Silk`.
2. In `mdsrc/ConfigUtilities.cs`, add a `public static string DataDirectory` field. Make `GetExeDirectory()` return it when it is set, so saves and config don't go to Revit's install folder.
3. Build:
   ```bat
   cd src\host
   dotnet build -c Release
   ```
   The csproj compiles `../mdsrc/**/*.cs`. `nuget.config` clears package sources, because no packages are needed.
4. Copy `bin\Release\net8.0\RevitDoom.Managed.dll` to `RevitDoom.extension\bin\`.

`host/TestProgram.cs.txt` is a small console test that renders frames to files without Revit. Rename it to `.cs` in a separate console project to use it.

Restart Revit after replacing a DLL.
