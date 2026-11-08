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
    /// Phil's Builds as a launcher. It is built and flown; high over Kerbin everything drops away and its crew pod
    /// comes home on its chute to the hangar. That flown piece is picked up as the whole design (the flown pod
    /// plus everything else new, with matching parts from the bin), priced at their refurbish (by wear) for the flown parts, built,
    /// and flown again: the same pod comes home a second time as the same ship, with two flights in its log.
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

        static void WriteLibrary(string saveDir)
        {
            string subs = Path.Combine(saveDir, "Subassemblies");
            Directory.CreateDirectory(subs);
            string text = new Regex(@"^ship = .*$", RegexOptions.Multiline).Replace(File.ReadAllText(KerbalX), "ship = " + DesignName, 1);
            File.WriteAllText(Path.Combine(subs, DesignName + ".craft"), text);
            File.WriteAllText(Path.Combine(saveDir, "PhilsBuilds.cfg"),
                "PHILS_BUILDS\n{\n\tSUBASSEMBLY\n\t{\n\t\tfile = " + DesignName + "\n\t\tkind = Launcher\n\t\tfamily = Lifter\n\t\tstyle = Roman\n\t\tmark = 1\n\t\trating = 10\n\t\twidth = 2.5\n\t}\n}\n");
        }

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

        List<string> podSerials = new List<string>();

        /// <summary>
        /// On the pad: up to 20 km over Kerbin and falling, drop the boosters and the pod, fly the core off to orbit,
        /// and wait for KSP to delete the falling pieces (the pod comes home on its chute).
        /// </summary>
        IEnumerator FlyAndDrop(string flight)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            CelestialBody home = v.mainBody;
            Part pod = v.rootPart;
            Part decoupler = v.parts.FirstOrDefault(p => p.FindModuleImplementing<ModuleDecouple>() != null && Subtree(p).Count < v.parts.Count && p.parent != null && (p.parent == pod || p.parent.parent == pod));
            Check(decoupler != null, flight + ": found the decoupler under the crew pod");
            if (decoupler == null) yield break;
            var podPiece = v.parts.Where(p => !Subtree(decoupler).Contains(p) && !IsBooster(p)).ToList();
            podSerials = podPiece.Select(PRSPart.SerialOf).Where(s => s != null).ToList();
            foreach (Part p in v.parts) if (p.Rigidbody != null) p.Rigidbody.isKinematic = false;
            foreach (LaunchClamp lc in v.FindPartModulesImplementing<LaunchClamp>()) lc.Release();
            yield return new WaitForSeconds(1f);
            double ra = home.Radius + 20000, rp = 60000;
            FlightGlobals.fetch.SetShipOrbit(FlightGlobals.Bodies.IndexOf(home), (ra - rp) / (ra + rp), (ra + rp) / 2, 0, 0, Math.PI, 0, Planetarium.GetUniversalTime());
            yield return new WaitForSeconds(5f);
            v = FlightGlobals.ActiveVessel;
            Check(!v.LandedOrSplashed && v.altitude > 10000, flight + ": flying high over Kerbin (" + Math.Round(v.altitude) + " m)");
            var boosterSerials = v.parts.Where(IsBooster).Select(PRSPart.SerialOf).ToList();
            foreach (ModuleAnchoredDecoupler d in v.FindPartModulesImplementing<ModuleAnchoredDecoupler>().ToList()) if (!d.isDecoupled) d.Decouple();
            ModuleDecouple sd = decoupler.FindModuleImplementing<ModuleDecouple>();
            if (sd != null && !sd.isDecoupled) sd.Decouple();
            yield return new WaitForSeconds(1.5f);
            Vessel core = FlightGlobals.Vessels.Where(x => x != null && x.loaded && x != FlightGlobals.ActiveVessel && !x.parts.Any(p => podSerials.Contains(PRSPart.SerialOf(p))) && !x.parts.Any(p => boosterSerials.Contains(PRSPart.SerialOf(p))))
                                       .OrderByDescending(x => x.parts.Count).FirstOrDefault();
            Check(core != null, flight + ": the core stage separated from the pod");
            if (core == null) yield break;
            FlightGlobals.ForceSetActiveVessel(core);
            yield return new WaitForSeconds(3f);
            FlightGlobals.fetch.SetShipOrbit(FlightGlobals.Bodies.IndexOf(home), 0, home.Radius + 150000, 0, 0, 0, 0, Planetarium.GetUniversalTime());
            yield return WaitFor(() => PRSScenario.Instance != null && PRSScenario.Instance.Hangar.Any(h => !h.Ready && h.Placed.Any(podSerials.Contains)), 90, flight + ": the pod came home");
            yield return new WaitForSeconds(2f);
        }

        IEnumerator Body()
        {
            // 1. A career game with the Kerbal X filed as a launcher.
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
            Check(d != null && d.Kind == DesignKind.Launcher, "the Kerbal X is filed as the launcher " + d?.Name);
            if (d == null) yield break;

            // 2. Build it in the VAB, all new.
            yield return ToEditor();
            EditorLogic.LoadShipFromFile(d.Path);
            yield return new WaitForSeconds(3f);
            typeof(BuildsApi).GetMethod("RaisePlaced", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { d, PlaceHow.Load, EditorLogic.fetch.ship.parts, null });
            yield return new WaitForSeconds(0.5f);
            int designParts = EditorLogic.fetch.ship.parts.Count;
            EditorAddon.Instance.OpenBuildPanel();
            BuildPanel panel = EditorAddon.Instance.Panel;
            Quote q1 = panel.Quote;
            Note("first build: " + designParts + " parts, new " + Pricing.Funds(q1.NewPrice) + ", fuel " + Pricing.Funds(q1.FuelPrice));
            Check(q1.NewPrice > 0 && q1.RefurbishPrice == 0, "the first build is all new parts");
            PRSScenario sc = PRSScenario.Instance;
            Check(panel.Confirm(), "building the first one " + panel.Error);
            string jobId = sc.Jobs.LastOrDefault()?.Id;
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            BuildJob job = sc.Jobs.FirstOrDefault(j => j.Id == jobId);
            string err = "";
            Check(job != null && sc.Outsource(job, out err), "outsourcing it " + err);
            HangarShip first = sc.Hangar.FirstOrDefault(h => h.Ready && BuildsLink.DesignOf(h) == d.Id);
            Check(first != null, "it's ready in the hangar (" + first?.DisplayName + ")");
            if (first == null) yield break;

            // 3. Flight 1: everything drops away; the pod comes home on its chute.
            Check(sc.LaunchReady(first, out err), "launching " + first.DisplayName + " " + err);
            yield return WaitFor(() => HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.loaded && !FlightGlobals.ActiveVessel.packed, 120, "it reached the pad");
            yield return new WaitForSeconds(3f);
            yield return FlyAndDrop("flight 1");
            sc = PRSScenario.Instance;
            HangarShip flown = sc.Hangar.FirstOrDefault(h => !h.Ready && h.Placed.Any(podSerials.Contains));
            Check(flown != null && flown.Flights == 1 && BuildsLink.DesignOf(flown) == d.Id, "the pod is in the hangar as a flown copy of the launcher (" + flown?.DisplayName + ", " + flown?.Placed.Count + " parts)");
            if (flown == null) yield break;
            string flownId = flown.Id, flownName = flown.DisplayName;
            var flownSerials = flown.Placed.ToList();
            int clampsInBin = sc.Bin.Count(b => (b.Part ?? "").StartsWith("launchClamp"));
            Note("after flight 1: hangar " + sc.Hangar.Count + ", parts bin " + sc.Bin.Count + " (" + clampsInBin + " launch clamps)");
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            flown = sc.Hangar.FirstOrDefault(h => h.Id == flownId);
            CopyRow row = BuildsLink.Instance.CopiesOf(d).FirstOrDefault(r => r.Id == flownId);
            Check(row != null && row.Text.Contains("1 flight"), "Phil's Builds lists it under the launcher (" + row?.Label + " " + row?.Text + ")");

            // Clicking the VAB now brings back the ship just launched, from KSP's cache. Its parts still flying (the core
            // stage in orbit) must get new serials; only the pod, which is hangar hardware again, keeps its own.
            var flying = new HashSet<string>(HighLogic.CurrentGame.flightState.protoVessels.SelectMany(pv => pv.protoPartSnapshots)
                .Select(ps => ps.modules.FirstOrDefault(m => m.moduleName == Core.Craft.ModuleName)?.moduleValues?.GetValue("serial")).Where(s => !string.IsNullOrEmpty(s)));
            var vabBuilding = FindObjectOfType<VehicleAssemblyBuilding>();
            Check(vabBuilding != null, "found the VAB at the Space Center");
            if (vabBuilding == null) yield break;
            typeof(VehicleAssemblyBuilding).GetMethod("OnClicked", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(vabBuilding, null);
            yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.EDITOR && EditorLogic.fetch != null && EditorAddon.Instance != null && EditorLogic.fetch.ship != null && EditorLogic.fetch.ship.parts.Count > 0, 90, "clicking the VAB brought back the launched ship");
            yield return new WaitForSeconds(3f);
            var backInVab = EditorLogic.fetch.ship.parts.Select(PRSPart.SerialOf).Where(s => s != null).ToList();
            Note("back in the VAB: " + backInVab.Count + " parts; " + flying.Count + " serials still flying");
            Check(flying.Count > 0 && !backInVab.Any(flying.Contains), "no part in the VAB shares a serial with a part still flying (" + backInVab.Count(flying.Contains) + " shared)");
            Check(flownSerials.All(backInVab.Contains), "the pod, back in the hangar, keeps its own serials");
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            flown = sc.Hangar.FirstOrDefault(h => h.Id == flownId);

            // 4. Picked up as the whole design: the flown pod plus everything else new.
            CompletionResult c = BuildsLink.Complete(d, flown);
            Check(c != null && c.Used.Count == flownSerials.Count && c.NewSlots.Count == designParts - flownSerials.Count, "completed to the design: " + c?.Used.Count + " flown parts and " + c?.NewSlots.Count + " new");
            if (c == null) yield break;
            yield return ToEditor();
            EditorAddon.LoadKeepingSerials(c.Craft, d.Name, c.Used, "reuse-test");
            yield return new WaitForSeconds(3f);
            ShipConstruct ship = EditorLogic.fetch.ship;
            Check(ship.parts.Count == designParts, "the whole launcher is in the VAB (" + ship.parts.Count + " parts)");
            Check(ship.parts.Count(p => flownSerials.Contains(PRSPart.SerialOf(p)) && PRSPart.Of(p).flights == 1) == flownSerials.Count, "the flown pod's parts kept their serials and their flight");
            EditorAddon.Instance.OpenBuildPanel();
            panel = EditorAddon.Instance.Panel;
            Quote q2 = panel.Quote;
            var reusedEditorSerials = new HashSet<string>(c.Used.Concat(q2.BinSwaps.Keys));
            double dryReused = panel.Craft.Children("PART").Where(p => reusedEditorSerials.Contains(Core.Craft.SerialOf(p))).Sum(p => KspCosts.Instance.Cost(p).Dry);
            Note("second build: new " + Pricing.Funds(q2.NewPrice) + ", refurbish " + Pricing.Funds(q2.RefurbishPrice) + ", fuel " + Pricing.Funds(q2.FuelPrice) + "; reused parts' price " + Pricing.Funds(dryReused) + " (" + q2.BinSwaps.Count + " from the bin)");
            Check(q2.HangarShips.Contains(flownId), "the Build panel prices the flown pod as the hangar's own " + flownName);
            Check(Near(q2.NewPrice + dryReused, q1.NewPrice, 2), "new parts cost the design less the reused parts (" + Pricing.Funds(q2.NewPrice) + " + " + Pricing.Funds(dryReused) + " = " + Pricing.Funds(q1.NewPrice) + ")");
            // Each reused part costs its price × its craft's wear × half: the pod's wear from its flight, a bin part's its own.
            double wantRefurbish = panel.Craft.Children("PART").Where(p => reusedEditorSerials.Contains(Core.Craft.SerialOf(p))).Sum(p =>
                KspCosts.Instance.Cost(p).Dry * Pricing.RefurbishShare(q2.BinSwaps.TryGetValue(Core.Craft.SerialOf(p), out Owner bo) ? bo.Wear : Core.Wear.In(p.Children("MODULE").FirstOrDefault(m => m.Get("name") == Core.Craft.ModuleName)), S));
            double podWear = PRSScenario.Instance.Hangar.First(h => h.Id == flownId).Wear;
            Note("the flown pod came home at " + Core.Wear.Percent(podWear) + " wear");
            Check(podWear > 0 && Near(q2.RefurbishPrice, wantRefurbish, 2), "the reused parts cost their refurbish by wear (" + Pricing.Funds(q2.RefurbishPrice) + " of " + Pricing.Funds(wantRefurbish) + ")");
            Check(panel.HangarCopy == null && !panel.Unfiled, "no 'Use it' offer and no 'Not filed' warning");
            sc = PRSScenario.Instance;
            Check(panel.Confirm(), "building the second one " + panel.Error);
            BuildJob job2 = sc.Jobs.LastOrDefault();
            Check(job2 != null && sc.Hangar.First(h => h.Id == flownId).ReservedBy == job2.Id, "the flown pod is reserved by the build");
            string job2Id = job2?.Id;

            // 5. Built and flown again: the same pod comes home as the same ship, with two flights.
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            job2 = sc.Jobs.FirstOrDefault(j => j.Id == job2Id);
            Check(job2 != null && sc.Outsource(job2, out err), "outsourcing the second one " + err);
            HangarShip second = sc.Hangar.FirstOrDefault(h => h.Ready && h.Placed.Any(flownSerials.Contains));
            Check(second != null && second.Placed.Count == designParts, "the second one is ready, with the flown pod in it (" + second?.DisplayName + ")");
            // The pod is the launcher's top (its root part), so the rebuilt launcher is still that ship.
            Check(second != null && second.Id == flownId && second.DisplayName == flownName && sc.Hangar.Count(h => h.Placed.Any(flownSerials.Contains)) == 1, "built on the flown pod, it's still " + flownName + ", not a new ship (" + second?.DisplayName + ")");
            if (second == null) yield break;
            int clampsOnPad = ShipConstruction.FindVesselsLandedAt(HighLogic.CurrentGame.flightState, "LaunchPad").Count;
            int binBefore = sc.Bin.Count;
            bool launched = sc.LaunchReady(second, out err);
            Check(launched, "launching " + second.DisplayName + " " + err);
            if (!launched) yield break;
            if (clampsOnPad > 0) Check(sc.Bin.Count > binBefore, "the first launch's clamps, still on the pad, were cleared into the parts bin (" + (sc.Bin.Count - binBefore) + " parts)");
            yield return WaitFor(() => HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.loaded && !FlightGlobals.ActiveVessel.packed, 120, "it reached the pad");
            yield return new WaitForSeconds(3f);
            Vessel v2 = FlightGlobals.ActiveVessel;
            Check(v2.parts.Where(p => flownSerials.Contains(PRSPart.SerialOf(p))).All(p => PRSPart.Of(p).flights == 2), "on the pad, the reused pod is on its second flight");
            Check(v2.parts.Where(p => !flownSerials.Contains(PRSPart.SerialOf(p))).All(p => PRSPart.Of(p).flights <= 2) && v2.parts.Count(p => PRSPart.Of(p).flights == 1) > 0, "and the new parts on their first");
            yield return FlyAndDrop("flight 2");
            sc = PRSScenario.Instance;
            HangarShip back = sc.Hangar.FirstOrDefault(h => !h.Ready && h.Placed.Any(flownSerials.Contains));
            Check(back != null && back.Flights == 2, "the pod came home again, now with 2 flights (" + back?.DisplayName + ")");
            Check(back != null && back.DisplayName == flownName, "as the same ship it was (" + back?.DisplayName + ", first came home as " + flownName + ")");
            List<string> log = back != null ? sc.LogOf(back) : new List<string>();
            foreach (string l in log) Note("flight log: " + l);
            Check(log.Count >= 2, "its flight log has both flights (" + log.Count + ")");
        }
    }
}
