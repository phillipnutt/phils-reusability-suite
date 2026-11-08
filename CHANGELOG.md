# Changelog

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
