# Phil's Reusability Suite

> **Written by an AI.** Every line of code in this mod was written by Claude (Anthropic's AI model).
> Phil Nutt designs it and play-tests it. He isn't a programmer, and no human has reviewed the code.
> Back up your saves before trying it.

A Kerbal Space Program 1.12 career mod that makes ships things you build, fly, bring home and fly again.

**Status: pre-release (0.1.0).** Everything below works and is tested in game by automated tests, but
it hasn't had much real play yet. Back up your saves. The design is in [DESIGN.md](DESIGN.md).

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
- **A flown booster under a new payload.** Phil's Builds' library window lists the hangar copies under
  each launcher. Pick a flown first stage and the whole launcher goes on the cursor: that exact stage plus
  a new upper stage from the design. Attach it under your payload. Only the new parts cost full price and
  full build time.
- **Built from designs.** Only ships built from a design filed in Phil's Builds come home to the hangar.
  Pieces of anything else go to the parts bin, and the Build panel warns you first. If a design you load
  already has a ready copy in the hangar, the Build panel offers to use it.

Every part has a serial number and a flight count, so a booster comes home as itself: "Oak 005 #4, 5
flights".

## Requirements

- KSP 1.12.x
- ModuleManager
- Harmony 2 (000_Harmony)
- [Phil's Builds](https://github.com/phillipnutt/phils-builds) (required: it's where designs are filed). Phil's Builds works fine without
  this mod.

StageRecovery and ScrapYard do the same jobs, so remove them. While StageRecovery is installed, the suite
leaves stage recovery to it.

It's made for career games. In sandbox and science games there's no money: builds just take time.

## Install

Copy `GameData/PhilsReusabilitySuite` from the release zip into your KSP `GameData` folder, alongside
ModuleManager, Harmony and Phil's Builds. It works in a new game or an existing one. Ships launched before
it was installed have no launch record, so when they come home their parts go to the parts bin rather
than the hangar.

## Settings

Every number is in `GameData/PhilsReusabilitySuite/Settings.cfg`: the build-time curve, overtime and
outsourcing prices, refit prices, scrap value, build lines per level, and `requireDesign` (set it to
`false` to let unfiled ships come home to the hangar too).

## Building

`msbuild PhilsReusabilitySuite.csproj /p:KspDir=<your KSP folder>` builds the DLL into
`GameData/PhilsReusabilitySuite/Plugins`. Add `/p:InstallToGame=true` to copy the mod into that KSP.
`Tools/Test-Core.ps1 -Ksp <your KSP folder>` runs the logic checks against KSP's stock craft.

## License

MIT. See [LICENSE](LICENSE).
