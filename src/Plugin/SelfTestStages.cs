using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PhilsReusabilitySuite.Core;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// An unattended in-game check of build step 4, stage recovery, started by the test harness through the
    /// hidden PRSSelfTestStages event on the stock Kerbal X. It checks the landing estimates, then lifts the
    /// ship to 20 km, drops the six radial boosters (no chutes: lost) and the crew pod (big chute: comes home),
    /// switches to the core stage and moves it far away, so KSP deletes the falling pieces and the suite
    /// recovers them.
    /// </summary>
    class SelfTestStages : MonoBehaviour
    {
        static bool running;
        int pass, fail;

        public static void Begin()
        {
            if (running) return;
            running = true;
            var go = new GameObject("PRSSelfTestStages");
            DontDestroyOnLoad(go);
            go.AddComponent<SelfTestStages>();
        }

        void Check(bool ok, string what)
        {
            if (ok) { pass++; Debug.Log("[PRS-TEST] PASS " + what); }
            else { fail++; Debug.LogError("[PRS-TEST] FAIL " + what); }
        }

        static void Note(string s) { Debug.Log("[PRS-TEST] " + s); }

        void Start() { StartCoroutine(Run()); }

        IEnumerator Run()
        {
            // The test craft are stock ships, not filed in Phil's Builds: let their pieces come home to the hangar.
            bool designRule = Suite.Settings.RequireDesign;
            Suite.Settings.RequireDesign = false;
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

        static List<Part> Subtree(Part p)
        {
            var list = new List<Part> { p };
            foreach (Part c in p.children) list.AddRange(Subtree(c));
            return list;
        }

        static double Mass(IEnumerable<Part> parts) => parts.Sum(p => p.mass + p.Resources.Cast<PartResource>().Sum(r => r.amount * r.info.density));

        IEnumerator Body()
        {
            yield return new WaitForSeconds(1f);
            Check(StageReturns.Instance != null && !StageReturns.StageRecoveryInstalled, "the suite's stage recovery is on (StageRecovery isn't installed)");
            Vessel v = FlightGlobals.ActiveVessel;
            CelestialBody home = v.mainBody;
            double rho = home.atmDensityASL;

            // 1. Landing estimates from live parts.
            Part pod = v.rootPart;
            Part decoupler = v.parts.FirstOrDefault(p => p.FindModuleImplementing<ModuleDecouple>() != null && Subtree(p).Count < v.parts.Count && p.parent != null && (p.parent == pod || p.parent.parent == pod));
            Check(decoupler != null, "found the decoupler under the crew pod");
            if (decoupler == null) yield break;
            var podPiece = v.parts.Where(p => !Subtree(decoupler).Contains(p) && !IsBooster(p)).ToList();
            Part chute = podPiece.FirstOrDefault(p => p.FindModuleImplementing<ModuleParachute>() != null);
            var weightsBefore = chute != null ? chute.DragCubes.Cubes.Select(c => c.Weight).ToList() : null;
            StageReturns.Profile podProf = StageReturns.Measure(podPiece);
            double podVt = Landing.TerminalSpeed(Mass(podPiece), podProf.CdA, rho);
            Note("pod piece: " + podPiece.Count + " parts, " + Mass(podPiece).ToString("0.00") + " t, drag " + podProf.CdA.ToString("0.0") + ", " + podProf.Chutes + " chute(s), " + podVt.ToString("0.0") + " m/s");
            Check(podProf.Chutes >= 1 && podVt > 3 && podVt < 12, "the crew pod comes down safely on its big chute (" + podVt.ToString("0.0") + " m/s)");
            Check(chute == null || weightsBefore.SequenceEqual(chute.DragCubes.Cubes.Select(c => c.Weight)), "measuring left the real parachute's drag state as it was");

            Part radial = v.parts.FirstOrDefault(p => p.FindModuleImplementing<ModuleAnchoredDecoupler>() != null);
            List<Part> booster = radial != null ? Subtree(radial) : new List<Part>();
            StageReturns.Profile bProf = StageReturns.Measure(booster);
            double bVt = Landing.TerminalSpeed(Mass(booster), bProf.CdA, rho);
            Note("booster: " + booster.Count + " parts, " + Mass(booster).ToString("0.00") + " t, drag " + bProf.CdA.ToString("0.0") + ", " + bVt.ToString("0.0") + " m/s");
            Check(bProf.Chutes == 0 && bVt > Landing.LostSpeed, "a booster with no chutes falls far too fast to survive (" + bVt.ToString("0") + " m/s)");

            // 2. Up to 20 km, falling: an orbit whose highest point is 20 km up, with the ship at that point.
            foreach (Part p in v.parts) if (p.Rigidbody != null) p.Rigidbody.isKinematic = false;
            foreach (LaunchClamp lc in v.FindPartModulesImplementing<LaunchClamp>()) lc.Release();
            yield return new WaitForSeconds(1f);
            double ra = home.Radius + 20000, rp = 60000;
            FlightGlobals.fetch.SetShipOrbit(FlightGlobals.Bodies.IndexOf(home), (ra - rp) / (ra + rp), (ra + rp) / 2, 0, 0, Math.PI, 0, Planetarium.GetUniversalTime());
            yield return new WaitForSeconds(5f);
            v = FlightGlobals.ActiveVessel;
            Note("after the move: " + v.situation + ", " + Math.Round(v.altitude) + " m, " + Math.Round(v.srfSpeed) + " m/s, packed=" + v.packed);
            Check(!v.LandedOrSplashed && v.altitude > 10000 && v.altitude < 40000, "the ship is flying high over Kerbin (" + Math.Round(v.altitude) + " m)");

            // The VAB's landing check measures a piece "on its own" while it's still part of the ship. Measure the
            // pod piece and a booster that way now, to compare with the real pieces once they've dropped away.
            double podAlone = StageReturns.MeasureAlone(podPiece).CdA;
            List<Part> oneBooster = radial != null ? Subtree(radial) : new List<Part>();
            double boosterAlone = oneBooster.Count > 0 ? StageReturns.MeasureAlone(oneBooster).CdA : 0;
            uint boosterId = oneBooster.Select(p => p.flightID).FirstOrDefault();

            // 3. Drop the boosters and the pod, then switch to the core and move it far away.
            var podIds = podPiece.Select(p => p.flightID).ToList();
            var boosterIds = v.parts.Where(IsBooster).Select(p => p.flightID).ToList();
            string podRootName = podPiece.Count > 0 ? podPiece[0].partInfo.name : "";
            int hangarBefore = PRSScenario.Instance.Hangar.Count;
            // It's the launch of a hangar ship, Kerbal X #1, whose blueprint is the whole craft (as if it had been built
            // and launched from the hangar): the piece with its pod is the ship coming home.
            LaunchRecord rec = PRSScenario.Instance.RecordOf(v.parts[0].launchID, v.parts[0].flightID);
            Check(rec != null, "the launch was recorded");
            if (rec == null) yield break;
            Ship kx = PRSScenario.Instance.NewShip("Kerbal X");
            kx.Blueprint = Hardware.Plain(rec.Craft.Clone());
            kx.Flights = 1;
            rec.ShipId = kx.Id;
            for (int i = 0; i < rec.Slots.Count; i++) rec.Slots[i] = i;
            foreach (ModuleAnchoredDecoupler d in v.FindPartModulesImplementing<ModuleAnchoredDecoupler>().ToList()) if (!d.isDecoupled) d.Decouple();
            ModuleDecouple sd = decoupler.FindModuleImplementing<ModuleDecouple>();
            if (sd != null && !sd.isDecoupled) sd.Decouple();
            yield return new WaitForSeconds(1.5f);
            Vessel core = FlightGlobals.Vessels.Where(x => x != null && x.loaded && x != FlightGlobals.ActiveVessel && !x.parts.Any(p => podIds.Contains(p.flightID)) && !x.parts.Any(p => boosterIds.Contains(p.flightID)))
                                       .OrderByDescending(x => x.parts.Count).FirstOrDefault();
            Check(core != null, "the core stage separated from the pod");
            if (core == null) yield break;
            Vessel boosterVessel = FlightGlobals.Vessels.FirstOrDefault(x => x != null && x.loaded && x.parts.Any(p => p.flightID == boosterId));
            StageReturns.Profile boosterReal = boosterVessel != null ? StageReturns.MeasuredFor(boosterVessel.persistentId) : null;
            Note("booster drag: measured on its own before dropping " + boosterAlone.ToString("0.00") + ", after dropping " + (boosterReal != null ? boosterReal.CdA.ToString("0.00") : "not measured"));
            Check(boosterReal != null && Math.Abs(boosterAlone - boosterReal.CdA) <= boosterReal.CdA * 0.15, "a booster measured on its own before it drops reads the same as after (" + boosterAlone.ToString("0.00") + " vs " + (boosterReal?.CdA.ToString("0.00") ?? "none") + ")");
            uint podId = FlightGlobals.ActiveVessel != null ? FlightGlobals.ActiveVessel.persistentId : 0;   // the pod stays the ship being flown
            FlightGlobals.ForceSetActiveVessel(core);
            yield return new WaitForSeconds(3f);
            Check(FlightGlobals.ActiveVessel == core, "switched to the core stage");
            FlightGlobals.fetch.SetShipOrbit(FlightGlobals.Bodies.IndexOf(home), 0, home.Radius + 150000, 0, 0, 0, 0, Planetarium.GetUniversalTime());
            Note("moved the core stage to orbit; waiting for KSP to delete the falling pieces");

            // 4. KSP deletes the pieces left falling out of range; the suite decides each landing.
            float t0 = Time.realtimeSinceStartup;
            while (StageReturns.Instance != null && StageReturns.Instance.Outcomes.Count < 7 && Time.realtimeSinceStartup - t0 < 90) yield return null;
            List<string> outcomes = StageReturns.Instance != null ? StageReturns.Instance.Outcomes.ToList() : new List<string>();
            foreach (string o in outcomes) Note("outcome: " + o);
            Check(outcomes.Count >= 7, "all seven falling pieces were handled (" + outcomes.Count + ")");
            Check(outcomes.Count(o => o.Contains("was lost")) >= 6, "the six boosters without chutes were lost (" + outcomes.Count(o => o.Contains("was lost")) + ")");
            PRSScenario sc = PRSScenario.Instance;
            List<HangarShip> cameHome = sc.Hangar.Skip(hangarBefore).ToList();
            HangarShip podShip = cameHome.FirstOrDefault(h => h.Craft.Children("PART").Any(p => Hardware.PartName(p) == podRootName));
            Check(podShip != null && podShip.Id == kx.Id, "the crew pod came home to the hangar as the ship, " + kx.DisplayName + " (" + podShip?.DisplayName + ")");
            Check(outcomes.Any(o => o.Contains(kx.DisplayName + " came home")), "its message says it came home");
            Check(podShip == null || (podShip.Flights == 1 && podShip.Missing == kx.BlueprintParts - podShip.PartCount && podShip.Missing > 0),
                  "it has 1 flight, and it's missing the stages it dropped (" + podShip?.Missing + ")");
            Check(cameHome.Count == 1, "no lost booster is in the hangar: only the pod came home (" + cameHome.Count + ")");

            // The pod was measured for real when it went out of range (it was the ship being flown when it dropped).
            StageReturns.Profile podReal = StageReturns.MeasuredFor(podId);
            Note("pod drag: measured on its own before dropping " + podAlone.ToString("0.00") + ", after dropping " + (podReal != null ? podReal.CdA.ToString("0.00") : "not measured"));
            Check(podReal != null && Math.Abs(podAlone - podReal.CdA) <= podReal.CdA * 0.15, "the pod measured on its own before it drops reads the same as after (" + podAlone.ToString("0.00") + " vs " + (podReal?.CdA.ToString("0.00") ?? "none") + ")");

            // What the player was told as the pieces dropped away.
            List<string> forecasts = StageReturns.Instance != null ? StageReturns.Instance.Forecasts.ToList() : new List<string>();
            foreach (string f in forecasts) Note("forecast: " + f);
            Check(forecasts.Any(f => f.StartsWith("6 × ") && f.Contains("won't survive")), "dropping the six boosters gave one message: they won't survive");
            Check(forecasts.Any(f => f.StartsWith(kx.DisplayName + " will come home")), "the pod's forecast said " + kx.DisplayName + " will come home");
        }

        static bool IsBooster(Part p)
        {
            for (Part a = p; a != null; a = a.parent)
                if (a.FindModuleImplementing<ModuleAnchoredDecoupler>() != null) return true;
            return false;
        }
    }
}
