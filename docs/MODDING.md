# Modding guide: BA Balance Mod

This document explains how the mod works, how to add new editable fields, and how to build it from source.
It's written so another modder can reproduce and extend everything.

## How it works

Broken Arrow's per-unit stats (HP, armour, price, weapon damage, penetration, and so on) are **not** stored
as normal Unity data. They live inside a single Resources asset, `DataBaseCompiled`, which ships encrypted
on disk, so editing the file directly is a dead end.

You don't have to. The game decrypts the database into plain model objects with public setters at runtime,
so the mod leaves the file alone and works with those live objects instead. On each launch it:

1. waits for `BrokenArrow.Shared.Ecs.DataBaseService._instance` to report `IsLoaded`,
2. reads your `overrides.json`,
3. looks each entry up by id (`GetUnitById`, `GetAmmunitionById`) and writes the new values straight onto
   the live, decrypted model objects,
4. re-applies a few times so the values survive scenario reloads.

Because it only edits objects in memory, nothing on disk changes and removing the DLL restores stock behaviour.

## `overrides.json` schema

```jsonc
{
  "units": [
    // match by "id" (best) or "name" (exact, case-insensitive). Omit any field to leave it untouched.
    { "id": 240, "name": "BMPT Terminator", "Cost": 190, "HP": 20,
      "ArmorValue": 5,
      "KinF": 40, "KinS": 20, "KinR": 10, "KinT": 5,     // kinetic armour front/side/rear/top
      "HeatF": 30, "HeatS": 15, "HeatR": 10, "HeatT": 5, // HEAT armour front/side/rear/top
      "Stealth": 0.5 }
  ],
  "ammunitions": [
    { "id": 2, "Damage": 12.5, "PenMin": 500, "PenGround": 450,
      "SupplyCost": 20, "ResupplyTime": 5 }
  ]
}
```

Find ids, exact names and current values in `_BAMod\dump\Units_full.json` and
`_BAMod\dump\Ammunitions_full.json`, which the mod re-exports every launch.

## Adding a new editable field

The mapping from JSON keys to game fields lives in `src/BalanceMod.cs`:

- `ApplyUnits()` handles the `units` list. Each field is a line like:
  ```csharp
  if (Has(o, "HP")) { a.MaxHealthPoints = VI(o["HP"]); changed++; }
  ```
  To expose another unit/armour property, add a matching line using the model's own property name.
- `ApplyAmmo()` handles the `ammunitions` list the same way.
- The models come from `Il2CppBrokenArrow.DataBase.Models` (`Units`, `Armors`, `Ammunitions`, `Options`,
  `Weapons`, `Mobility`, `Sensors`, `Abilities`). Browse them in the generated
  `MelonLoader\Il2CppAssemblies\Il2CppBrokenArrow.dll` (see below) to discover property names.
- `VI()` reads an int, `VF()` reads a float, `Has()` checks a key is present. Use whichever matches the
  property type.

Weapons, mobility, sensors and abilities are reached from a unit or by id in the same way, wire them into
a new `ApplyX()` and a new JSON section following the existing pattern.

## Building from source

**Prerequisites**

- Windows, with the game installed.
- [.NET SDK 6.0 or newer](https://dotnet.microsoft.com/download) (`dotnet --version` to check).
- MelonLoader installed in the game folder, and the game launched **once** modded. That first launch
  generates `MelonLoader\Il2CppAssemblies\`, the game's IL2CPP code as regular .NET assemblies, which this
  project references. (Running `install.bat` from the repo root installs MelonLoader for you.)

**Build**

```powershell
dotnet build -c Release src\BABalanceMod.csproj -p:GameDir="D:\Games\Broken Arrow"
```

Point `-p:GameDir` at your own install (or edit the default `<GameDir>` line in `src\BABalanceMod.csproj`).
The output `BABalanceMod.dll` lands in `src\bin\Release\`. Copy it into the game's `Mods\` folder, or just
overwrite `dist\BABalanceMod.dll` and re-run `install.bat`.

## Rebuilding against a new game patch

If a game patch shifts things, this is the reproducible path that got here:

1. Install MelonLoader (IL2CPP build) into the game folder; launch once to generate `Il2CppAssemblies`.
2. The gameplay database is `BrokenArrow.Shared.Ecs.DataBaseService` (singleton `_instance`), exposing
   `GetUnitById`, `GetAllUnits`, `GetAmmunitionById`, `GetOptionById`. The on-disk `DataBaseCompiled` is
   encrypted, but the game decrypts it into `BrokenArrow.DataBase.Models.*` objects with public setters , 
   edit those at runtime.
3. Poll `DataBaseService._instance.IsLoaded` in `OnUpdate`. Don't Harmony-hook the loader (it's inlined),
   and don't enumerate the Il2Cpp collections directly (that crashes), look items up by id.
4. Build against the interop assemblies and drop the DLL in `Mods\`.

## Notes

- Arsenal card price = a unit's base `Cost` plus its default loadout, so a card can read slightly higher
  than the `Cost` you set.
- Some named variants in older changelogs don't exist under the same name in the current build; match by id
  from the dumps to be sure.
