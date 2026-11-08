# Changelog

## Unreleased

From Phil's first play-test, and work on the night after.

New:
- **Build from Phil's Builds without the VAB.** The Build queue tab has "Build from Phil's Builds…": every filed
  launcher, upper stage and craft with its price and build time (Normal, Overtime or Outsource). Anything that
  can't be built yet says why: a part not researched or not bought, too many parts for the building, too heavy
  or too big for the launch pad or runway.
- **Restore to design, from the hangar.** A flown copy of a filed design (a first stage that came home, a ship
  that lost a fin) has "Restore to <design>" in the Hangar tab: its own parts plus new ones (or matching bin
  parts) for everything it's missing, priced and queued without the VAB. It stays the same ship, with the same
  name, number and flight log, and is ready to launch when it's done.
- **Each dropping stage says at once whether it will come home**: on its chutes, with a landing burn, hard, or
  not at all. Stages dropping together make one message ("6 × … won't survive the landing").

Fixed:
- Stage recovery now works while you're at the Space Center or the Tracking Station. A ship left falling there
  used to vanish with no landing check and no message.
- A launcher built on its own was called "Untitled Space Craft". A ship that is all one design is now built
  under the design's name; a payload stacked on a launcher takes the payload's name.
- A payload stacked on a launcher was listed under the launcher in Phil's Builds as one of its copies. A ship
  counts as a copy of a design only if every part placed from a design came from that one.
- A hangar ship opened in the VAB lost its hangar identity after a trip to the Space Center and back.
- The VAB's landing check gave different speeds each time and measured each piece as if still stacked in the
  ship. It now measures each piece on its own, as it will fall, and leaves out launch clamps (they stay on the
  pad).
- KSP's pre-launch warnings during a build say "Build anyway" instead of "Launch anyway".
- Messages say why a piece went to the parts bin.

## 0.1.0 (pre-release)

First release, for testing. Back up your saves.

- A build queue in place of instant launches. The green Launch button opens the Build panel: each piece
  of the ship, what it costs and how long it takes. Overtime builds twice as fast; Outsourcing finishes
  at once. One build line per VAB or SPH level.
- A hangar. Finished builds wait there, ready, and launch at no extra cost. Recovered ships come back
  exactly as they landed and fly again after a refit: 10% of a part's price plus 1% per flight.
- A Recover prompt on all three of KSP's Recover buttons: To the hangar (pay the transport home), Sell for
  scrap (half its value), or Cancel.
- Stage recovery. Stages that fall out of range come home if they'd land safely on parachutes or with a
  landing burn. Hard landings break off outer parts. The VAB shows each dropping piece's landing speed.
  The suite leaves stage recovery to StageRecovery while it's installed.
- A parts bin. Loose parts are used automatically in new builds at refit price.
- Every part has a serial number and a flight count, and every hangar ship has a flight log.
- The link with Phil's Builds: hangar copies listed under each design in its library window; picking a
  flown first stage puts the whole launcher on the cursor (that stage plus a new upper stage); "Use it?"
  for a ready copy of the design in the editor; a "Not filed in Phil's Builds" warning. Pieces of unfiled
  ships go to the parts bin (`requireDesign` in Settings.cfg).
- Every number in `GameData/PhilsReusabilitySuite/Settings.cfg`.
- Nothing is recorded during KRASH simulations. Recoveries are reported the stock way, so other mods
  (such as Phil's Iterative Parts) count them.
