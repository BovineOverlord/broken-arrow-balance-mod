# Broken Arrow: Balance Mod

A MelonLoader mod that lets you rebalance Broken Arrow's unit and weapon stats by editing a plain-text
JSON file. Edit a number, restart the game, and your change is in effect. Remove the mod and the game is
back to stock.

> **Requires the [Local Skirmish mod](https://github.com/BovineOverlord/broken-arrow-local-skirmish) as
> well.** Install both. Local Skirmish is what gives you a working offline battle to actually play your
> rebalanced stats in.

> ## ⚠️ Single-player / offline only — going online with mods can get you BANNED
> Modified stats are **server-checked** in official multiplayer, and the client is integrity-checked. Playing
> online with the mod active is detectable and **can get your account banned** (Skirmish is online by default —
> that's what the [Local Skirmish mod](https://github.com/BovineOverlord/broken-arrow-local-skirmish) makes
> offline). Rules: launch **only** with the "Broken Arrow (Modded)" shortcut (anti-cheat off, so you're never
> online with mods); **never** use Steam **Play**/`EACLauncher.exe` while the DLL is in `Mods\`; to play real
> online multiplayer, **remove `Mods\BABalanceMod.dll` first**. As a safety net the mod also refuses to apply
> any overrides if it detects EasyAntiCheat loaded. It's a fan-made mod, not affiliated with or endorsed by the
> developers or publisher of Broken Arrow.

---

## Install (the easy way)

1. Click the green **Code** button above and choose **Download ZIP**, then unzip it anywhere.
2. Double-click **`install.bat`**.

That's it. The installer will:

- find your Broken Arrow install automatically (Steam libraries and common paths; it asks if it can't),
- download and install **MelonLoader v0.7.3** into the game folder for you if it isn't already there,
- copy the mod into `Mods\`,
- create `_BAMod\overrides.json` (the file you edit) if you don't already have one,
- write `steam_appid.txt` + a Steam-aware launcher (so the game doesn't hang at "Loading Hangar"),
- and add a **"Broken Arrow (Modded)"** desktop shortcut.

Do the same with the **[Local Skirmish mod](https://github.com/BovineOverlord/broken-arrow-local-skirmish)**,
which is also required. Then launch the game with the new desktop shortcut and you're modded.

> **Why a special shortcut?** Broken Arrow's normal Steam launch runs through EasyAntiCheat, which blocks
> mods. The shortcut starts the game with anti-cheat off (required for any MelonLoader mod to load) but still
> with a Steam context — the installer writes `steam_appid.txt` and the launcher makes sure the Steam client
> is running first. Without that context the game hangs at the "Loading Hangar" screen, so **keep Steam running
> and signed in** when you launch modded. The first modded launch is slower because MelonLoader sets itself up
> once.

## How to change stats

1. Open **`_BAMod\overrides.json`** (created by the installer, in your game folder).
2. Add or edit entries under `units` or `ammunitions`.
3. Launch modded, and your values are in effect.

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
- After the first launch, look in **`_BAMod\dump\`** for `Units_full.json` and `Ammunitions_full.json`: full
  exports of every unit and ammo with their ids, names and current values, so you know exactly what to copy
  and what number you're changing.
- To confirm it worked, open `MelonLoader\Latest.log` and look for a line like
  `applied overrides: units=N ammo=M`.

### Fields you can change

**Per unit** (`units`): `Cost` (arsenal price), `HP` (max health), `Stealth`, and armour: `ArmorValue`,
`KinF/KinS/KinR/KinT` (kinetic front/side/rear/top), `HeatF/HeatS/HeatR/HeatT` (HEAT front/side/rear/top).

**Per ammunition** (`ammunitions`): `Damage`, `PenMin` / `PenGround` (penetration at min / ground range),
`SupplyCost`, `ResupplyTime`.

More fields (weapon reload, mobility, sensors, abilities) can be added. See [docs/MODDING.md](docs/MODDING.md).

### What it can't do

- **Affect online multiplayer.** Offline only, by design.
- **Add new units or models.** It changes numbers on existing entities, it doesn't add content. If there's
  a lot of interest, I'll expand the mod to support this.
- **Change things that aren't per-unit.** A few tunables (for example global sprint cooldowns) aren't wired
  into `overrides.json` yet.
- The mod has to stay installed for your changes to apply. Remove it and the game is back to stock (that's a
  feature: it's non-destructive).

## Uninstall

Delete `Mods\BABalanceMod.dll`. The game is immediately back to stock. To remove the loader entirely, delete
`version.dll` and the `MelonLoader\` folder from the game directory.

## Build it yourself / extend it

How to compile from source and add new fields is in **[docs/MODDING.md](docs/MODDING.md)**.

## License

[MIT](LICENSE), 2026 BovineOverlord. Provided as-is, with no warranty. Use at your own risk; you are
responsible for complying with the game's own terms of service.
