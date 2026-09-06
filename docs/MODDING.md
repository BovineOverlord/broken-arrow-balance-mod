# Modding guide: BA Balance Mod

Most changes need no code at all. The mod is driven entirely by `_BAMod/overrides.json`, which you edit to
set unit and ammunition values (see the README for the field list and examples). This guide only covers
building the mod from source, in case you want to compile your own copy.

## Building from source

Prerequisites:

- Windows, with the game installed.
- .NET SDK 6.0 or newer (`dotnet --version` to check): https://dotnet.microsoft.com/download
- MelonLoader installed in the game folder, and the game launched once modded. Running `install.bat` from
  the repo root installs MelonLoader for you, and the first modded launch produces the assemblies the
  project builds against.

Build:

```powershell
dotnet build -c Release src\BABalanceMod.csproj -p:GameDir="D:\Games\Broken Arrow"
```

Point `-p:GameDir` at your own install, or edit the default `<GameDir>` line in `src\BABalanceMod.csproj`.
The output `BABalanceMod.dll` lands in `src\bin\Release\`. Copy it into the game's `Mods\` folder, or
overwrite `dist\BABalanceMod.dll` and re-run `install.bat`.
