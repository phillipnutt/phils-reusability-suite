using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PhilsReusabilitySuite.Core;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// An unattended in-game check of a powered landing, started by the test harness through the hidden
    /// PRSSelfTestPowered event on the stock Z-MAP Satellite Launch Kit. High over Kerbin every stage drops away;
    /// the satellite on top is flown off to orbit, leaving the middle stage (its own QBE probe core, a Terrier and
    /// its tanks, no chutes) to fall out of range. The suite should say that it will come home with a landing burn,
    /// then land it that way, using up the propellant the burn takes, and keep it in the hangar.
    /// </summary>
    class SelfTestPowered : MonoBehaviour
    {
        static bool running;
        int pass, fail;

        public static void Begin()
        {
            if (running) return;
            running = true;
            var go = new GameObject("PRSSelfTestPowered");
            DontDestroyOnLoad(go);
            go.AddComponent<SelfTestPowered>();
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
            // The stock craft isn't filed in Phil's Builds: let its pieces come home to the hangar.
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

        static double Propellant(IEnumerable<ProtoPartSnapshot> parts) =>
            parts.SelectMany(ps => ps.resources).Where(r => r.resourceName == "LiquidFuel" || r.resourceName == "Oxidizer").Sum(r => r.amount);

        IEnumerator Body()
        {
            yield return new WaitForSeconds(1f);
            Check(StageReturns.Instance != null && !StageReturns.StageRecoveryInstalled, "the suite's stage recovery is on");
            Vessel v = FlightGlobals.ActiveVessel;
            CelestialBody home = v.mainBody;
            Check(v.FindPartModulesImplementing<ModuleDecouple>().Count >= 2, "the Z-MAP has its stage decouplers");

            // As if launched from the hangar as "Z-MAP Test #1": its launch record lists that ship, riding on the root part
            // (the satellite on top). A stage that comes home on its own carries no listed ship's root part: a new ship.
            PRSScenario sc0 = PRSScenario.Instance;
            LaunchRecord launchRec = sc0.RecordOf(v.parts[0].launchID, v.parts[0].flightID);
            Check(launchRec != null, "the Z-MAP's launch was recorded");
            Ship zmap = sc0.NewShip("Z-MAP Test");
            if (launchRec != null)
            {
                launchRec.Ships.Add(new KeyValuePair<string, int>(zmap.Id, Core.Craft.Read(launchRec.Craft).RootPart.Index));
                for (int i = 0; i < launchRec.Parts.Count; i++) launchRec.Members[i] = zmap.Id;
                zmap.Flights = 1;
            }
            int hangarBefore = sc0.Hangar.Count;

            // Up to 20 km, falling, as in the stage-recovery test.
            foreach (Part p in v.parts) if (p.Rigidbody != null) p.Rigidbody.isKinematic = false;
            yield return new WaitForSeconds(1f);
            double ra = home.Radius + 20000, rp = 60000;
            FlightGlobals.fetch.SetShipOrbit(FlightGlobals.Bodies.IndexOf(home), (ra - rp) / (ra + rp), (ra + rp) / 2, 0, 0, Math.PI, 0, Planetarium.GetUniversalTime());
            yield return new WaitForSeconds(5f);
            v = FlightGlobals.ActiveVessel;
            Check(!v.LandedOrSplashed && v.altitude > 10000, "the Z-MAP is flying high over Kerbin (" + Math.Round(v.altitude) + " m)");

            // Drop every stage. The satellite on top stays the ship being flown and goes to orbit; the middle stage,
            // with its own probe core, Terrier engine and tanks, is left falling with the stage below it.
            foreach (ModuleDecouple dm in v.FindPartModulesImplementing<ModuleDecouple>().ToList()) if (!dm.isDecoupled) dm.Decouple();
            yield return new WaitForSeconds(1.5f);
            Vessel top = FlightGlobals.ActiveVessel;
            Vessel crane = FlightGlobals.Vessels.FirstOrDefault(x => x != null && x.loaded && x != top
                && x.parts.Any(p => p.FindModuleImplementing<ModuleSAS>() != null)
                && x.parts.SelectMany(p => p.FindModulesImplementing<ModuleEngines>()).Any(e => !e.propellants.Any(pr => pr.name == "SolidFuel")));
            Check(crane != null, "the middle stage, with a probe core and a liquid engine, is falling on its own");
            if (crane == null) yield break;
            Note("the falling middle stage: " + crane.parts.Count + " parts: " + string.Join(", ", crane.parts.Select(p => p.partInfo.name).Distinct().ToArray()));
            double fuelBefore = Propellant(crane.BackupVessel().protoPartSnapshots);
            FlightGlobals.ForceSetActiveVessel(top);
            yield return new WaitForSeconds(3f);
            FlightGlobals.fetch.SetShipOrbit(FlightGlobals.Bodies.IndexOf(home), 0, home.Radius + 150000, 0, 0, 0, 0, Planetarium.GetUniversalTime());
            Note("moved the satellite to orbit; waiting for KSP to delete the falling stages");

            float t0 = Time.realtimeSinceStartup;
            while (StageReturns.Instance != null && (PRSScenario.Instance == null || PRSScenario.Instance.Hangar.Count <= hangarBefore) && !StageReturns.Instance.Outcomes.Any(o => o.Contains(" was lost") && o.Contains("Terrier")) && Time.realtimeSinceStartup - t0 < 90) yield return null;
            yield return new WaitForSeconds(1f);
            List<string> forecasts = StageReturns.Instance != null ? StageReturns.Instance.Forecasts.ToList() : new List<string>();
            List<string> outcomes = StageReturns.Instance != null ? StageReturns.Instance.Outcomes.ToList() : new List<string>();
            foreach (string f in forecasts) Note("forecast: " + f);
            foreach (string o in outcomes) Note("outcome: " + o);
            Check(forecasts.Any(f => f.Contains("will come home with a landing burn") && !f.StartsWith("Z-MAP Test #1")), "as it went out of range, the suite said the middle stage (a new piece, not Z-MAP Test #1) will come home with a landing burn");
            Check(outcomes.Any(o => o.Contains("came home") && o.Contains("landing burn")), "the middle stage came home with a landing burn");
            PRSScenario sc = PRSScenario.Instance;
            HangarShip craneShip = sc.Hangar.Skip(hangarBefore).FirstOrDefault();
            Check(craneShip != null, "the middle stage is in the hangar (" + craneShip?.DisplayName + ")");
            if (craneShip == null) yield break;
            Check(craneShip.Id != zmap.Id && craneShip.Flights == 1, "it came home as a ship of its own on its first flight, not as the Z-MAP Test #1 still in orbit (" + craneShip.DisplayName + ")");
            // The burn's propellant is gone: the hangar ship came back with less than it dropped with.
            double fuelAfter = craneShip.Craft.Children("PART").SelectMany(p => p.Children("RESOURCE"))
                .Where(r => r.Get("name") == "LiquidFuel" || r.Get("name") == "Oxidizer")
                .Sum(r => double.TryParse(r.Get("amount"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double a) ? a : 0);
            double leftShare = craneShip.Fuel;
            Note("middle stage propellant: " + fuelBefore.ToString("0.0") + " when it dropped; hangar record fuel share " + leftShare.ToString("0.00") + ", craft amount " + fuelAfter.ToString("0.0"));
            Check(leftShare < 0.999, "the landing burn used propellant (the hangar ship came back with " + Math.Round(leftShare * 100) + "% of its fuel)");
        }
    }
}
