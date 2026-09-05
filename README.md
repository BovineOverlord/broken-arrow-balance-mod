# Broken Arrow — Balance Mod

A [MelonLoader](https://github.com/LavaGang/MelonLoader) mod that lets you rebalance **Broken Arrow**'s
unit and weapon stats by editing a plain-text JSON file. It changes the game's database **live in memory**
each launch, so it works around the on-disk encryption without ever modifying game files.

Edit a number, restart the game, and your change is in effect. Remove the mod and the game is back to stock.

> **Single-player / offline only.** The stat database is server-authoritative online and the client is
> integrity-checked, so modified stats do **not** work in official multiplayer — and you shouldn't try to
> use them there. This is a tool for offline play and experimentation. It is a fan-made mod and is not
> affiliated with or endorsed by the developers or publisher of Broken Arrow.

---

## Install (the easy way)

1. Click the green **Code ▸ Download ZIP** button above and unzip it anywhere.
2. Double-click **`install.bat`**.

That's it. The installer will:

- find your Broken Arrow install automatically (Steam libraries and common paths; it asks if it can't),
- download and install **MelonLoader v0.7.3** into the game folder **for you** if it isn't already there,
- copy the mod into `Mods\`,
- create `_BAMod\overrides.json` (the file you edit) if you don't already have one,
- and add a **"Broken Arrow (Modded)"** desktop shortcut.

Then launch the game with that new desktop shortcut (see below) and you're modded.

> **Why a special shortcut?** Broken Arrow's normal Steam launch runs through EasyAntiCheat, which blocks
> mods. The shortcut starts `BrokenArrow.exe` directly with anti-cheat off, which is required for any
> MelonLoader mod to load. The very first modded launch is slower because MelonLoader generates the game's
> code assemblies once.

## How to change stats

1. Open **`_BAMod\overrides.json`** (created by the installer, in your game folder).
2. Add or edit entries under `units` or `ammunitions`.
3. Launch modded and your values are applied right after the database loads.

```jsonc
{
  "units": [
    { "id": 240, "name": "BMPT Terminator", "Cost": 190 },
    { "id": 159, "name": "UH-1Y Venom", "Cost": 70, "HP": 12, "KinF": 40 }
  ],
  "ammunitions": [
    { "id": 2, "Damage": 12.5, "PenMin": 500 }
  ]
}
```

- `id` is the reliable key. `name` also works (exact, case-insensitive). Any field you leave out is untouched.
- After the first launch, look in **`_BAMod\dump\`** for `Units_full.json` and `Ammunitions_full.json` —
  full exports of every unit and ammo with their ids, names and **current** values, so you know exactly
  what to copy and what number you're changing.
- To confirm it worked, open `MelonLoader\Latest.log` and look for a line like
  `applied overrides: units=N ammo=M`.

### Fields you can change

**Per unit** (`units`): `Cost` (arsenal price), `HP` (max health), `Stealth`, and armour —
`ArmorValue`, `KinF/KinS/KinR/KinT` (kinetic front/side/rear/top), `HeatF/HeatS/HeatR/HeatT`
(HEAT front/side/rear/top).

**Per ammunition** (`ammunitions`): `Damage`, `PenMin` / `PenGround` (penetration at min / ground range),
`SupplyCost`, `ResupplyTime`.

Weapons (reload, aim, burst), mobility (speed, agility), sensors and abilities all live in the same
database and can be exposed with a few more lines of code — see [docs/MODDING.md](docs/MODDING.md).

### What it can't do

- **Affect online multiplayer** — offline only, by design.
- **Add new units or models** — it changes numbers on existing entities, it doesn't add content.
- **Change things that aren't per-unit** — a few tunables (e.g. global sprint cooldowns) live in separate
  config objects and aren't wired into `overrides.json` yet.
- **Patch files permanently** — changes are re-applied at runtime each launch. That's what makes it
  non-destructive; it also means the mod has to stay installed.

## Uninstall

Delete `Mods\BABalanceMod.dll`. The game is immediately back to stock. To remove the loader entirely,
delete `version.dll` and the `MelonLoader\` folder from the game directory.

## Build it yourself / extend it

Everything you need — how the mod works, the full `overrides.json` schema, how to expose new stat fields,
and how to compile from source — is in **[docs/MODDING.md](docs/MODDING.md)**.

## License

[MIT](LICENSE) © 2026 BovineOverlord. Provided as-is, with no warranty. Use at your own risk;
you are responsible for complying with the game's own terms of service.
