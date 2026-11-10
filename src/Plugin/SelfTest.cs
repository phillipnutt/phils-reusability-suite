using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PhilsReusabilitySuite.Core;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// An unattended in-game check of build step 1, started by the test harness through the hidden
    /// PRSSelfTest event on a launched craft (the stock Kerbal X). It breaks a booster's nose cone off, drops the radial
    /// boosters, recovers every piece, saves, then loads each hangar ship in the VAB and checks it.
    /// Results go to the log as "[PRS-TEST]" lines; failures are logged as errors so the harness lists them.
    /// </summary>
    static class SelfTest
    {
        static bool running;

        public static void Begin()
        {
            if (running) return;
            running = true;
            var go = new GameObject("PRSSelfTest");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<SelfTestRunner>();
        }
    }

    class SelfTestRunner : MonoBehaviour
    {
        int pass, fail;

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
            yield return new WaitForSeconds(1f);
            IEnumerator flight = Flight();
            while (true)
            {
                object cur;
                try { if (!flight.MoveNext()) break; cur = flight.Current; }
                catch (Exception ex) { Check(false, "flight part threw " + ex); break; }
                yield return cur;
            }
            Note("done: " + pass + " passed, " + fail + " failed");
            Suite.Settings.RequireDesign = designRule;
            Debug.Log("[PRS-TEST-DONE]");
            Destroy(gameObject);
        }

        readonly List<string> expectedIds = new List<string>();

        /// <summary>Whether a part hangs below a radial decoupler (is on a booster that will be dropped).</summary>
        static bool OnBooster(Part p)
        {
            for (Part a = p.parent; a != null; a = a.parent)
                if (a.FindModuleImplementing<ModuleAnchoredDecoupler>() != null) return true;
            return false;
        }

        IEnumerator Flight()
        {
            PRSScenario sc = PRSScenario.Instance;
            Check(sc != null, "the suite's save data is running in flight");
            if (sc == null) yield break;
            Vessel v = FlightGlobals.ActiveVessel;
            Note("craft " + v.vesselName + ", " + v.parts.Count + " parts");

            uint launch = v.parts[0].launchID;
            Check(launch != 0 && v.parts.All(p => p.launchID == launch && p.flightID != 0), "every part has KSP's launch id and its own part id");
            Check(v.parts.All(p => PRSPart.Of(p) != null && string.IsNullOrEmpty(PRSPart.Of(p).hw)), "no part carries a VAB note into flight");
            LaunchRecord rec = sc.RecordOf(launch, v.parts[0].flightID);
            Check(rec != null && rec.Parts.Count == v.parts.Count && v.parts.All(p => rec.IndexOf(p.flightID) >= 0), "the launch was recorded with all " + v.parts.Count + " parts");
            if (rec == null) yield break;
            Check(rec.Ships.Count == 0, "launched by the test harness, not from the hangar: no hangar ship went up in it");
            Check(Pieces.Validate(rec.Craft).Count == 0, "the launch record is a valid craft");

            // Break the nose cone off one radial booster, so that booster comes home missing it. A clamped ship
            // on the pad won't snap a joint physically, so run the code KSP itself runs when Unity reports a
            // broken joint (PartJoint.OnJointBreak): it reports the break, then detaches the part.
            // (The variables keep the name "fin" from the first version of this test.)
            Part fin = v.parts.FirstOrDefault(p => p.children.Count == 0 && p.parent != null && p.attachJoint != null
                                                   && p.partInfo.name.StartsWith("pointyNoseCone", StringComparison.Ordinal) && OnBooster(p));
            uint finId = fin != null ? fin.flightID : 0;
            uint finParentId = fin != null && fin.parent != null ? fin.parent.flightID : 0;
            Vessel finVessel = fin != null ? fin.vessel : null;
            if (fin != null)
            {
                MethodInfo breakJoint = typeof(PartJoint).GetMethod("OnJointBreak", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (breakJoint != null) breakJoint.Invoke(fin.attachJoint, new object[] { 120f });
                else Note("PartJoint.OnJointBreak not found");
                yield return new WaitForFixedUpdate();
                Note("broke the joint of booster nose cone " + finId + (fin.vessel != finVessel ? ": it came off" : ": it's still on"));
            }
            else Note("no booster nose cone found to break off");
            yield return new WaitForSeconds(2f);
            if (fin != null) Check(fin.vessel != finVessel, "the nose cone really came off");

            // Drop the radial boosters.
            int dropped = 0;
            foreach (ModuleAnchoredDecoupler d in v.FindPartModulesImplementing<ModuleAnchoredDecoupler>().ToList())
                if (!d.isDecoupled) { d.Decouple(); dropped++; }
            Note("fired " + dropped + " radial decouplers");
            yield return new WaitForSeconds(4f);

            // Recover every other piece with the suite's parts on it.
            List<Vessel> others = FlightGlobals.Vessels.Where(o => o != null && o != FlightGlobals.ActiveVessel && o.loaded && o.parts.Any(p => PRSPart.Of(p) != null)).ToList();
            Note(others.Count + " pieces to recover");
            foreach (Vessel o in others)
            {
                string oName = o.vesselName;
                List<uint> oParts = o.parts.Select(p => p.flightID).ToList();
                bool hasFinParent = finParentId != 0 && oParts.Contains(finParentId);
                int hangarBefore = sc.Hangar.Count, binBefore = sc.Bin.Sum(k => k.Count), handledBefore = sc.RecoveriesHandled;
                ProtoVessel pv = o.BackupVessel();
                try { ShipConstruction.RecoverVesselFromFlight(pv, HighLogic.CurrentGame.flightState, true); }
                catch (Exception ex) { Note("stock RecoverVesselFromFlight threw: " + ex.Message); }
                if (sc.RecoveriesHandled == handledBefore)
                {
                    Note(oName + ": stock recovery didn't report it, so the test reports it");
                    GameEvents.onVesselRecovered.Fire(pv, true);
                }
                else Note(oName + ": stock recovery reported it");
                if (o != null && o.state != Vessel.State.DEAD) o.Die();
                yield return null;

                int binned = sc.Bin.Sum(k => k.Count) - binBefore;
                if (sc.Hangar.Count == hangarBefore + 1)
                {
                    HangarShip h = sc.Hangar[sc.Hangar.Count - 1];
                    Check(h.PartCount + binned == oParts.Count, h.DisplayName + ": all " + oParts.Count + " parts accounted for (hangar " + h.PartCount + ", bin " + binned + ")");
                    Check(h.Flights == 1 && h.Pieces.Count == 0, h.DisplayName + ": a new piece of this launch, on its first flight (" + h.Flights + ")");
                    List<string> probs = Pieces.Validate(h.Craft);
                    Check(probs.Count == 0, h.DisplayName + ": hangar craft is valid " + string.Join("; ", probs.Take(3).ToArray()));
                    // The booster that lost its nose cone is missing it; every other piece left at a decoupler, so
                    // nothing else is missing (and whole pieces destroyed after recovery don't change that).
                    if (hasFinParent) Check(h.Missing == 1, h.DisplayName + ": knows its nose cone broke off, and nothing else (" + h.Missing + " missing)");
                    else Check(h.Missing == 0, h.DisplayName + ": nothing missing (" + h.Missing + ")");
                    Check(h.Fuel >= 0 && h.Fuel <= 1, h.DisplayName + ": fuel left " + h.Fuel.ToString("P0"));
                    expectedIds.Add(h.Id);
                }
                else Check(binned == oParts.Count, oName + ": went to the parts bin (" + binned + " of " + oParts.Count + " parts)");
            }

            // Save, go to the VAB, and load each hangar ship.
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            EditorDriver.StartupBehaviour = EditorDriver.StartupBehaviours.START_CLEAN;
            EditorDriver.StartEditor(EditorFacility.VAB);
            float t0 = Time.realtimeSinceStartup;
            while (!(HighLogic.LoadedScene == GameScenes.EDITOR && EditorLogic.fetch != null && EditorLogic.fetch.ship != null))
            {
                if (Time.realtimeSinceStartup - t0 > 90f) { Check(false, "the VAB opened"); yield break; }
                yield return null;
            }
            yield return new WaitForSeconds(3f);
            sc = PRSScenario.Instance;
            Check(sc != null && expectedIds.All(id => sc.Hangar.Any(h => h.Id == id)), "hangar ships survived saving and loading (" + expectedIds.Count + ")");
            if (sc == null) yield break;
            foreach (string id in expectedIds)
            {
                HangarShip h = sc.Hangar.FirstOrDefault(x => x.Id == id);
                if (h == null) continue;
                EditorAddon.LoadHangarShip(h);
                yield return new WaitForSeconds(2.5f);
                ShipConstruct ship = EditorLogic.fetch.ship;
                Check(ship.parts.Count == h.PartCount, h.DisplayName + ": loads in the VAB with " + h.PartCount + " parts (got " + ship.parts.Count + ")");
                Check(sc.ExactShipOf(EditorAddon.EditorCraft()) == h, h.DisplayName + ": the VAB knows every part is that ship's hardware");
                Check(ship.parts.All(EditorAddon.IsHardware), h.DisplayName + ": every part counts as real hardware, not a new part");
            }
        }
    }
}
