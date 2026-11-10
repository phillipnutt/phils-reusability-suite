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
  back to the hangar exactly as they landed, a lost fin and all. Launching a ship with seats lets you pick its
  crew first, starting from the crew KSP would pick.
- **Wear and refurbishing.** Each craft has one wear number, from how hard its flights were: top speed, how
  close its hottest part came to its heat limit, and G. Fly it again as it is (it's only refuelled), or
  **refurbish** it back to 0% wear whenever you like: the price follows its wear, up to half the price of new.
  At 100% it must be refurbished first. Wear does nothing else: no failures. A ship that came back short of a
  part can be **restored** from the hangar, from its own blueprint: its own parts plus new ones for whatever it
  lost, and it stays the same ship.
- **Recovery that asks.** The Recover button offers **To the hangar** (you pay the transport home, more the
  farther away it landed) or **Sell for scrap** (half of what it's worth where it landed). Cancel leaves it where it
  is.
- **Stage recovery.** Stages that fall out of range come home on their own if they would land safely, on
  parachutes or with a landing burn: to the parts bin, or to the hangar if it's the ship itself. Landing between 6
  and 12 m/s breaks off outer parts; faster than that, the stage is lost. The VAB shows each dropping piece's
  landing speed while you design it, and in flight each stage says as it drops whether it will come home. It works
  while you watch from the Space Center or the Tracking Station too.
- **A parts bin.** Loose parts, from ships you take apart, launchers that come home, and anything else that comes
  home without its ship, kept as a count of each kind of part. They're refurbished (and emptied of fuel) as they go
  in, so new builds use them in place of new parts at no cost but their fuel, and the Build panel lists which ones.
- **Phil's Iterative Parts.** New parts are always their program's current Mk, whether you build in the VAB or
  from the Space Center. Flown parts keep the Mk they were built at until you press **Update PIP parts** (in the
  parts bin or on a hangar ship). A build that could use an old-Mk bin part asks whether to update it first, or
  builds that part new.
- **Build ahead from the Space Center.** "Build from Phil's Builds…" in the Build queue tab queues any filed
  craft without opening the VAB or SPH, and says why if it can't be built yet.
- **Launchers are parts.** The ship is what you saved as the ship. Put a Phil's Builds launcher under it in the VAB
  and that launcher isn't the ship: it has no name or log, it comes home to the parts bin, and the next launcher
  you build takes its parts from there. Saved together with its booster, the booster is the ship's own, and Restore
  builds it again. A launcher on its own can't be built: put a ship on it.
- **Built from designs.** Only ships built from a design filed in Phil's Builds come home to the hangar.
  Anything else goes to the parts bin, and the Build panel warns you first. Loading a design is always a new build:
  open a built ship from the hangar, or from Phil's Builds' hangar list.

Every launch records the ship that went up in it and which parts are its own. The piece with its pod or probe core
comes home as that ship: "Peleus III #2, 5 flights". A ship stays itself however it is rebuilt (open it in the VAB,
change it, even save it as another design), and keeps its own blueprint for Restore.

## Requirements

- KSP 1.12.x
- ModuleManager
- Harmony 2 (000_Harmony)
- [Phil's Builds](https://github.com/phillipnutt/phils-builds) (required: it's where designs are filed). Phil's Builds works fine without
  this mod. Its newest version also gives the suite's windows their pictures.
- [Phil's Iterative Parts](https://github.com/phillipnutt/phils-iterative-parts) is optional. With it, the suite keeps
  track of each part's Mk.

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
outsourcing prices, the wear rule and refurbish price, scrap value, build lines per level, and `requireDesign` (set it to
`false` to let unfiled ships come home to the hangar too).

## Building

`msbuild PhilsReusabilitySuite.csproj /p:KspDir=<your KSP folder>` builds the DLL into
`GameData/PhilsReusabilitySuite/Plugins`. Add `/p:InstallToGame=true` to copy the mod into that KSP.
`Tools/Test-Core.ps1 -Ksp <your KSP folder>` runs the logic checks against KSP's stock craft.

## License

MIT. See [LICENSE](LICENSE).
