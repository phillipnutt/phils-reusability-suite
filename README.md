# Phil's Reusability Suite

> **Written by an AI.** Every line of code in this mod was written by Claude (Anthropic's AI model).
> Phil Nutt designs it and play-tests it. He isn't a programmer, and no human has reviewed the code.
> Back up your saves before trying it.

A Kerbal Space Program 1.12 career mod that makes ships things you build, fly, bring home and fly again.

**Status: in development.** It works and is tested in game, but it isn't released. The design is in
[DESIGN.md](DESIGN.md).

## What it does

- **A build queue, and nothing else.** Press the green Launch button and you get the Build panel instead:
  every piece of the ship on its own line, what it costs, and how long it takes. Build time follows the
  price of the new parts: about a day for a small ship, ten for a big one. Pay for **Overtime** to build
  twice as fast, or **Outsource** to have it now. Each VAB or SPH level gives one build line. No facility
  upgrade times, no research times, no upgrade points.
- **A hangar.** Finished builds wait in the hangar, ready, and launch at no extra cost. Recovered ships come
  back to the hangar exactly as they landed, a lost fin and all, and fly again after a cheap refit.
  Refitting costs 10% of a part's price, plus 1% for every flight it has made.
- **Recovery that asks.** The Recover button offers **To the hangar** (you pay the transport home, more the
  farther away it landed) or **Sell for scrap** (half its price, less the haul). Cancel leaves it where it
  is.
- **Stage recovery.** Stages that fall out of range come home on their own if they would land safely, on
  parachutes or with a landing burn. Landing between 6 and 12 m/s breaks off outer parts; faster than that,
  the stage is lost. The VAB shows each dropping piece's landing speed while you design it.
- **A parts bin.** Loose parts, from ships you take apart or pieces with no engine or probe core, are used
  automatically in new builds at refit price.
- **A flown booster under a new payload.** Put a stored first stage under a new payload in the VAB: only
  the new parts cost full price and full build time.

Every part has a serial number and a flight count, so a booster comes home as itself: "Oak 005 #4, 5
flights".

## Requirements

- KSP 1.12.x
- ModuleManager
- Harmony 2 (000_Harmony)
- Phil's Builds (for the launcher library link)

StageRecovery and ScrapYard do the same jobs, so remove them. While StageRecovery is installed, the suite
leaves stage recovery to it.

## Settings

Every number is in `GameData/PhilsReusabilitySuite/Settings.cfg`: the build-time curve, overtime and
outsourcing prices, refit prices, scrap value and build lines per level.

## Building

`msbuild PhilsReusabilitySuite.csproj /p:KspDir=<your KSP folder>` builds the DLL into
`GameData/PhilsReusabilitySuite/Plugins`. Add `/p:InstallToGame=true` to copy the mod into that KSP.
`Tools/Test-Core.ps1 -Ksp <your KSP folder>` runs the logic checks against KSP's stock craft.

## License

MIT. See [LICENSE](LICENSE).
