# Regen Assistant

A [Dalamud](https://dalamud.dev) plugin for FFXIV that telegraphs what your
heal-over-time effects are still going to do: next to each player's HP on the
party list it shows the **estimated HP the active regens will still restore**
and **how long until they finish**, plus an optional **square gauge** that
drains as the longest regen runs out.

Built from the [goatcorp SamplePlugin](https://github.com/goatcorp/SamplePlugin)
template (Dalamud.NET.Sdk 15 / API level 15).

## Features

* **Party list overlay** — anchored to each member's HP gauge on the native
  party list (`_PartyList`), in HUD order, including the solo frame:
  * `+12.3k 14s` — estimated remaining healing and time to completion
  * a square gauge that fills proportionally to the remaining regen time
* **Party Regen Monitor** — a standalone window listing every member's HP,
  total estimated regen, and a countdown bar; hover the heal amount for a
  per-status breakdown.
* **Tracked effects** — Regen, Medica II/III, Asylum, Aspected Benefic/Helios,
  Helios Conjunction, Wheel of Fortune, Whispering Dawn, Angel's Whisper,
  Fey Union, Sacred Soil, Seraphism, Kerakeia, Physis I/II, Aurora.
  Status ids are resolved from the game's Status sheet **by name at load
  time**, so they survive game patches. You can disable any built-in or add
  your own `status id → potency/tick` entries in the settings.

## How the estimate works

HoTs tick once per actor tick (every 3 s). The plugin computes
`remaining ticks × potency per tick × HP-per-potency`. Actual healing scales
with the *caster's* stats, which are not visible for other players, so
**HP-per-potency is a calibration slider** (default 35): divide an observed
tick amount by the tick's potency and enter the result. Optionally the
estimate can be capped at the target's missing HP.

## Commands

| Command | Effect |
| --- | --- |
| `/regenassist` | Toggle the Party Regen Monitor window |
| `/regenassist config` | Open settings |
| `/regenassist overlay` | Toggle the party list overlay |

## Building

### Prerequisites

* XIVLauncher, FINAL FANTASY XIV, and Dalamud installed and run at least once
  (or a Dalamud dev distribution extracted somewhere and pointed to with the
  `DALAMUD_HOME` environment variable).
* .NET 10 SDK.

### Steps

1. Open `RegenAssistant.sln` in Visual Studio / Rider, or run `dotnet build`.
2. The built plugin lands at `RegenAssistant/bin/x64/Debug/RegenAssistant.dll`
   (`Release` builds also produce a distribution-ready
   `RegenAssistant/bin/x64/Release/RegenAssistant/latest.zip`).

Building on Linux/CI works with
`dotnet build -c Release -p:EnableWindowsTargeting=true` and `DALAMUD_HOME`
pointing at an extracted [dalamud-distrib](https://github.com/goatcorp/dalamud-distrib).

### Activating in-game

1. `/xlsettings` → `Experimental` → add the full path to
   `RegenAssistant.dll` to Dev Plugin Locations.
2. `/xlplugins` → `Dev Tools` → `Installed Dev Plugins` → enable
   `Regen Assistant`.
3. `/regenassist` to open the monitor; the overlay is on by default.

## Notes & limitations

* Healing amounts are **estimates** (see calibration above); crit HoT ticks
  and healing-received buffs are not modeled.
* Cross-world party members who are not in the current zone have no readable
  status list and are skipped.
* Trust/NPC party members are not yet tracked.

## License

AGPL-3.0-or-later, following the SamplePlugin template.

This plugin was developed with AI assistance (Claude); per the
[Dalamud AI usage policy](https://dalamud.dev/plugin-publishing/ai-policy),
disclose this if submitting to the official plugin repository.
