using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using PhilsBuilds;
using PhilsReusabilitySuite.Core;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// The whole reuse loop in one unattended run, started by the test harness through the hidden
    /// PRSSelfTestReuse event. In a throwaway career save (zz_PRSSelfTestReuse) the stock Kerbal X is filed in
    /// Phil's Builds as a craft (saved with its booster) and as a launcher. It is built and flown; high over Kerbin
    /// everything drops away and its crew pod comes home on its chute to the hangar as the ship, missing what it dropped.
    /// The flown pod is rebuilt into the whole design (its own parts refurbished by wear, the rest new or from the bin) and
    /// flown again: the same pod comes home a second time as the same ship, with two flights in its log. Rebuilt on the
    /// launcher instead, the ship's blueprint is the pod piece alone, and it comes home missing nothing.
    /// </summary>
    class SelfTestReuse : MonoBehaviour
    {
        public const string SaveName = "zz_PRSSelfTestReuse";
        const string DesignName = "Lifter 010 I";
        static bool running;
        int pass, fail;

        public static void Begin()
        {
            if (running) return;
            running = true;
            var go = new GameObject("PRSSelfTestReuse");
            DontDestroyOnLoad(go);
            go.AddComponent<SelfTestReuse>();
        }

        void Check(bool ok, string what)
        {
            if (ok) { pass++; Debug.Log("[PRS-TEST] PASS " + what); }
            else { fail++; Debug.LogError("[PRS-TEST] FAIL " + what); }
        }

        static void Note(string s) { Debug.Log("[PRS-TEST] " + s); }
        static bool Near(double a, double b, double tol = 1.5) => Math.Abs(a - b) <= tol;
        static double Funds => Funding.Instance != null ? Funding.Instance.Funds : double.NaN;
        static Core.Settings S => Suite.Settings;

        void Start() { StartCoroutine(Run()); }

        IEnumerator Run()
        {
            bool designRule = Suite.Settings.RequireDesign;
            Suite.Settings.RequireDesign = true;
            IEnumerator inner = Body();
            while (true)
            {
                object cur;
                try { if (!inner.MoveNext()) break; cur = inner.Current; }
                catch (Exception ex) { Check(false, "the test threw " + ex); break; }
                yield return cur;
            }
            Note("done: " + pass + " passed, " + fail + " failed");
            Suite.Settings.RequireDesign = designRule;
            Debug.Log("[PRS-TEST-DONE]");
            Destroy(gameObject);
        }

        IEnumerator WaitFor(Func<bool> cond, float timeout, string what)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!cond())
            {
                if (Time.realtimeSinceStartup - t0 > timeout) { Check(false, what + " (timed out)"); yield break; }
                yield return null;
            }
        }

        IEnumerator ToEditor()
        {
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            EditorDriver.StartupBehaviour = EditorDriver.StartupBehaviours.START_CLEAN;
            EditorDriver.StartEditor(EditorFacility.VAB);
            yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.EDITOR && EditorLogic.fetch != null && EditorAddon.Instance != null, 90, "the VAB opened");
            yield return new WaitForSeconds(3f);
        }

        IEnumerator ToSpaceCenter()
        {
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            HighLogic.LoadScene(GameScenes.SPACECENTER);
            yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.SPACECENTER && PRSScenario.Instance != null, 90, "the Space Center opened");
            yield return new WaitForSeconds(3f);
        }

        static string KerbalX => Path.Combine(KSPUtil.ApplicationRootPath, "Ships", "VAB", "Kerbal X.craft");

        const string LauncherName = "Boulder 001 I";

        /// <summary>The Kerbal X filed as a craft (a capsule saved with its booster), and as a launcher to put under a ship.</summary>
        static void WriteLibrary(string saveDir)
        {
            string craftDir = Path.Combine(saveDir, "Ships", "VAB", "Capsules"), subs = Path.Combine(saveDir, "Subassemblies");
            Directory.CreateDirectory(craftDir);
            Directory.CreateDirectory(subs);
            var name = new Regex(@"^ship = .*$", RegexOptions.Multiline);
            string text = File.ReadAllText(KerbalX);
            File.WriteAllText(Path.Combine(craftDir, DesignName + ".craft"), name.Replace(text, "ship = " + DesignName, 1));
            File.WriteAllText(Path.Combine(subs, LauncherName + ".craft"), name.Replace(text, "ship = " + LauncherName, 1));
            File.WriteAllText(Path.Combine(saveDir, "PhilsBuilds.cfg"),
                "PHILS_BUILDS\n{\n\tSUBASSEMBLY\n\t{\n\t\tfile = " + LauncherName + "\n\t\tkind = Launcher\n\t\tfamily = Boulder\n\t\tstyle = Roman\n\t\tmark = 1\n\t\trating = 10\n\t\twidth = 2.5\n\t}\n}\n");
        }

        /// <summary>The decoupler under the crew pod (everything below it is the stages).</summary>
        static Part DecouplerUnder(List<Part> parts, Part pod) =>
            parts.FirstOrDefault(p => p.FindModuleImplementing<ModuleDecouple>() != null && Subtree(p).Count < parts.Count && p.parent != null && (p.parent == pod || p.parent.parent == pod));

        static List<Part> Subtree(Part p)
        {
            var list = new List<Part> { p };
            foreach (Part c in p.children) list.AddRange(Subtree(c));
            return list;
        }

        static bool IsBooster(Part p)
        {
            for (Part a = p; a != null; a = a.parent)
                if (a.FindModuleImplementing<ModuleAnchoredDecoupler>() != null) return true;
            return false;
        }


        /// <summary>
        /// On the pad: up to 20 km over Kerbin and falling, drop the boosters and the pod, fly the core off to orbit,
        /// and wait for KSP to delete the falling pieces (the pod comes home on its chute, as the ship it launched as).
        /// </summary>
        IEnumerator FlyAndDrop(string flight, string shipId)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            CelestialBody home = v.mainBody;
            Part pod = v.rootPart;
            Part decoupler = DecouplerUnder(v.parts, pod);
            Check(decoupler != null, flight + ": found the decoupler under the crew pod");
            if (decoupler == null) yield break;
            var podPiece = v.parts.Where(p => !Subtree(decoupler).Contains(p) && !IsBooster(p)).ToList();
            var podIds = podPiece.Select(p => p.flightID).ToList();
            foreach (Part p in v.parts) if (p.Rigidbody != null) p.Rigidbody.isKinematic = false;
            foreach (LaunchClamp lc in v.FindPartModulesImplementing<LaunchClamp>()) lc.Release();
            yield return new WaitForSeconds(1f);
            double ra = home.Radius + 20000, rp = 60000;
            FlightGlobals.fetch.SetShipOrbit(FlightGlobals.Bodies.IndexOf(home), (ra - rp) / (ra + rp), (ra + rp) / 2, 0, 0, Math.PI, 0, Planetarium.GetUniversalTime());
            yield return new WaitForSeconds(5f);
            v = FlightGlobals.ActiveVessel;
            Check(!v.LandedOrSplashed && v.altitude > 10000, flight + ": flying high over Kerbin (" + Math.Round(v.altitude) + " m)");
            var boosterIds = v.parts.Where(IsBooster).Select(p => p.flightID).ToList();
            foreach (ModuleAnchoredDecoupler d in v.FindPartModulesImplementing<ModuleAnchoredDecoupler>().ToList()) if (!d.isDecoupled) d.Decouple();
            ModuleDecouple sd = decoupler.FindModuleImplementing<ModuleDecouple>();
            if (sd != null && !sd.isDecoupled) sd.Decouple();
            yield return new WaitForSeconds(1.5f);
            Vessel core = FlightGlobals.Vessels.Where(x => x != null && x.loaded && x != FlightGlobals.ActiveVessel && !x.parts.Any(p => podIds.Contains(p.flightID)) && !x.parts.Any(p => boosterIds.Contains(p.flightID)))
                                       .OrderByDescending(x => x.parts.Count).FirstOrDefault();
            Check(core != null, flight + ": the core stage separated from the pod");
            if (core == null) yield break;
            FlightGlobals.ForceSetActiveVessel(core);
            yield return new WaitForSeconds(3f);
            FlightGlobals.fetch.SetShipOrbit(FlightGlobals.Bodies.IndexOf(home), 0, home.Radius + 150000, 0, 0, 0, 0, Planetarium.GetUniversalTime());
            yield return WaitFor(() => PRSScenario.Instance != null && PRSScenario.Instance.HangarOf(shipId) != null, 90, flight + ": the pod came home");
            yield return new WaitForSeconds(2f);
        }

        IEnumerator Launch(HangarShip h, string what)
        {
            PRSScenario sc = PRSScenario.Instance;
            Check(sc.LaunchReady(h, out string err), "launching " + what + " " + err);
            yield return WaitFor(() => HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.loaded && !FlightGlobals.ActiveVessel.packed, 120, what + " reached the pad");
            yield return new WaitForSeconds(3f);
        }

        /// <summary>The launch record of the ship on the pad.</summary>
        static LaunchRecord OnThePad()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            return v == null ? null : PRSScenario.Instance?.RecordOf(v.parts[0].launchID, v.parts[0].flightID);
        }

        static int BinCount(PRSScenario sc) => sc.Bin.Sum(k => k.Count);

        /// <summary>
        /// A design file with a hangar ship's parts in their own slots (by the design and slot Phil's Builds stamped on them),
        /// noted as its hardware, and every other part new: what the player builds in the VAB around a flown pod.
        /// </summary>
        static Node AroundShip(Node file, string designId, HangarShip h, out int used)
        {
            Node craft = Hardware.Stamped(file, designId);
            List<Node> stored = h.Craft.Children("PART").ToList();
            var bySlot = new Dictionary<int, int>();
            for (int k = 0; k < stored.Count; k++)
            {
                Node m = Hardware.Module(stored[k]);
                if (m?.Get("design") == designId && int.TryParse(m.Get("slot"), out int s) && !bySlot.ContainsKey(s)) bySlot[s] = k;
            }
            used = 0;
            int i = 0;
            foreach (Node part in craft.Children("PART"))
            {
                if (bySlot.TryGetValue(i, out int at) && Hardware.PartName(stored[at]) == Hardware.PartName(part))
                {
                    Hardware.Set(part, Hardware.Make(h.CraftId, at));
                    Hardware.CarryHardware(stored[at], part);
                    used++;
                }
                i++;
            }
            return craft;
        }

        IEnumerator Body()
        {
            // 1. A career game with the Kerbal X filed as a craft, and as a launcher.
            GameParameters gp = GameParameters.GetDefaultParameters(Game.Modes.CAREER, GameParameters.Preset.Normal);
            Game g = GamePersistence.CreateNewGame(SaveName, Game.Modes.CAREER, gp, "Squad/Flags/default", GameScenes.SPACECENTER, EditorFacility.VAB);
            HighLogic.CurrentGame = g;
            HighLogic.SaveFolder = SaveName;
            WriteLibrary(Path.Combine(KSPUtil.ApplicationRootPath, "saves", SaveName));
            g.startScene = GameScenes.SPACECENTER;
            g.Start();
            yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.SPACECENTER && PRSScenario.Instance != null && Funding.Instance != null, 120, "the career game reached the Space Center");
            yield return new WaitForSeconds(4f);
            Funding.Instance.SetFunds(10000000, TransactionReasons.Cheating);
            Library.Scan();
            Design d = BuildsApi.Designs().FirstOrDefault(x => x.Name == DesignName);
            Design dl = BuildsApi.Designs().FirstOrDefault(x => x.Name == LauncherName);
            Check(d != null && d.Kind == DesignKind.Craft && dl != null && dl.Kind == DesignKind.Launcher, "the Kerbal X is filed as the craft " + d?.Name + " and the launcher " + dl?.Name);
            if (d == null || dl == null) yield break;

            // 2. Build it in the VAB, all new.
            yield return ToEditor();
            EditorLogic.LoadShipFromFile(d.Path);
            yield return new WaitForSeconds(3f);
            typeof(BuildsApi).GetMethod("RaisePlaced", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { d, PlaceHow.Load, EditorLogic.fetch.ship.parts, null });
            yield return new WaitForSeconds(0.5f);
            int designParts = EditorLogic.fetch.ship.parts.Count;
            Check(!EditorLogic.fetch.ship.parts.Any(EditorAddon.IsHardware), "a design loaded from its file is all new parts");
            EditorAddon.Instance.OpenBuildPanel();
            BuildPanel panel = EditorAddon.Instance.Panel;
            Quote q1 = panel.Quote;
            Note("first build: " + designParts + " parts, new " + Pricing.Funds(q1.NewPrice) + ", fuel " + Pricing.Funds(q1.FuelPrice));
            Check(q1.NewPrice > 0 && q1.RefurbishPrice == 0 && panel.MainShip == null, "the first build is all new parts, a new ship");
            PRSScenario sc = PRSScenario.Instance;
            Check(panel.Confirm(), "building the first one " + panel.Error);
            string jobId = sc.Jobs.LastOrDefault()?.Id;
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            BuildJob job = sc.Jobs.FirstOrDefault(j => j.Id == jobId);
            string err = "";
            Check(job != null && sc.Outsource(job, out err), "outsourcing it " + err);
            HangarShip first = sc.Hangar.FirstOrDefault(h => h.Ready && BuildsLink.DesignOf(h) == d.Id);
            Check(first != null && first.DisplayName == DesignName + " #1" && first.Flights == 0, "it's ready in the hangar as " + DesignName + " #1, never flown (" + first?.DisplayName + ")");
            if (first == null) yield break;
            string shipId = first.Id, shipName = first.DisplayName;

            // 3. Flight 1: everything drops away; the piece with the pod comes home as the ship it launched as. Saved with
            // its booster, the booster is the ship's own (Phil: "restore would rebuild the booster").
            yield return Launch(first, shipName);
            LaunchRecord rec1 = OnThePad();
            Check(rec1 != null && rec1.ShipId == shipId && rec1.Slots.Count == designParts && rec1.Slots.All(s => s >= 0),
                  "its launch record lists the ship that went up, every part of it the ship's own");
            Check(PRSScenario.Instance.ShipOf(shipId)?.Flights == 1 && PRSScenario.Instance.HangarOf(shipId) == null, "launching counted its first flight, and it left the hangar");
            yield return FlyAndDrop("flight 1", shipId);
            sc = PRSScenario.Instance;
            HangarShip flown = sc.HangarOf(shipId);
            Check(flown != null && !flown.Ready && flown.Flights == 1 && BuildsLink.DesignOf(flown) == d.Id && flown.DisplayName == shipName,
                  "the pod is in the hangar as " + shipName + " again, a flown copy of the launcher (" + flown?.DisplayName + ", " + flown?.PartCount + " parts, " + flown?.Flights + " flights)");
            if (flown == null) yield break;
            int flownParts = flown.PartCount;
            BuildsLink.Restore whole = BuildsLink.RestoreOf(flown);
            Check(flown.Missing == designParts - flownParts && whole != null && whole.NewParts == flown.Missing && whole.Craft.Children("PART").Count() == designParts,
                  "it's missing the " + flown.Missing + " parts it dropped, and Restore rebuilds them, booster and all (" + whole?.NewParts + ")");
            Check(sc.Hangar.Count(h => BuildsLink.DesignOf(h) == d.Id) == 1, "nothing it dropped is in the hangar");
            int clampsInBin = sc.Bin.Where(k => k.Key.StartsWith("launchClamp")).Sum(k => k.Count);
            Note("after flight 1: hangar " + sc.Hangar.Count + ", parts bin " + BinCount(sc) + " (" + clampsInBin + " launch clamps)");
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            CopyRow row = BuildsLink.Instance.CopiesOf(d).FirstOrDefault(r => r.Id == shipId);
            Check(row != null && row.Text.Contains("1 flight"), "Phil's Builds lists it under its design (" + row?.Label + " " + row?.Text + ")");

            // Clicking the VAB now brings back, from KSP's cache, the ship that was there when it was built. That hardware
            // has launched since (the pod came home as a new copy of its parts), so in the VAB it's all new parts.
            var vabBuilding = FindObjectOfType<VehicleAssemblyBuilding>();
            Check(vabBuilding != null, "found the VAB at the Space Center");
            if (vabBuilding == null) yield break;
            typeof(VehicleAssemblyBuilding).GetMethod("OnClicked", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(vabBuilding, null);
            yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.EDITOR && EditorLogic.fetch != null && EditorAddon.Instance != null && EditorLogic.fetch.ship != null && EditorLogic.fetch.ship.parts.Count > 0, 90, "clicking the VAB brought back the ship from before");
            yield return new WaitForSeconds(3f);
            ShipConstruct cached = EditorLogic.fetch.ship;
            Note("back in the VAB: " + cached.parts.Count + " parts, " + cached.parts.Count(EditorAddon.IsHardware) + " counted as hardware");
            Check(!cached.parts.Any(EditorAddon.IsHardware), "none of its parts counts as hardware: the ship they were has flown since");
            EditorAddon.Instance.OpenBuildPanel();
            Check(EditorAddon.Instance.Panel.Quote.HangarShips.Count == 0 && EditorAddon.Instance.Panel.ReadyShip == null, "and the Build panel prices it as a new ship, not " + shipName);
            EditorAddon.Instance.Panel.Showing = false;
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            flown = sc.HangarOf(shipId);

            // 4. The flown pod opened in the VAB with a launcher built around it again (the player's work, done here from the
            // design file): the pod's own parts as its hardware, everything else new or from the bin.
            yield return ToEditor();
            sc = PRSScenario.Instance;
            flown = sc.HangarOf(shipId);
            Node around = AroundShip(KspNodes.FromKsp(ConfigNode.Load(d.Path)), d.Id, flown, out int used);
            Check(used == flownParts, "the launcher around the pod: " + used + " of its parts are the pod's own, " + (designParts - used) + " new");
            EditorAddon.LoadNoted(around, flown.Name, flown.CraftId, "reuse-test");
            yield return new WaitForSeconds(3f);
            ShipConstruct ship = EditorLogic.fetch.ship;
            Check(ship.parts.Count == designParts, "the whole launcher is in the VAB (" + ship.parts.Count + " parts)");
            Check(ship.parts.Count(EditorAddon.IsHardware) == flownParts, "the flown pod's " + flownParts + " parts are its hardware; the rest are new parts");
            EditorAddon.Instance.OpenBuildPanel();
            panel = EditorAddon.Instance.Panel;
            Quote q2 = panel.Quote;
            List<Node> craft2 = panel.Craft.Children("PART").ToList();
            double dryHangar = q2.Owners.Keys.Sum(i => KspCosts.Instance.Cost(craft2[i]).Dry);
            QuoteLine binLine = q2.Lines.FirstOrDefault(l => l.Kind == OwnerKind.Bin);
            double dryBin = binLine != null ? binLine.Items.Sum(i => KspCosts.Instance.Cost(i.Part).Dry) : 0;
            Note("second build: new " + Pricing.Funds(q2.NewPrice) + ", refurbish " + Pricing.Funds(q2.RefurbishPrice) + ", fuel " + Pricing.Funds(q2.FuelPrice) + "; the pod's parts " + Pricing.Funds(dryHangar) + ", " + q2.BinParts + " bin parts " + Pricing.Funds(dryBin));
            Check(q2.HangarShips.Contains(shipId) && q2.Owners.Count == flownParts, "the Build panel prices the flown pod as the hangar's own " + shipName);
            Check(Near(q2.NewPrice + dryHangar + dryBin, q1.NewPrice, 2), "new parts cost the design less the parts it already has (" + Pricing.Funds(q2.NewPrice) + " + " + Pricing.Funds(dryHangar) + " + " + Pricing.Funds(dryBin) + " = " + Pricing.Funds(q1.NewPrice) + ")");
            double podWear = flown.Wear;
            Note("the flown pod came home at " + Core.Wear.Percent(podWear) + " wear");
            Check(podWear > 0 && Near(q2.RefurbishPrice, dryHangar * Pricing.RefurbishShare(podWear, S), 2), "the pod's parts cost their refurbish by its wear; bin parts are already paid for (" + Pricing.Funds(q2.RefurbishPrice) + ")");
            Check(binLine == null || binLine.Price < 0.5, "bin parts cost nothing");
            Check(panel.MainShip?.Id == shipId && panel.KeepShipId == shipId && panel.Title == shipName, "the build starts from the pod, so it's " + shipName + " again (" + panel.Title + ")");
            Check(!panel.Unfiled, "no 'Not filed' warning");
            sc = PRSScenario.Instance;
            int binBeforeBuild = BinCount(sc);
            Check(panel.Confirm(), "building the second one " + panel.Error);
            BuildJob job2 = sc.Jobs.LastOrDefault();
            Check(job2 != null && sc.HangarOf(shipId).ReservedBy == job2.Id && job2.ShipId == shipId, "the build keeps " + shipName + " and has taken it");
            Check(BinCount(sc) == binBeforeBuild - q2.BinParts, "it took " + q2.BinParts + " parts from the bin (" + binBeforeBuild + " → " + BinCount(sc) + ")");
            Check(ship.parts.All(EditorAddon.IsHardware), "the ship left in the VAB is the build's parts now");
            string job2Id = job2?.Id;

            // 5. Built and flown again: the same pod comes home as the same ship, with two flights.
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            job2 = sc.Jobs.FirstOrDefault(j => j.Id == job2Id);
            Check(job2 != null && sc.Outsource(job2, out err), "outsourcing the second one " + err);
            HangarShip second = sc.HangarOf(shipId);
            Check(second != null && second.Ready && second.PartCount == designParts && second.DisplayName == shipName && second.Flights == 1 && sc.Hangar.Count(h => BuildsLink.DesignOf(h) == d.Id) == 1,
                  "built on the flown pod, it's still " + shipName + ", ready, with its one flight (" + second?.DisplayName + ")");
            if (second == null) yield break;
            int clampsOnPad = ShipConstruction.FindVesselsLandedAt(HighLogic.CurrentGame.flightState, "LaunchPad").Count;
            int binBefore = BinCount(sc);
            yield return Launch(second, shipName);
            sc = PRSScenario.Instance;
            if (clampsOnPad > 0) Check(BinCount(sc) > binBefore, "the first launch's clamps, still on the pad, were cleared into the parts bin (" + (BinCount(sc) - binBefore) + " parts)");
            Check(sc.ShipOf(shipId)?.Flights == 2, "on the pad, " + shipName + " is on its second flight");
            yield return FlyAndDrop("flight 2", shipId);
            sc = PRSScenario.Instance;
            HangarShip back = sc.HangarOf(shipId);
            Check(back != null && back.Flights == 2 && back.DisplayName == shipName, "the pod came home again as " + shipName + ", now with 2 flights (" + back?.DisplayName + ")");
            List<string> log = back != null ? sc.LogOf(back) : new List<string>();
            foreach (string l in log) Note("flight log: " + l);
            Check(log.Count == 2 && log.All(l => l.Contains(" · " + shipName + " · ")), "its flight log has both flights, both as " + shipName);
            if (back == null) yield break;

            // 6. Phil's play-test (Peleus III #2): the ship opened in the VAB, a launcher built under it again, and saved as
            // another design. It's still that ship (Ship of Theseus): its name, number, flights and log through build,
            // launch and recovery, and no new number used up. This time the stages under the pod are a launcher placed from
            // Phil's Builds (Phil: "if I have it saved as just the ship, and slap a Boulder 001 underneath it"): they aren't
            // the ship, so its blueprint is the pod piece alone, and they come home as nothing.
            const string NewDesign = "Lifter 010 II";
            yield return ToEditor();
            sc = PRSScenario.Instance;
            HangarShip pod = sc.HangarOf(shipId);
            EditorAddon.LoadHangarShip(pod);
            yield return new WaitForSeconds(3f);
            Check(EditorLogic.fetch.ship.parts.All(EditorAddon.IsHardware), shipName + " opened in the VAB: every part is its hardware");
            Node rebuilt = AroundShip(KspNodes.FromKsp(ConfigNode.Load(d.Path)), d.Id, pod, out int podParts);
            EditorAddon.LoadNoted(rebuilt, pod.Name, pod.CraftId, "reuse-refit");
            yield return new WaitForSeconds(3f);
            Check(EditorLogic.fetch.ship.parts.Count(EditorAddon.IsHardware) == podParts && EditorLogic.fetch.ship.parts.Count == designParts,
                  "a launcher built around it again: " + podParts + " of its parts are its own, the rest new");
            List<Part> vabParts = EditorLogic.fetch.ship.parts;
            Part under = DecouplerUnder(vabParts, vabParts[0]);
            List<Part> launcherParts = under != null ? Subtree(under) : new List<Part>();
            foreach (Part p in launcherParts) PRSPart.Of(p).design = dl.Id;
            int shipParts = designParts - launcherParts.Count;
            Check(under != null && launcherParts.All(p => !EditorAddon.IsHardware(p)) && shipParts == podParts, "the stages under the pod are " + LauncherName + ": " + launcherParts.Count + " launcher parts, all new");
            EditorLogic.fetch.ship.shipName = NewDesign;
            EditorAddon.Instance.OpenBuildPanel();
            panel = EditorAddon.Instance.Panel;
            Check(panel.MainShip?.Id == shipId && panel.KeepShipId == shipId && panel.Title == shipName,
                  "saved as " + NewDesign + ", it's still " + shipName + " (Phil: \"that ship is still the Peleus III #2\"), shown as " + panel.Title);
            panel.Chosen = BuildPanel.Pace.Outsource;
            Check(panel.Confirm(), "refitting it " + panel.Error);
            HangarShip refit = sc.HangarOf(shipId);
            Check(refit != null && refit.Ready && refit.DisplayName == shipName && refit.Flights == 2 && refit.Ship.DesignId == d.Id,
                  "the refit is still " + shipName + ", with its 2 flights, and still a " + DesignName + " (" + refit?.DisplayName + ")");
            Check(refit != null && refit.PartCount == designParts && refit.Ship.BlueprintParts == shipParts && refit.Slots.Count(s => s < 0) == launcherParts.Count && BuildsLink.Under(refit) == LauncherName,
                  "it's " + shipName + " on " + LauncherName + ": its blueprint is the " + shipParts + " parts of the ship, not the launcher (" + refit?.Ship.BlueprintParts + ", on " + BuildsLink.Under(refit) + ")");
            Check(!sc.Ships.Values.Any(x => x.Name == NewDesign) && BuildsLink.Instance.CopiesOf(d).Any(r => r.Id == shipId),
                  "no " + NewDesign + " number was used, and Phil's Builds still lists it under " + DesignName);
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            yield return Launch(sc.HangarOf(shipId), shipName + " built as " + NewDesign);
            Check(FlightGlobals.ActiveVessel.vesselName == shipName, "it flies as " + shipName + " (" + FlightGlobals.ActiveVessel.vesselName + ")");
            yield return FlyAndDrop("flight 3", shipId);
            sc = PRSScenario.Instance;
            back = sc.HangarOf(shipId);
            Check(back != null && back.DisplayName == shipName && back.Flights == 3, "it came home as " + shipName + ", 3 flights (" + back?.DisplayName + ")");
            Check(back != null && back.Missing == 0 && BuildsLink.RestoreOf(back) == null && sc.Hangar.Count(h => BuildsLink.DesignOf(h) == d.Id) == 1,
                  "the whole ship came home, missing nothing: the launcher it dropped isn't its own, and isn't a ship either (" + back?.Missing + " missing)");
            log = back != null ? sc.LogOf(back) : new List<string>();
            Check(log.Count == 3 && log.All(l => l.Contains(" · " + shipName + " · ")), "its flight log is one ship under one name: " + string.Join(" | ", log.ToArray()));
            if (back == null) yield break;

            // 7. Phil's play-test (the Kupe): a ship taken apart is gone. Its parts go into the bin knowing nothing about it,
            // a new build that uses them is a new ship, and that ship comes home as itself, never as the one taken apart.
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            back = sc.HangarOf(shipId);
            int inBin = BinCount(sc);
            double apart = sc.TakeApartPrice(back);
            double fundsBefore = Funds;
            Check(sc.TakeApart(back, out err), "taking " + shipName + " apart " + err);
            Check(BinCount(sc) == inBin + flownParts && sc.HangarOf(shipId) == null, "its " + flownParts + " parts went to the parts bin");
            Check(apart > 0 && Near(fundsBefore - Funds, apart, 1), "refurbishing them for the bin was paid then (" + Pricing.Funds(apart) + ")");
            Check(sc.Bin.All(k => Hardware.Module(k.Sample)?.Get(Hardware.Field) == null && k.Sample.Children("RESOURCE").All(r => r.Get("amount") == "0")),
                  "bin parts carry no ship and no fuel");
            yield return ToSpaceCenter();   // the Space Center tidies away ships that no longer exist
            sc = PRSScenario.Instance;
            Check(sc.ShipOf(shipId) == null, shipName + " no longer exists anywhere");
            // (A new career hasn't researched or bought the Kerbal X's parts, nor upgraded the VAB and launch pad for it; a
            // Space Center build checks all that, so do it all.)
            foreach (ScenarioUpgradeableFacilities.ProtoUpgradeable pu in ScenarioUpgradeableFacilities.protoUpgradeables.Values)
                foreach (Upgradeables.UpgradeableFacility uf in pu.facilityRefs) uf.SetLevel(uf.MaxLevel);
            foreach (ProtoRDNode rd in AssetBase.RnDTechTree.GetTreeNodes())
            {
                ProtoTechNode t = rd.tech;
                t.state = RDTech.State.Available;
                t.partsPurchased = PartLoader.LoadedPartsList.Where(ap => ap.TechRequired == t.techID).ToList();
                ResearchAndDevelopment.Instance.SetTechState(t.techID, t);
            }
            BuildsLink.LibraryBuild item = BuildsLink.Library().FirstOrDefault(i => i.Design.Id == d.Id);
            Check(item != null && item.Quote.BinParts >= flownParts, "a new " + DesignName + " from Phil's Builds uses the bin's parts (" + item?.Quote.BinParts + ")");
            BuildJob job3 = item != null ? BuildsLink.BuildFromLibrary(item, false, true, out err) : null;
            Check(job3 != null, "building it " + err);
            HangarShip fresh = sc.Hangar.FirstOrDefault(h => h.Ready && BuildsLink.DesignOf(h) == d.Id);
            Check(fresh != null && fresh.Id != shipId && fresh.DisplayName == DesignName + " #2" && fresh.Flights == 0 && sc.LogOf(fresh).Count == 0,
                  "it's a new ship, " + DesignName + " #2, never flown, with an empty log (" + fresh?.DisplayName + ")");
            if (fresh == null) yield break;
            string freshId = fresh.Id;
            yield return Launch(fresh, fresh.DisplayName);
            yield return FlyAndDrop("flight 4", freshId);
            sc = PRSScenario.Instance;
            HangarShip home = sc.HangarOf(freshId);
            Check(home != null && home.DisplayName == DesignName + " #2" && home.Flights == 1 && !sc.Hangar.Any(h => h.DisplayName == shipName),
                  "it came home as " + DesignName + " #2 on its first flight, not as " + shipName + " (" + home?.DisplayName + ")");
        }
    }
}
