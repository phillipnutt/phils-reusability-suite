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
- **The VAB's landing check says how much propellant to keep** for a stage that can make a landing burn (it can
  steer and has liquid engines): "lands with a burn if it keeps 0.25 t of propellant". It says so if its engines
  are too weak to hold it up.
- **Each dropping stage says at once whether it will come home**: on its chutes, with a landing burn, hard, or
  not at all. Stages dropping together make one message ("6 × … won't survive the landing").

Simplified (Phil: "more complexity and overlapping stuff... has more ways in which it can fail"). Three questions
that were each answered in several places now have one rule each, and the code is about 100 lines shorter:
- **Is this VAB part real hangar hardware?** One rule: a part keeps its serial if that serial belongs to nothing
  else, or if the suite itself put that hardware in the VAB. This replaces three lists and a timing flag. It also
  closes a gap: after KSP's New button, a design saved from a hangar ship's parts could still take that ship.
- **What did a piece lose on the way?** Worked out once, when it comes home, from the ship as it launched: a part
  that didn't come home was lost unless it left at a decoupler, docking port, claw or launch clamp. This replaces
  following KSP's joint-break, decouple and destruction events through the flight (and their timing rules). One
  small change: a decoupler whose joint snaps instead of firing now counts as a stage leaving, not a part lost.
- **What is a returning piece called?** One rule, used both when it comes home and by the flight forecast.

From Phil's third play-test (Gallant I, the afternoon after):
- **Wear and Refurbish.** Each craft has one wear number, 0 to 100%, from how hard its flights were: top speed,
  how close its hottest part came to its heat limit, and G. It shows in the hangar, the Build panel and Phil's
  Builds. Refurbishing (it was "Refit") is optional now: launch a hangar ship again as it is (it's refuelled,
  which is all it pays), or refurbish it back to 0% whenever you like. The refurbish price follows the wear:
  the craft's price × its wear × half (20% wear costs about what a refit did; 100% costs half the price of
  new). At 100% it must be refurbished before it flies again. Wear does nothing else: no failures. Any build
  refurbishes the flown hardware in it, Restore included, so the built craft leaves at 0%. This replaces the
  refit rule (10% plus 1% per flight) and refitting before every reuse. The numbers are in Settings.cfg.
- **The hangar panel's buttons stay at the bottom** and the flight log scrolls: with five flights in the log,
  "Open in the VAB" couldn't be reached.
- **"Gallant I #1 #1".** A ship opened from the hangar and built in the VAB now stays that ship (Gallant I #1).
  Refurbish and restore builds are called by the ship's own name in the queue and the log ("Gallant I #1", not
  "Gallant I"), so two copies can be told apart.
- **"Open in the VAB" from the Space Center** goes to the ship's building and opens it there. Phil's Builds
  offers it beside Load, as "Load as stored".
- **Fuel against what it launched with:** "fuel 100% of launch load" for a booster loaded to 30% on purpose that
  brought all of it back (it read "fuel 31%").
- **The Build panel's breakdown is a list,** not cards to pick from: "8 flown parts from Gallant I #1",
  "7 new parts".
- The Recover prompt's scrap line says what the sum does: half of what it's worth where it landed.

From Phil's second play-test (the morning after):
- **Building a design could quietly use up a hangar ship.** Craft files saved in the VAB keep their parts'
  serial numbers, so a design saved from parts that were later built (Harris Hawk II saved from the I's parts)
  brought that hangar ship's hardware back in when loaded in the same VAB visit, and the build took the ship.
  A design loaded, merged or picked up from a file now always comes in as new parts. Only the suite's own
  hangar actions (Load from the hangar, "Use it", editing a queued build, picking a hangar copy in Phil's
  Builds) bring real hardware into the editor.
- **A ship built from hangar ships keeps the top ship's identity.** A ready Argo I #1 stacked on a ready
  Rockall 002 #1 is built as Argo I #1, with its flight log, not a new Argo I #2. Hangar cards say what a ship
  is stacked on: "on Rockall 002 #1" (that ship's own hardware) or "on Rockall 002" (built new with it).
- The landing check calls each piece a stage ("Stage with LV-909 Terrier"), and a heat shield is no longer a
  piece of its own: its jettison isn't in the staging unless you turn it on, so it stays with its pod.
- Letters that hang below the line (g, p, q, y, commas) were cut off in the suite's windows: "Argo" read "Arao".
- "1 parts" now reads "1 part".
- Moving or deleting parts in the VAB no longer writes "destroyed with the rest of its ship" to the log.

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
- Coming back to the VAB after a launch, KSP brings back the ship just launched: its parts now get new serial
  numbers unless they're hangar hardware, instead of sharing serials with the ship still flying.
- A build can't be started from an old price: if a bin part or hangar ship it uses was sold, taken apart or put
  in another build since it was priced, the build is refused (nothing charged) and priced again.
- After building with parts from the bin, the ship left in the VAB no longer matched its build: changing it and
  pressing Build started a second build at full price. It now takes the bin parts' serials and stays the build.
- One bin part could stand in for two parts of a build (two parts with the same serial). A bin part already in the
  ship is no longer matched again.
- "Update build" on a build that had just been finished or cancelled reserved parts for a build that no longer
  existed. It's refused and the panel prices the ship again.
- A multi-mode engine (a RAPIER) counted its thrust once per mode when judging a landing burn.
- Going back to a ship still on the pad (or quickloading it) counted another flight on every part each time.
- A saved craft launched from KSP's own launch-pad building could carry the serials of hangar ships, bin parts or
  ships still flying; those parts now get new serials.
- A ready hangar ship couldn't be launched from the VAB or SPH when funds were below its full price (KSP's own
  "can you afford it" check). It's already paid for, so it now launches.
- "Bring it home" on a ship with no launch record took KSP's refund and gave back bin parts that couldn't be
  used or sold. Bin parts from such recoveries now keep what they need to be matched and valued.
- In the Tracking Station, picking another ship in the list while the Recover prompt was open recovered that one
  instead. The prompt now always recovers the ship it was asked about.
- Pieces of one ship that came home separately all became the same hangar ship. Now the piece holding the launched
  ship's root part comes home as that ship, with its name, number and flight log. A piece that came off it is a
  ship of its own: named after its design if it's a copy of one ("Kopje 005 #2"), or the next number otherwise.
  Reused hardware with an identity of its own (a flown "Kopje 005 #1" under a new payload) keeps that identity.
- The VAB's launch-site list (the slide-out by the green button) launched past the Build panel; it now opens the
  Build panel unless the ship is a ready hangar ship.
- A dropped stage crashing could count its top part as broken off, so the piece it came from showed parts
  missing. A destroyed part now only counts as lost if its ship survives it (a fin lost on landing). Launch
  clamps letting go at launch no longer count as broken off.
- Settings.cfg accepts 0 for overtime and outsourcing prices, the refurbish share and scrap value.
- "Back to the queue" in the library list threw an error; the "Take apart" and "Sell for scrap" links showed
  "<u>" tags; landing-check lines were cut off when they wrapped; prices in the Build panel's piece list lost the
  tail of their comma, so 16,303 read as "16.303".
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
