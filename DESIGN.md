# Phil's Reusability Suite: design

> Written by Claude (an AI). Phil designs and play-tests; he isn't a programmer.
> Design agreed with Phil on 2026-11-07, mockup included. Nothing is built yet: code starts only when
> Phil says go.

A KSP 1.12 career mod that makes ships things you build, fly, bring home and fly again. It adds a
build queue, a hangar for recovered ships, stage recovery into the hangar, part reuse, and a flown
booster under a newly built payload.

## The idea (Phil, 2026-11-07)

1. **A build queue, and nothing else.** Ships take time to build, scaled by size: about 1 day for a
   small ship, about 10 for a big one. Pay extra to rush a build, or a lot to skip the wait.
2. **Hangar storage.** A recovered ship goes into storage. It can be relaunched exactly as it was,
   after a short, cheap refurbishment.
3. **Stage recovery.** Dropped boosters that would survive their landing come back, ideally into the
   hangar, not just as a refund.
4. **Part reuse.** Recovered parts make new builds cheaper and faster.
5. **A flown booster under a new payload.** In the VAB, pick a stored booster and build a new payload on
   top. Only the new parts cost full money and build time; the booster only needs refurbishing. Phil:
   "combining a used but refueled launcher with a newly built payload is pretty outside of the scope of
   vanilla KSP... I really like that shit though."

**Not wanted.** These are why Kerbal Construction Time "did too much":
- facility upgrade times;
- research or tech-unlock times;
- experience or upgrade points for R&D or build speed.

## Agreed

1. **Two mods: the suite needs Phil's Builds, but Phil's Builds doesn't need the suite** (2026-11-07).
   - Phil's Builds works fully on its own.
   - With the suite installed, Phil's Builds shows hangar counts on its designs and launchers, for
     example "Oak 030 · 2 in hangar".
   - The suite uses Phil's Builds' families, marks and launcher ratings instead of keeping its own.
   - The hand-off between the two is agreed with Phil's Builds' own development, and the hook is added
     there.
2. **Rush is Overtime, and instant is Outsourcing** (Phil, 2026-11-07). In roleplay terms:
   - Overtime: paying more to build faster means overtime and higher wages.
   - Outsourcing: finishing at once means buying the build from another agency.
   - Prices are still to be set (see the build-time question).
3. **The suite replaces both StageRecovery and ScrapYard** (Phil, 2026-11-07: "I believe both").
   - **Stage recovery uses StageRecovery's rules and limits,** so it feels the same: parachute drag and
     mass give the landing speed, with full value under 6 m/s and nothing over 12 m/s. A powered landing
     needs fuel with a 1.5× margin. Very fast stages get a burn-up check.
   - **Stages are measured while still close by,** which avoids StageRecovery's TweakScale bug.
   - **The VAB shows whether a stage will come home,** and at what speed, while you design it.
   - **Switch-over:** while StageRecovery is installed, the suite leaves stage recovery to it, so the two
     never act on the same stage. When the suite's recovery works, StageRecovery leaves the pack.
   - **The suite tracks parts itself.** It warns if ScrapYard is installed alongside.
4. **Whole ships on top of simple part accounting** (Phil, 2026-11-07: "Brilliant. Let's go with this
   idea."). He'd asked how to keep "I have flown this exact booster" without the mess of editing stored
   craft:
   - **Price and build time are counted part by part:** parts you already own cost their refurbishment; new
     parts cost full price and full build time. This is ScrapYard's simplicity underneath. (Until round 4 every
     part carried a serial number and a flight count; since then a ship's identity is the suite's own record,
     not something kept on its parts: see "Ship identity, rebuilt" below.)
   - **The hangar keeps whole ships, exactly as they came back.** A grid fin that broke off is gone. Each
     ship has a number and a log, for example "Oak 030 #3, 4 flights".
   - **Stored ships aren't locked.** Open one in the VAB and change anything. Added parts are new parts,
     and removed parts go to the parts bin. No full rebuild is ever needed.
   - **The design is the blueprint; the hangar ship is a physical copy.** The hangar shows any difference
     from the design ("missing 1 grid fin"). "Restore to design" adds the missing parts as new ones.
   - **The parts bin** holds loose parts from ships you strip or take apart. New builds use matching bin
     parts automatically, as ScrapYard did.
   - **How a recovered ship becomes loadable again:** the suite remembers each launch exactly as it left
     the pad. At recovery it crosses off the parts that didn't come back. Parts the launch record can't
     place (docked or claw-grabbed objects, parts added on EVA) go to the parts bin as loose parts. The
     hardware is never lost; only those bits lose the "same ship" identity.
   - **What a returning piece is compared against:** its own side of the separation in the launch
     record. A payload you released through a decoupler or docking port isn't "missing". A fin that
     broke off is. The launch record also lists the ships that went up in it ("Oak 030 #3" under a new
     payload), so that booster comes home as itself, one flight older.

5. **Refuelling is automatic** (Phil, 2026-11-07: "if you think it works and it's not too hacky let's
   do it"). He sets spaceplane tanks to chosen levels on purpose, so:
   - **The launch record that crosses off missing parts also holds every tank's level.** "Refuel" means
     "back to how it left the pad".
   - **The hangar keeps the ship as it came back,** for example "fuel 48%".
   - **On relaunch,** every tank goes back to its level at the last launch. You pay for the missing fuel
     at normal prices. Anything above that level, such as ore brought home or Kerbalism waste, is sold
     back or emptied.
   - **For different levels,** open the ship in the VAB and set the sliders, as for any build.
   - **The rest of the refit is automatic too:** parachutes repacked, heat-shield ablator refilled,
     Kerbalism supplies topped up, and science experiments emptied (their science was credited at
     recovery).
6. **Build time** (Phil, 2026-11-07: "everything you ideated is perfect"):
   - **What sets it:** the price of the new parts, without fuel. Price already reflects how complex a part
     is. Mass would make big cheap tanks slow and small expensive probes fast. Part count punishes struts
     and small parts.
   - **Reused parts** add only their refurbishment time (see refurbishment below).
   - **Formula:** days = (price ÷ 20,000) ^ 0.8, in Kerbin days, the 6-hour days the game clock shows.
     Big ships build a little faster per fund than small ones, so a late-game 1M-funds ship takes about
     23 days, not 50.
   - **Examples** from Phil's own ships (prices include fuel, so they're rough):

     | Ship | Price | Build | Overtime | Outsourcing |
     |---|---|---|---|---|
     | Retard 5000 | 11k | 0.6 d | 0.3 d, +2.8k | now, +11k |
     | Comsat I | 47k | 2.0 d | 1.0 d, +12k | now, +47k |
     | Evergreen I | 116k | 4.1 d | 2.0 d, +29k | now, +116k |
     | DESU - Drive | 209k | 6.5 d | 3.3 d, +52k | now, +209k |
     | DESU - Full | 301k | 8.7 d | 4.4 d, +75k | now, +301k |
     | late-game ship | 1M | 22.9 d | 11.4 d, +250k | now, +1M |

   - **Overtime** builds twice as fast, for 25% more than the build's price. Switched on partway through,
     it costs 25% of the share still to build.
   - **Outsourcing** finishes the build at once, for 100% of the share still to build: twice the price
     for a fresh build, much less for one nearly done.
   - **How many at once:** each building (VAB, SPH) works on one ship at a time at level 1, two at level
     2 and three at level 3. This uses the stock facility upgrades Phil already pays for, with no new
     points or upgrade times. Other ships wait in line.

7. **Refurbishment and wear** (Phil, 2026-11-07: "Yeah this is all money"):
   - **Recovering no longer pays cash; you keep the hardware.** Stock refunds up to 98% of a ship's value
     on recovery. With the suite, the ship goes to the hangar instead. For cash, sell it for scrap.
   - **What goes where:**
     - A recovered piece that can fly goes to the hangar as a whole ship. "Can fly" means it has an
       engine, or a pod or probe core.
     - Anything else goes straight to the parts bin, for example a parachuted interstage. Stock
       fairing halves aren't parts, so they never come back.
   - **Transport home** costs what stock's distance rule would have kept back. That's about 2% of the
     piece's value at KSC, about 16% from 300 km downrange, and up to 90% on the far side of Kerbin.
     It's charged at recovery, and it rewards landing back at KSC or on the runway.
   - **Refurbishment price** (until 2026-11-08 the "refit": 10% of each part's price plus 1% per flight,
     required before every reuse). Now it follows **wear**, below.
   - **Wear and refurbishing (Phil, 2026-11-08, approved as the starting model, "Yes, start there"; tune in
     play):**
     - **One wear number per craft** (not per part), 0 to 100%, shown in the suite and in Phil's Builds.
     - **Refurbishing is optional:** refurbish any time, or launch again as it is (it's only refuelled). At
       **100% a refurbish is forced** (Phil chose that over forced retirement as simplest).
     - **No failures or other penalties** from wear, and it stays out of PIP ("way too messy").
     - **What a flight adds:** while a craft is loaded, each part keeps the flight's peaks: surface speed, the
       hottest part as a share of its own limit (the worse of skin and core), and G (KSP's smoothed
       `vessel.geeForce`). At recovery: speed 10% × peak ÷ 2,500 m/s (at most 10%); heat 0 below half the
       limit, rising to 30% at the limit; G 0 below 3 G, rising to 20% at 10 G. Roughly: a pad hop about 1%, a
       short suborbital hop 2 to 5%, orbit and a normal re-entry 20 to 25%, a near-melting steep re-entry 50%.
     - **Stages that land unloaded** (stage recovery) have no readings for the fall: they bring home the peaks
       they had when they left physics range, which is about their speed at separation.
     - **Refurbish price = the craft's part price × wear × ½** (20% wear is about the old 10% refit; 100% is
       half the price of new). Its time is the build-time formula, as for any build.
     - **Any build refurbishes the flown hardware in it:** Restore (the surviving parts too, so one new fin
       doesn't "reset" a worn craft and new parts don't count as worn), a flown booster under a new payload, a
       hangar ship built on in the VAB. The built craft leaves at 0%.
   - **Refurbishment time:** the build-time formula, applied to the refurbishment price plus any new
     parts.
   - **Hard landings, 6 to 12 m/s:** outer parts with nothing attached to them can break off: fins,
     antennas, panels, legs. None break at 6 m/s, about half at 9 m/s and all of them at 12 m/s. Over
     12 m/s the whole piece is lost, as in StageRecovery.
   - **Selling:** hangar ships and bin parts sell for scrap at 50% of their price.
   - **Examples:**

     | Case | Refit | Transport | Time |
     |---|---|---|---|
     | Magnolia Mk I SSTO (28.5k), 2nd flight, landed on the runway | 3.1k | 0.6k | about 1.4 hours |
     | 150k booster, 5th flight, landed 300 km downrange | 21k | 24k | about 1 day |
     | The same booster landed back at KSC | 21k | 3k | about 1 day |
     | A new 150k booster instead | 150k | none | 5 days |

   - **Stingier than stock, on purpose:** in stock, a ship recovered at KSC gives 98% back. Reuse in
     the suite is still 4 to 8 times cheaper than building new, and much faster.

## The Recover prompt (proposed; to confirm with the mockup)

**The Recover button asks first** (Phil, 2026-11-07, asked what happens to something useless landed
at the poles, and remembered KCT's prompt):
- **The stock Recover button stays.** In flight and in the Tracking Station, pressing it opens a small
  prompt with the money shown:
  - **To the hangar**, for the transport cost. This is picked first, so Enter does it.
  - **Sell for scrap:** half its price, less stock's distance loss. For example, at a pole about 950 km
    from KSC, you'd get about 27% of its price.
  - **Cancel.**
- **Not worth it? Don't recover it.** Go back to the Space Center and it stays where it landed, as in
  stock. You can recover it later from the Tracking Station.
- **Crew and science are recovered as in stock,** whichever choice you make.
- **Automatic stage recovery never asks.** Pieces that can fly go to the hangar; the rest go to the
  bin.

## Building and launching (agreed)

Phil, 2026-11-07: "I dig it bubba I dig it". This is question 5, the flown booster under a new
payload.
- **The green Launch button becomes Build.** It puts the ship in the build queue and charges for it
  then. KRASH simulations are unaffected, because they have their own button.
- **Finished ships go to the hangar,** "ready, 0 flights". You launch from the hangar: from the queue
  and hangar window, at the KSC or in the VAB/SPH. So you can build ahead, for example three Oak 030
  boosters waiting for payloads.
- **Payload first, booster underneath** (Phil: "the design flow is usually much more build the payload,
  and then slap that booster on the bottom of it"). This is the flow Phil's Builds already has for
  launchers:
  - Phil's Builds' Launchers tab lists launchers that can lift what's in the editor. Hangar copies come
    first, with flights, condition and refit price.
  - Clicking a hangar copy picks up that exact booster on the cursor, held by its top, where the last
    payload sat, ready to attach under the payload.
  - The suite's own hangar list in the VAB does the same for any stored ship.
  - A picked ship is reserved for this build and greyed out in the hangar.
  - Nothing is locked.
- **Combining ships.** The VAB can hold any mix of blueprints and hangar ships. Per-part pricing does
  the rest. Two ways to fly a comsat on a stored booster:
  - **All at once:** put the comsat design in the editor and attach the stored booster underneath. One
    build follows: the comsat's new parts take their day or two, and the booster's refit happens
    alongside. The booster is reserved for that whole time.
  - **Build ahead, stack later:** build the comsat on its own; it goes to the hangar "ready". Later, pick
    it from the hangar, attach the booster underneath and press Build. Only the booster's refit costs
    anything, so it's done in hours. This is useful when the booster is still out flying while the
    comsat is built, or to keep payloads waiting.
  - **Parts that haven't flown need no refit,** so a ready, unflown payload costs nothing to stack.
  - **No paying twice by accident:** if you load the Comsat II blueprint while a ready Comsat II is in
    the hangar, the price panel says so ("A ready Comsat II is in the hangar. Use it?"). One click swaps
    in the real one.
- **The price panel in the VAB** shows four lines:
  - new parts, at full price;
  - reused parts, at refit price;
  - fuel;
  - build time, with an Overtime switch.
  - Pointing at the "reused" line highlights those parts green.
- **One queue entry per build,** for example "Comsat II on Oak 030 #3". Its time is the build-time rule
  applied to new parts plus refit.
- **Editing a queued build keeps the work done.** The price difference is charged or refunded.
- **Cancelling a build** refunds it in full, and reused hardware goes back to the hangar.
- **Designs never carry specific hardware.** Saving the ship in the VAB saves a blueprint. Which
  booster is under it is known only to the build and the launch record.

## Two-stage launchers (agreed)

Phil, 2026-11-07: "I love this vision".

Phil, 2026-11-07: most of his launchers have two stages, a first stage and an orbital-insertion upper
stage. He wants to "design a 5 ton comsat and then slap a 5T to orbit launcher beneath it", without
building the upper stage himself every time.

- **Words:**
  - The *launcher* is everything under the payload, the Phil's Builds library item ("Oak 005").
  - The *first stage* usually comes home.
  - The *upper stage* usually doesn't.
- **The hangar holds what came home,** for example "Oak 005 #4, first stage, 1 flight".
- **Picking a launcher completes it.** For each launcher, the Launchers tab offers New, or a flown first
  stage ("#2, 3 flights", "#4, 1 flight").
  - Picking #4 puts the whole Oak 005 on the cursor: first stage #4 plus a fresh upper stage built from
    the design.
  - This is "Restore to design", done automatically.
- **Price and time:**
  - The first stage costs its refit.
  - The upper stage and the payload are new parts.
  - One build covers it all.
- **The general rule:** the suite fills a launcher from the hangar wherever it can, and builds new
  whatever's missing. This covers:
  - a lost grid fin;
  - radial boosters that land separately (each fills its own slot);
  - an upper stage that does come home.
- **How it knows what's what:** every part placed from a Phil's Builds design remembers its place in
  that design ("Oak 005, part 12"). The suite needs no stage tagging; it simply knows which parts of the
  design came home and which didn't.
- **Marks:** a first stage remembers its launcher's mark. A Mk I stage is completed with the Mk I upper
  stage. It also fits a newer mark if that mark's first stage is unchanged.
- **Build ahead:** a complete new launcher can be built on its own and wait in the hangar "ready", so
  stacking a payload on it is quick.
- **The Build panel lists each piece on its own line** (Phil: "that would just be so incredibly sick"):
  - "Oak 005 #4 · first stage · 1 flight": refit and fuel, plus whether it lands safely and at what
    speed.
  - "Oak 005 upper stage": new, its price and fuel.
  - "Comsat II": in the hangar, ready, no cost.
  - Parts from the bin, then the total, build time, and the Overtime switch.
- **What comes home needs a Phil's Builds design** (Phil's own rule: no design means "scrap it for
  parts"):
  - A returning piece built from a library design goes to the hangar. It gets a new number on its first
    return and keeps it after that.
  - Parts added by hand that come home attached to a hangar ship stay with that ship, so edits stick.
  - A returning piece with no design at all goes to the parts bin. Its parts still make the next build
    cheaper.
  - The Build panel warns before you build: "Not filed in Phil's Builds: anything that comes home goes
    to the parts bin". It has a button to file it there and then.
  - The Recover prompt shows "To the parts bin" in place of "To the hangar" for such a ship.

## Mockup v1 (approved by Phil, 2026-11-07: "Approve it")

The windows use PIP's look, like Phil's Part Hider and Phil's Builds. There are eight screens:

1. **Build panel, payload from the hangar.** Opens when you press Build in the VAB/SPH.
   - Each piece gets its own line: "Comsat II · in the hangar · no cost", "Oak 005 upper stage · new",
     and "Oak 005 #4 · first stage · reused · 1 flight", with the landing check ("comes home: 5.8 m/s
     on 3 chutes").
   - Below that come the totals (new parts, refit, payload from the hangar, fuel) and a pace choice:
     Normal, Overtime or Outsource, each with its time and extra cost.
   - The footer shows funds after the build and which build line it goes on.
2. **Build panel, new payload.** The same panel with a new Comsat II, and the "A ready Comsat II is in
   the hangar. Use it?" banner. Clicking Use it switches to the hangar copy.
3. **Phil's Builds Launchers tab with the suite installed.**
   - Each launcher with flown first stages lists New, #4 and #2 under it, with price and build time.
   - A "With flown copies" filter is added.
   - The side panel shows the chosen copy's cost breakdown and flight log. Picking #2 shows "missing 1
     grid fin", which is restored to the design.
4. **Suite window: Build queue.**
   - Builds are grouped by building and line, plus waiting builds.
   - Each build has Overtime, Outsource, Edit and Cancel.
   - A "Ready to launch" list has Launch buttons, and there's a game clock.
5. **Suite window: Hangar.**
   - Filters: All, Ready to fly, Launchers, Spaceplanes, Capsules, Payloads.
   - Reserved ships are greyed out, showing where they are ("Under Comsat II, VAB queue").
   - The side panel has the next flight's cost, the flight log, and buttons: Refit and launch, Open in
     the SPH, Take apart, Sell for scrap.
6. **Suite window: Parts bin.** Grouped parts with how many you have, flights, refit price and scrap
   price. Parts the VAB is using right now are marked. Sell selected, or sell all.
7. **Recover prompt:** To the hangar (−transport) or Sell for scrap (+value), with crew and science
   coming home either way. Cancel leaves the ship where it is.
8. **Stage recovery messages:**
   - came home;
   - came home hard, with lost fins and an antenna;
   - an interstage that went to the parts bin;
   - a stage lost at 23 m/s.

Small extras in the mockup, approved along with it:
- a "Warp to next finish" button in the queue;
- a flight log for each hangar ship;
- "Move up" for a waiting build;
- "Refit and launch", one button that queues the refit and makes the ship ready to launch.

The Recover prompt is approved with the mockup too.

## What the suite needs from Phil's Builds (to agree with that mod's own development)

Phil's Builds must keep working on its own. Everything below is optional on its side: it either reads
data or calls hooks that do nothing when the suite isn't installed.

1. **Read the library:** designs, families, marks, categories and launcher ratings (tons, width). The
   suite can reference Phil's Builds directly, because it requires it.
2. **"Design placed":** when Phil's Builds loads, merges or picks up a library item, it reports which
   design and mark each part came from, and its place in that design. The suite stamps the parts with
   that.
3. **"Design saved":** when a craft or subassembly is saved as a design or a new mark, it reports which
   parts were saved, so the suite can re-stamp them.
4. **Hangar copies in the library window:** an optional provider that Phil's Builds asks for:
   - the copies of a design (number, flights, condition, price, build time);
   - counts for the Craft and Launchers tabs;
   - the "With flown copies" filter.
5. **Picking a copy:** clicking a hangar copy hands it to the suite, which puts the whole launcher on
   the cursor: the flown first stage plus new parts from the design.
6. **"Is this filed?":** whether the craft in the editor belongs to a family, for the Build panel's
   "Not filed in Phil's Builds" warning.

## Build plan (proposed)

Each step is play-tested on the clean test install before the next. Every launch of KSP is asked for
first.

1. **Prove the hard part first.** Stamp parts with serials and keep a launch record. Recover a stage,
   cross off the parts that didn't come back, and load what's left in the VAB. KCT gave up on this, so
   it comes before anything else.
2. **Build queue:**
   - the Build button and Build panel;
   - build time, Overtime and Outsourcing;
   - charging once, at Build;
   - build lines per VAB/SPH level;
   - the queue window.
3. **Hangar:**
   - the Recover prompt;
   - storing ships;
   - Refit and launch, with refuel to launch levels;
   - the parts bin, with automatic matching;
   - Take apart and Sell for scrap.
4. **Stage recovery:**
   - the suite's own landing rules, measured while the stage is close by;
   - hard landings that break off parts;
   - the messages;
   - the VAB's "comes home" check;
   - stepping aside while StageRecovery is installed.
5. **Phil's Builds link:**
   - hangar copies in the Launchers tab;
   - completing two-stage launchers;
   - Restore to design;
   - "Use it?" for ready payloads;
   - the "Not filed" warning.
6. **Finish:**
   - every number in a settings file;
   - ignoring KRASH sims;
   - reporting recoveries the stock way, so PIP counts them;
   - the ScrapYard warning;
   - a README;
   - a release like Phil's other mods.

## Build status

Phil said "Yes, start building" on 2026-11-07. A git repo was made with Phil's personal identity.

**Released 0.1.0 as a pre-release on 2026-11-07** (Phil: "Yes, release it"; 0.1.0 pre-release, and
Phil's Builds released first). It's on GitHub as phillipnutt/phils-reusability-suite, tag v0.1.0. It needs
Phil's Builds 0.1.0 or later. Before publishing, all four in-game tests ran again on the release zip next
to Phil's Builds' own release: 166 checks passed.

**Phil's Builds hooks:** that mod's own development proposed a `PhilsBuilds.BuildsApi` covering all six
needs, and the suite accepted it. Its DESIGN.md has the details. Nothing is built there until Phil says
so. Two details:
- `DesignPlaced` fires as soon as Phil's Builds places a design. For a pick-up, the parts are held on the
  cursor and aren't in the ship until attached, so the suite stamps the parts the event carries.
- ~~The suite keeps its own copy of each design file~~ (changed while building step 5): a slot is only
  filled from the hangar when the part there is the same part as in the design file today. If a mark was
  saved over and a slot now holds a different part, that slot is built new. That's simpler than keeping
  copies of design files, and it never puts the wrong part in a slot.

**Step 1, built and proven in game** (six harness runs on the clean test install, 2026-11-07):
- **Proven:**
  - launch records with all 73 parts of the stock Kerbal X;
  - stock recovery of each dropped booster into the hangar, with dangling fuel lines going to the bin;
  - hangar ships surviving a save;
  - each hangar ship loading in the VAB with exactly its parts, serials and flight counts.
- **Found and fixed:**
  - KSP reports false joint breaks on landing legs when a ship unpacks. A break now only counts if the
    part really left its ship half a second later.
  - Telling a break from a decouple, as KSP 1.12's code reports them:
    - a decoupler or undocking announces itself (onPartDeCouple or onPartUndock), then removes the
      joint, which is also reported as a joint break;
    - a joint that physics snaps is reported as a break first, and then KSP decouples the part.

    So a joint break counts only if no separation of that part was announced in the same frame. The
    fourth run proved the break detection, and also showed radial decouplers wrongly counted as broken
    off, which this rule fixes.
  - The fifth run passed 60 of 61 checks: the break detected, and dropping boosters counting nothing as
    broken off. The broken-off nose cone happened to be recovered first, as its own piece, and that
    cleared its "broke off" note. So its booster didn't list it as missing. A broken-off part now stays
    noted until it next launches, because the ship it came off is still missing it.
  - **The sixth run passed all 61 checks, with no errors.** The booster came home as "5 parts to the
    hangar, 1 missing, 1 to the parts bin". Step 1 is proven in game.

What step 1 contains:
- **`PRSPart`**, on every part through a ModuleManager patch. It isn't added to EVA kerbals, asteroids or
  comets. It carries:
  - a serial number, new for every copy;
  - a flight count, which goes up at each launch.
- **The editor keeps serials honest.** Any part whose serial the editor didn't give out, or didn't load
  from the hangar, gets a new one, and so does a duplicate. So saved designs and subassemblies never
  carry specific hardware.
- **Launch records** are KSP's own copy of the ship as it left the pad, with every part's serial written
  in. They're not recorded during KRASH simulations. Records whose parts no longer exist anywhere are
  dropped at the Space Center.
- **Broken-off parts are noticed:** a joint that breaks without a decoupler or undocking, or a part
  destroyed while still attached.
- **Recovery** (stock recovery for now): the piece's serials are matched to its launch record, and the
  record is cut down to what came home. The craft must pass the validity check, or every part goes to the
  parts bin. Parts it can't place, and struts or fuel lines whose other end is gone, also go to the bin.
  Ships are numbered per name. Money isn't touched yet.
- **A plain prototype hangar list** in the VAB/SPH toolbar, with Load. It stands in until the real
  windows.
- **Checks:**
  - `Tools/Test-Core.ps1` runs the cross-off logic against KSP's 70 stock craft (781 pieces, 5,758
    checks, all passing).
  - An in-game self-test runs through the test harness on the stock Kerbal X: break a fin off, drop the
    boosters, recover every piece, save, then load each hangar ship in the VAB.

**Steps 2 to 4, built and tested in game** (2026-11-07; Phil: "go ahead and move forward with whatever
you need to do to complete this - launch and close KSP as you need"):

- **Step 2, the build queue.**
  - **The green Launch button opens the Build panel.** It shows each piece on its own line, the totals,
    Normal / Overtime / Outsource with time and price, funds before and after, and which build line.
  - **Build runs KSP's own pre-launch checks** (part count, size and mass limits, experimental parts, the
    facility, a control source). It leaves out KSP's price check and "launch site clear".
  - **Money is paid once, at Build.** Editing a queued build pays or refunds the difference and keeps the
    work done. Cancelling refunds it. Overtime is paid on the share still to build. Outsourcing refunds
    any unused overtime.
  - **Time passes in every scene except the editors,** across the build lines. Lines come from KSP's
    normalized facility level (stock's three levels assumed when the count isn't reported). A finishing
    build stops time warp.
  - **Finished ships go to the hangar, ready.** Launching a ready ship costs nothing: KSP's rollout
    charge is handed back. Launch it from the suite window, or load it in the editor unchanged and press
    the green button.
  - **The suite window** (toolbar, at the KSC and in the VAB/SPH) has three tabs. Build queue: lines,
    progress, Overtime / Outsource / Move up / Edit / Cancel, the ready list and "Warp to next finish".
    Hangar: groups, next-flight cost, Launch / Refit and launch / Open in the VAB, Take apart, Sell.
    Parts bin.
- **Step 3, the hangar.**
  - **The Recover prompt** is on all three of KSP's recovery paths (the flight button, the KSC vessel
    markers, the Tracking Station), through Harmony. It offers To the hangar (transport), Sell for scrap,
    or Cancel.
  - **Recovery money is settled against exactly what KSP paid,** measured around KSP's own refund. To the
    hangar takes the refund back and charges the transport; Sell keeps half the refund.
  - **A piece with no engine or probe core goes to the parts bin.**
  - **Matching bin parts are used automatically in new builds:** the same part at the same price, at refit
    price, carrying their own serial.
  - **Take apart and Sell for scrap** work for hangar ships and bin parts.
- **Step 4, stage recovery.**
  - **When KSP deletes a stage falling out of range in Kerbin's air,** the suite judges its landing:
    StageRecovery's rules, a powered landing with a 1.5× fuel margin, and the burn-up check.
  - **Drag is measured from the stage's own parts** when it separates or goes on rails, using KSP's
    own drag maths on a temporary state that is put back afterwards. Open parachutes count without being
    hidden by their parent part, as StageRecovery does. RealChute canopies use their diameter and the
    fabric's drag coefficient from RealChute's material file.
  - **Hard landings (6 to 12 m/s)** break off outer parts with nothing attached to them.
  - **Recovery is announced, not done the stock way,** because KSP is mid-way through deleting the vessel.
    The crew are then taken off so the deletion doesn't kill them.
  - **Messages** cover came home, came home hard, went to the bin, and lost.
  - **While StageRecovery is installed, the suite leaves stage recovery to it** and says so at the KSC.
    It also warns if ScrapYard is installed.
  - **The Build panel has a landing check:** each piece that drops off, with its landing speed empty of
    fuel and with every chute open.

Decisions made while building (within the agreed design):
- **Ships taken into a build stay in the hangar list,** greyed as "in a build", as the mockup shows,
  rather than moving out. Cancelling just frees them.
- **Each part remembers the hangar ship it belongs to** ("hull" on PRSPart). A piece that comes home
  takes the identity most of its parts share, so a ship launched from the hangar comes back as itself,
  and a flown first stage under a new payload comes back as "Oak 005 #4". New parts take the identity of
  the ship they were built into. Loose parts have none.
- **Serials the editor didn't give out or load from the hangar are replaced,** so saved designs never
  carry hardware. Coming back to the editor keeps the hangar parts of the ship that was left there.

**Test results (2026-11-07):**
- **Core checks:** 5,784.
- **Harness runs on the clean install:** step 1 55/55, build queue and hangar 58/58, stage recovery
  14/14.
- **What the stage test proved:** the Kerbal X crew pod came home at 4.6 m/s on its big chute from
  1,728 km out; its six chute-less boosters were lost at 444 m/s.
- **Bugs found and fixed along the way:**
  - a build-line count of 3 outside the KSC;
  - recovery money off by about 1%;
  - parachutes counted as hidden behind their pod;
  - landing legs counted as broken off when a decoupler moved them;
  - a KSP error from recovering a vessel mid-deletion.

**The test harness** gained a `waitlog` step, so a run moves on as soon as a self-test logs its done
marker instead of idling on a fixed wait (Phil: "you should be able to tell when your own tests are done
and not sit idle").

**Step 5, the Phil's Builds link, built and tested in game** (2026-11-07). Phil's Builds built its
`BuildsApi` (Phil: "Yes, build them") and tested it on its side. The suite now requires Phil's Builds.
- **Parts remember their design.** When Phil's Builds places a design (load, merge, pick up, or a
  hangar copy) or saves one, each part is stamped with the design's id and its slot in the design file.
- **Hangar copies in Phil's Builds' library window.** Under each filed design:
  - a New row for launchers and upper stages;
  - a row per hangar copy: "ready", "complete" or "first stage", its flights, any missing parts, and
    its price and build time;
  - a detail panel: what's refit, how many parts are new, fuel, and the flight log;
  - a note on the design's own row, such as "1 ready, 2 flown first stages in the hangar";
  - copies in a queued build are greyed out.
- **Picking a hangar copy puts the whole design on the cursor:** the copy's own parts in their slots,
  keeping their serials, flights and identity, plus new parts for every slot it doesn't have. That's both
  "a flown first stage plus a new upper stage" and "Restore to design" (lost parts are added back new).
- **"Use it?"** If the design in the editor has a ready, never-changed copy in the hangar, the Build panel
  offers it. Use it turns the editor's parts into that ship, so the green button launches it for free.
- **"Not filed in Phil's Builds"** shows in the Build panel when the ship isn't a filed design, with a
  File it button that opens Phil's Builds' Save panel.
- **Phil's rule is on** (`requireDesign` in Settings.cfg): a piece that comes home with no part from a
  filed design goes to the parts bin, not the hangar.
- **Slots are matched by part, not by copies of design files** (a change from the plan above). A slot
  is only filled from the hangar if it holds the same part in the design file today; otherwise it's
  built new. KSP renames some old parts when it loads a craft ("RCSBlock" became "RCSBlock.v2"), so
  design files are first run through KSP's own upgrade scripts. Phil's Builds' own test spotted this.

**Test results (2026-11-07, step 5):**
- **Core checks:** 6,344, all passing, including completing a copy whose design changed.
- **Harness runs on the clean install:**
  - step 1: 55/55;
  - stage recovery: 14/14;
  - build queue and hangar: 58/58;
  - Phil's Builds link: 39/39.
  - No exceptions in any run.
- **What the link test does,** in its own career save with the stock Jumping Flea filed as a launcher:
  1. Builds it.
  2. Sees the ready copy in the library rows and in the Build panel's "Use it".
  3. Flies it and loses the chute.
  4. Brings the rest home as "first stage · 1 flight".
  5. Picks it up as the whole design: 7 flown parts plus a new chute, each in its slot, priced 422
     new and 262 refit.
  6. Checks that an unfiled Jumping Flea shows the warning and that all 8 of its parts go to the bin.
- **Found and fixed:**
  - the "Not filed" text was cut off;
  - the hangar note called a never-flown copy "flown".

**Released 0.1.0, then Phil's first play-test (2026-11-07 evening).** Phil filed a payload (Seeker I) and a
launcher (Kopje 005) in his own career on the test install, built both, stacked them and flew. Fixed from what he
found, all covered by in-game tests:
- A launcher built on its own kept KSP's default name. A ship that is all one design is now built under the
  design's name; a payload stacked on a launcher takes the payload's (its root part's) name.
- A payload stacked on a launcher counted as a copy of the launcher (a majority of its parts). A hangar ship is
  now a copy of a design only if every design-placed part came from that one design.
- Stage recovery only ran in flight: a ship left falling while Phil watched from the Tracking Station was deleted
  by KSP with no landing check. It now runs at the Space Center and in the Tracking Station too, with ground speed
  and height worked out from the orbit. (KSP's Orbit.GetVel() includes the planet's own speed around the Sun away
  from flight: GetRelativeVel() is used.)
- A hangar ship opened in the VAB lost its identity after a trip to the Space Center and back: which parts were
  hangar hardware was noted as the editor closed, after its ship was gone. It's now kept up to date.
- The VAB landing check gave different speeds each time and measured each piece while still stacked between its
  neighbours (faces it shares with them counted as hidden). It now brings the whole ship's drag up to date first,
  and measures each piece on its own by unhooking its stack joints to the rest for the measurement only. Launch
  clamps are left out (they stay on the pad); a heat shield keeps its ablator. Checked in flight: a booster
  measured this way before it drops reads 0.71, the same as the real dropped booster; the pod 2,069 vs 2,195.
- KSP's pre-launch warnings during a build say "Build anyway".
- Launch clamps left on the pad by an earlier launch no longer block launching a ready ship: they're recovered
  to the parts bin.

**Overnight 2026-11-07 → 08** (Phil: "do whatever you want within the scope of these projects", "Build freely",
push code, no release). Phil's Builds worked alongside on its own findings from the same play-test.
- **Build from Phil's Builds without the VAB:** the Build queue tab lists every filed launcher, upper stage and
  craft, priced, with Normal / Overtime / Outsource. What KSP's own checks would stop is checked from the craft
  file: parts not researched, bought or installed, the building's part limit, the pad's or runway's mass and size.
- **Restore to design** in the Hangar tab: a flown copy is built back up to its whole design (its own parts, new
  or bin parts for the rest) and stays the same ship.
- **Forecasts:** as each stage drops (or, if it was the ship being flown, as it goes out of range), the suite
  says whether it will come home, and how. Pieces dropping together make one message.
- **New in-game tests:** a powered landing (the Z-MAP's middle stage comes home with a landing burn and uses up
  the propellant), and the whole reuse loop (build, fly, the pod comes home, picked up as the whole design at
  refit price, built and flown again, comes home as the same ship with two flights).
- **Three code reviews** (a separate agent reading the code fresh each time, the last two over the older 0.1.0
  code too) found about 20 problems, all fixed and listed in the CHANGELOG. The ones that shaped the design:
  - **Which parts are hangar hardware in the VAB** is decided only from what the suite gave out (a hangar ship
    loaded, a bin part matched, a queued build opened), never from the serials a craft happens to carry. KSP
    brings back the ship just launched when you return to the VAB, and its serials are still flying.
  - **A price is checked again when Build is pressed:** if a bin part or hangar ship it counted on has been
    sold, taken apart or put in another build, the build is refused and priced again.
  - **Which piece is the ship** when pieces come home separately: the piece holding the launched ship's root
    part keeps its name, number and log. The others are ships of their own.
  - **A record knows its vessel** (KSP's persistent id), so going back to a ship on the pad doesn't count
    another flight. KSP reuses vessel ids, so a record also has to share serials with the ship.
  - **Destroyed parts:** a part destroyed while attached counts as lost from its ship only if the ship survives
    it (a fin lost on landing). A stage that crashes whole loses nothing "from" anything. Launch clamps letting
    go never count.
  - **Harmony patches** are applied one at a time, so a KSP change that breaks one hook leaves the rest working.

**Phil's second play-test (2026-11-08 morning)** in his own career on the test install: a capsule (Argo I) and a
launcher (Rockall 002) built apart and stacked in the VAB, which worked (two "Use it" offers, nothing to pay, no
build time). Fixed from what he found:
- **Serials in saved craft files.** Craft files saved in the editor keep each part's serial. The editor trusted
  every serial it had given out during a visit, so a design saved from parts that were then built (Harris Hawk
  II, saved from the I's parts) brought the hangar ship's hardware back when loaded in the same visit, and the
  next build quietly used that ship up (three times in a row in his log). Now trust follows the ship, not the
  serial: a load that replaces the ship resets trust to what the suite itself is loading (`EditorExplicit`: a
  hangar ship, a queued build); a merge and Phil's Builds placing a design file (Load, Merge, PickUp, but not a
  hangar Copy) give the placed parts new serials. KSP fires `onEditorLoad` during the load, with the whole new
  ship for a normal load and only the merged parts for a merge (read from `EditorLogic.ShipToLoadSelected`).
- **A build made from hangar ships keeps the top ship's identity** (Phil: "your pick"): the hangar ship that holds
  the build's root part, under the same name, stays that ship (number and flight log). The others join it.
  Hangar cards say what a ship sits on, from its parts' hulls ("on Rockall 002 #1") or designs ("on Rockall 002").
- **The landing check** splits only at decouplers in the staging, so a heat shield (its jettison is unstaged by
  default) stays with its pod; pieces are called stages.
- **Text:** IMGUI clipping cut off the OS fonts' descenders ("Arao" for "Argo", "16.303"); styles don't clip now.
- Phil's Builds' own findings (Load underground, saving over a launcher, stage names of a deleted launcher, a stale
  "Built on") went to its session.

**Simplification pass (2026-11-08)**, Phil's call after asking whether the code was "ballooning into patch after
patch". About a third of the bugs since 0.1.0 came from two places where several layered rules answered one
question. Each question now has one rule (about 100 fewer lines of code, every test still passing):
- **Hardware in the editor** (`EditorAddon`): a part keeps its serial if no hangar ship, bin part, build or ship in
  flight owns it, or if the suite put it there (`Claim`: hangar Load, editing a build, Use it, hangar copies, the
  ship just built). KSP's `onEditorRestart` (as the editor opens, for New and for every load, not merges) resets
  what the suite put there, except when it reopens with the ship left there (`LOAD_FROM_CACHE`); then whatever of
  that ship is still stored is claimed. This replaced the trusted, carried and explicit serial lists and the
  "restoring" flag, and found a gap they shared (New, then picking up a design saved from a hangar ship).
- **What a piece lost** (`Pieces.Build`): read from the launch record when it comes home. A part missing from below
  a part that came home was lost, unless the two parted at a separator (any module with "Decouple" in its name,
  docking ports, claws, launch clamps). Parts the suite's own landing check broke off are lost either way. This
  replaced the joint-break, decouple, undock and part-destroyed listeners, the same-frame and 1.5-second rules, and
  the saved "broke off" list. It's checked offline by Test-Core on every stock craft.
- **Who a returning piece is** (`PRSScenario.WhoIs`): one rule for the hangar and the flight forecast.

**Phil's third play-test (2026-11-08 afternoon)**, career save on the test install (Gallant I, Capsules). Working:
recovery with 7 lost parts, building from a hangar copy, Restore with all 15 parts, Open in the VAB loading exactly
what's stored. Pick up stays the whole design; Open in the VAB is the "just what's stored" route. Changed:
- **Wear and Refurbish** (above): replaces the flight-count refit and the always-refit rule. "Refit and launch"
  (which only queued the refit) is gone: a hangar ship has Launch (refuelled first, keeps its wear), Refurbish
  (queued, back to 0%) and Restore (refurbishes the rest too). Wear rides on each part (`PRSPart.wear`, with the
  flight's peaks), so it travels into the editor, into flight and into the parts bin with the hardware.
- **The hangar panel** scrolls its prices and flight log; its buttons stay at the bottom (at 5 flights "Open in the
  VAB" couldn't be reached).
- **A ship opened from the hangar keeps its name:** the editor calls it "Gallant I #1", and the build is named
  "Gallant I", so it stays Gallant I #1 (it came home as "Gallant I #1 #1"). Builds that keep a hangar ship's
  identity (refurbish, restore, built on top of it) are shown by that ship's name in the queue and the log.
- **Open in the VAB** works from the Space Center too (it goes to the ship's building and opens it there), and
  beside Pick up in Phil's Builds (from Phil's Builds' API 2: `CopyDetail.OpenLabel` and `Open`).
- **Fuel** is shown against what the ship launched with ("fuel 100% of launch load" for a booster loaded to 30% on
  purpose that brings all of it back), not against tank capacity.
- **The Build panel's breakdown** is a list, not cards: "8 flown parts from Gallant I #1", "7 new parts".
- **Scrap:** the Recover prompt says what the maths does: half of what it's worth where it landed (KSP's distance
  rule first, then half), not "half its price, less the haul".
- **The Build panel offers flown copies too** (Phil: "Yes, point it out"): a design loaded as new parts with a hangar
  copy of it that fits (each of the copy's parts has its slot here, same part) gets "Use it": a ready, complete copy
  first, then the copy with the most parts, then the least worn. Its parts take their slots, the rest stay new.
- **Whole-craft pick-ups into an empty VAB are named after the design** (Phil's Builds' rule, now for every kind of
  design, not only launchers and upper stages).

**Phil's fourth play-test (2026-11-08 evening)**, the same career and a new one. Working: Take apart, "Use it" for a
ready payload and a ready launcher copy, Build · 0, and the unwatched-landing rules ("good and reasonable math").
Changed:
- **One rule for a Phil's Iterative Parts part's Mk** (Phil: "totally current with whatever the VAB can know and
  apply"). A *new* part is its program's current Mk wherever the build starts. A *flown* part keeps the Mk it was
  built at until it's updated. PIP owns what a Mk is and the one step that brings a part up to date
  (`PIP.PipApi`: `ToCurrent`, `Behind`, `Title`, and the `IsHardware` hook); the suite calls it by reflection (no
  PIP: nothing changes). "Up to date" includes the family's newest version, whose part has another name: the suite
  then renames every link to it (`Completion.TakePartName`), matches an old bin part as it would be once updated
  (`Pip.Preview`), and lets a flown part fill its design slot as the part it is (Restore's `family` rule). Three bugs
  this fixes:
  - A Space Center build used the design file's saved Mk (Gallant I #4 flew Mk 1 parts, priced as Mk 3). Library
    builds and Restore's new parts now go through PIP's step first (`Pip.MakeCurrent`).
  - Restore built a flown part's slot from the design file's PIP data, so Gallant I #1's Mk 2 parts came back Mk 1.
    `Completion.CarryHardware` puts the flown part's own hardware modules (`ModulePipPart`, `ModulePipEngine`) in its
    slot; everything else (staging, a fired decoupler) still comes from the design. A bin part put into a build
    carries its own the same way.
  - The VAB re-stamped every PIP part with the current Mk, hangar hardware included (opening Gallant I #1 made its
    Mk 1 pod a Mk 2). PIP now asks the suite as the editor builds each part from its saved data
    (`EditorAddon.IsHardwareNode`, the serial rule above) and leaves hardware as it is. Builds also copy each hardware
    part's stored PIP modules into the build (`WithHardware`), and "Use it" reloads them onto the editor's parts.
- **Update PIP parts** (Phil's design). Bin parts and hangar copies whose programs have moved on show it ("Augur I
  Mk. 1 → Mk. 3") and have an "Update PIP parts" button. A build (VAB, Space Center or Restore) that finds a bin part
  that fits once updated asks: "Update and use · N", or "No", which says those parts will be built new and the old
  ones stay in the bin, then prices it that way. Old-Mk bin parts are never used as they are. With PIP's prices the
  same at every Mk, matching still notices the Mk (`Pip.Behind`), so it asks instead of quietly using an old part.
  The update costs `pipUpdatePerMk` of the part's price per Mk gained (Phil: "10% per Mk"; PIP counts a new version
  as the next Marks along the family). A hangar ship's own old parts **can fly as they are** (Phil): its card says
  "N PIP parts behind" with Update, and Restore asks "update its old parts too?", where No keeps them. Only bin parts
  follow the strict rule.
- **New parts split by design** in the Build panel: "Cabral I · 1 new part", "Shkhara 022 · 31 new parts".
- **The bin's parts are named** in the Build panel and library cards, with any refurbish cost ("Mk1 Command Pod ·
  wear 20% · refurbish 45"), and the bin tab says what a build would pay to refurbish each row.
- **Bin parts keep the fuel they came back with**, as hangar ships do: a build only tops them up (Gallant I #6 paid
  160 for fuel it already had).
- **Pictures** in the Build queue (job cards, library cards) and the Hangar (rows and the detail panel), from Phil's
  Builds' own design pictures (`BuildsApi.PictureOf`, its API 3). A stack shows its payload, and the launcher
  beside it in the detail panel; a copy missing parts gets an amber marker. The Parts bin stays text-only.

**Phil's fifth play-test (2026-11-09)**, a new career with both DLCs. Changed:
- **Pick the crew before launch** (Phil: "implement who the crew is going to be before launch"). Launch (or "Refuel
  and launch") on a hangar ship with seats opens a crew step in the suite window first (`CrewStep.cs`): the crew by
  part, seat by seat, beside the kerbals at home, with Add, ×, Fill, Clear and Reset, as KSP's own crew panel has.
  It starts with KSP's own pick (`DefaultCrewForVessel`, as KSP's launch-pad dialog does) and launches with the
  manifest as picked (`LaunchReady(h, out error, crew)`). A ship with no seats launches at once. `CanLaunch` holds
  the checks (in a build, worn out, the pad clear), so nothing is charged until the crew step's Launch. KSP's own
  crew panel can't be borrowed: it lives only inside its launch-pad dialog (which lists every craft file) and the
  VAB. Mockup sent to Phil with the build.

## Ship identity, rebuilt (round 4, 2026-11-09)

Phil's sixth play-test found ships coming back to life and one ship logged under three names: Peleus
III #2 launched as "Peleus IV #1", came home as Peleus III #2, was taken apart, and then came home again as the
Kupe's capsule. The cause was the foundation: identity was copied onto every part (serial, hull, flights, wear) and
four separate guesses rebuilt it from those copies. Phil: "please favor simplifying code rather than accumulating
patch after patch on a dubious foundation". The proposal and his decisions are in Phil's Mods' notes
(`suite-foundation-v2.md`, including its later sections). This section replaces everything above about serials,
hulls, `WhoIs`, per-part flight counts and wear, "Use it", Restore to design, and bin parts keeping their fuel.

**Phil's rules:**
- **A. Ship of Theseus** (Phil: "once I build the Peleus III #2, that ship should always be the Peleus III #2,
  regardless of if I add that barometer, save that new ship design as the Peleus IV ... that ship is still the Peleus
  III #2"). A build that starts from a hangar ship is always that ship, whatever it's saved as and however much is
  changed: its name, number, flights and log stay, and no new number is used up. There's no prompt. To get a new ship
  from old hardware, take the old one apart first; the build from the bin then gets the design's next number.
- **No "built as" text** (Phil: "The ship is the Peleus III #2, period"). Cards, the Build panel, the queue, logs and
  messages show the ship's own name only.
- **B.** A ship's flight count is its own launches.
- **C.** The parts bin is a plain inventory ("hey we have this thing"). A part going in forgets its ship, its
  flights and all its fuel, and is refurbished as it goes in (paid then). After that it's exactly like a new part
  that's already paid for. Mk 2 and Mk 3 of a PIP program are different parts.
- **D.** Owned parts are cheaper only because they're already paid for; flights never change a price. Price lines
  say "from the parts bin, already paid for", never "flown parts". "On X" names only what a ship sits on.
- **E.** On return, "see what launch that something came from and what ships (and their IDs) were used in that
  launch, and then categorize them accordingly."
- **Its own blueprint** (Phil: "couldn't it have a blueprint of the 'Peleus III #2' and restore uses that?"). Every
  ship keeps the whole ship as it was last built, hidden in the suite's save, and Restore uses it.
- **Its design, for good** (Phil: "The Peleus III #2 knows it's a Peleus III ... It just also knows that it is
  SPECIFICALLY the Peleus III #2"). A ship belongs to the design it was first built as; Phil's Builds counts it there.
- **Picked explicitly.** Built ships are opened, launched or picked up from the hangar, or from Phil's Builds' hangar
  list ("Peleus III · 1 in hangar: Peleus III #2"), which brings in the real ship. Loading a design is always a new
  build; the Build panel never decides by itself that it's a hangar ship.

**The records** (`Records.cs`), each in one place, nothing kept on parts:
- **`Ship`**: id, name, number, the design it was first built as (`DesignId`, set once), its own flights, its flight
  log. It lives while it exists anywhere: in the hangar, riding inside another hangar ship (a flown first stage under
  a new payload), kept by a build, or in flight while the part it rides on (its root part) is still flying. Ships that
  exist nowhere are dropped at the Space Center (`Tidy`).
- **`HangarShip`**: the ship, its craft, an id for this copy of the craft (`CraftId`, new whenever its parts change),
  ready, wear (one number per craft), what each part came home with, parts missing, `Pieces` (its parts that are
  another ship's: a flown first stage under it), and its **blueprint** with each part's place in it (`Slots`).
- **`LaunchRecord`**: KSP's own launch id, KSP's own part id for each part of the craft as launched (matched to the
  launched-ship config by the craft's id for each part, so the order of the ship's part list doesn't matter), its wear
  as it left, the ships that went up (each with the part it rides on) and which ship each part is. KSP writes both ids
  at launch, docking only reads them, and the save keeps them (checked in KSP's own code, and in game by
  `SelfTestIds`). One record per launch: going back to the pad or quickloading finds it by its launch id.
- **`BuildJob`**: the ship it keeps, or none (a new ship), which of its parts are which hangar ship's hardware, and the
  parts it took from the bin (counts, given back on Cancel).
- **`BinKind`**: one kind of part and how many. The kind is the part, its PIP program and Mk, and its price (which
  covers variants and rescaling). One sample part node, empty, with no ship.

**The VAB** (Phil: "the VAB to understand if I've got an actual ship that is loaded in there ... and it knows that the
new part I just added is not a built part"). A part is real hardware only if it carries a note the suite put there
itself when it brought a stored ship in ("craftId:index", `Core.Hardware`): Open in the VAB, Edit a queued build, a
hangar ship picked up in Phil's Builds. The note is checked against the hangar whenever it's used (that ship, its
current craft, the same part there), so a note from an older copy of a ship points at nothing. Parts that arrive any
other way (KSP's craft browser, a merge, a subassembly, a Phil's Builds design) are new parts: their notes are cleared
as they arrive (`onEditorLoad` and a Harmony prefix on `EditorLogic.SpawnConstruct`, the two places every arriving
part passes); so is a copy of a part already on the editor's ship (a merge of a file saved from it). A copy made with
the editor's tools is a new part (`OnCopy`). KSP's undo and its cache (as the editor opens) keep the notes of the ship
that was there. Launching ends them. A build that's cancelled keeps a short list of which hangar parts its parts were
(`cancelled`, the last 10), so the VAB's ship is still that hardware. PIP's `IsHardware` hook asks the same rule. A
hangar part with something added to it in the VAB (cargo, a bigger size, a dearer variant) is still that hardware;
what was added costs its full price (`Owner.Added`). The VAB ship of a hangar ship is named after the ship's name
("Peleus III"), never its number.

**A build** is the hangar ship whose hardware is nearest its root (its root part, unless that's a new part), whatever
the craft is saved as (`MainShipOf`). Any other ship in the build whose own root part is in it rides along as itself
(Pieces). With no hangar hardware it's a new ship, which belongs to its design for good (the design of all its parts,
or of its root part, the rule that also names it) and gets that design's next number when it's finished. Restore and
Refurbish keep their ship explicitly; a kept ship must be in the build. A hangar ship's parts the build leaves out go
to the parts bin, refurbished at that ship's wear, priced in the build ("1 part left out goes to the parts bin"). What
was built becomes the ship's blueprint.

**A launch** of a hangar ship is always started by the suite (the Space Center's Launch, the Build panel's Launch,
the VAB's launch-site list), which notes which hangar ship is launching (`PendingLaunch`). At the launch record, only
that ship counts, and only if every launched part is exactly its hardware. It lists the ship (riding on the craft's
root part) and every ship inside it (each on its own highest part), counts a flight for each, and takes the ship out
of the hangar. A launch from KSP's own launch-pad dialog lists no ships, whatever its file says.

**A return** (`RecoverLaunch`): parts are grouped by launch (two launches docked together are filed per launch). For
each, the record gives each part's place by its KSP part id; the piece is rebuilt (`Pieces.Build`), and it's the
launch's ship whose root part it carries (the highest in the launch's tree if several; the others ride inside it as
themselves). If it carries none it's a new piece, named after its design (or its root part's design) and belonging to
it, on its first flight. Wear = the craft's wear at launch plus this flight's peaks. Its blueprint is the ship as it
launched less what it dropped on purpose (stages, payloads), so Restore puts back only what it lost. Each part's
cargo is taken as it came home, so cargo used or taken out in flight is gone. Parts not in the record (added in
flight), parts that can't be placed and dangling struts go to the parts bin, refurbished at that wear and paid then.
A ship whose root part didn't come home is closed as lost. The flight forecast names a falling piece by the same rule
(`PRSScenario.ShipIn`).

**Restore** rebuilds a hangar ship from its own blueprint (`Hardware.Restore`): each part it still has in its place,
as its own hardware (its Mk, its cargo as it is), and every part it lost new (PIP's current Mk). The button just says
"Restore". Phil's Builds' Load or Pick up on a hangar ship brings in the same whole ship.

**The bin** is matched only when pricing, by kind and count, at no cost ("from the parts bin, already paid for").
Bin parts are filled from empty. An old-Mk kind that would be the part once updated is offered ("Update and use",
"No"), as before. Taking a ship apart costs its parts' refurbish at its wear ("Take apart · refurbish N"); stage
recovery adds the bin's refurbish to its transport line; the Recover prompt shows it, launch by launch, when parts
will go to the bin.

**Older saves** (format 1, before round 4) start the suite fresh on load (Phil: start fresh, no
converter): empty hangar, bin and queue, queued builds refunded, numbers already given out kept. Ships still in flight
have no launch record any more, so their parts go to the parts bin when they're recovered (Phil: "Parts bin"). The save
file itself isn't touched; the next save writes format 2.

## Open questions

- None for the design. The money numbers get tuned in play.
- Money balance: cash should limit Phil, but not badly, and reuse should save both time and money.
  Fine-tune later. All numbers above go in a settings file.
- UI mockup: the queue and hangar window, the VAB price panel, the Recover prompt, and what Phil's
  Builds shows.

## Research (2026-11-07)

These are summaries of other mods' public source code, read for this design. No code was copied.

**StageRecovery 1.9.8.1**
- **When it acts:** when the game deletes an unloaded stage falling through Kerbin's atmosphere. The
  stage's full record is still intact at that moment.
- **Landing speed:** worked out from parachute drag (stock and RealChute) and mass. Under 6 m/s returns
  the full value, and the value falls to nothing at 12 m/s.
- **Powered landings** need a pilot or SAS probe core, liquid engines, a thrust-to-weight ratio of at
  least 1, and fuel with a 1.5× margin.
- **Burn-up** is a random check above 2,000 m/s, reduced by remaining heat-shield ablator.
- **The refund** follows stock's distance-from-KSC rule (98% down to 10%), times a global setting.
- **It pays the refund before telling other mods,** and no other mod can stop the payment.
- **It has no "would this stage survive?" check.** Its only calculation pays out.
- **Its event data** is inconsistent and its text is translated, so other mods can't rely on it.
- **A known bug affects setups like Phil's.** KSP Community Fixes' memory-leak fix makes it read
  TweakScale-resized parts at their original size, so landing speeds can be badly wrong (one report:
  predicted 10 m/s, actual 25 m/s).
- **Maintenance:** it's maintained reactively, and the maintainer has twice declined "store the stage"
  requests.
- **Licence:** the licence file says MIT, but the code files carry GPL notices.

**ScrapYard**
- **How it works:** it keeps recovered parts as loose parts, never as whole stored ships, and swaps
  them into new builds by part name and price.
- **Unreleased fixes:** the CKAN build (2.2.2.1, from 2023, marked for KSP up to 1.12.3) wrote its own
  ids into the game's part ids. That broke the VAB's delta-v readout and spread duplicate ids into craft
  files. A fix was merged in 2026 but never released.
- **Money:** by default it changes no money at all.
- **Conflicts:** it acts on the same recovery and launch events this suite needs, so the two shouldn't
  run together.

**Kerbal Construction Time 1.4**
- **Build time:** about 2000 × √(dry cost) build points at a slow base rate, sped up by upgrade points.
  With default points, a 20k-funds ship took about 15 Kerbin days and a 500k ship about 77.
- **Building:** it takes over the green Launch button and charges when the build is queued.
- **Storage:** only for the active vessel. It copies the vessel as a craft file, keeps its fuel, and
  resets engines, chutes and experiments.
- **"Merge Built Vessel":** while editing a finished stored ship you could attach other stored ships and
  pay only for the new parts. This is the closest thing to idea 5, though it was never the main
  feature.
- **Rush:** each click cost 20% of the ship's price and finished 10% of the remaining work. There was no
  instant finish.
- **Lessons:**
  - Money leaked when a charge silently failed.
  - Stored vessels got lost after edits or recoveries.
  - Taking over stock buttons broke other mods.

**KRASH 0.5.34.2**
- Simulations start from KRASH's own button, not the green Launch button, so replacing that button
  doesn't break them.
- A sim can be detected from KRASH's save data.
- KRASH's revert undoes anything kept in a mod's own save data.

**PIP:** PIP counts a recovery when the game reports a vessel recovered. StageRecovery's recoveries
reach PIP that way, and this suite must report its recoveries the same way.
