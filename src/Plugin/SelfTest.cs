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

            List<string> serials = v.parts.Select(PRSPart.SerialOf).ToList();
            Check(serials.All(s => s != null), "every part has a serial number");
            Check(serials.Distinct().Count() == serials.Count, "serial numbers are unique");
            Check(v.parts.All(p => PRSPart.Of(p) != null && PRSPart.Of(p).flights == 1), "every part is on its first flight");
            LaunchRecord rec = sc.FindRecord(serials);
            Check(rec != null && rec.Serials.Count == serials.Count, "the launch was recorded with all " + serials.Count + " parts");
            if (rec == null) yield break;
            Check(Pieces.Validate(rec.Craft).Count == 0, "the launch record is a valid craft");

            // Break the nose cone off one radial booster, so that booster comes home missing it. A clamped ship
            // on the pad won't snap a joint physically, so run the code KSP itself runs when Unity reports a
            // broken joint (PartJoint.OnJointBreak): it reports the break, then detaches the part.
            // (The variables keep the name "fin" from the first version of this test.)
            Part fin = v.parts.FirstOrDefault(p => p.children.Count == 0 && p.parent != null && p.attachJoint != null
                                                   && p.partInfo.name.StartsWith("pointyNoseCone", StringComparison.Ordinal) && OnBooster(p));
            string finSerial = PRSPart.SerialOf(fin);
            string finParentSerial = PRSPart.SerialOf(fin != null ? fin.parent : null);
            Vessel finVessel = fin != null ? fin.vessel : null;
            if (fin != null)
            {
                MethodInfo breakJoint = typeof(PartJoint).GetMethod("OnJointBreak", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (breakJoint != null) breakJoint.Invoke(fin.attachJoint, new object[] { 120f });
                else Note("PartJoint.OnJointBreak not found");
                yield return new WaitForFixedUpdate();
                Note("broke the joint of booster nose cone " + finSerial + (fin.vessel != finVessel ? ": it came off" : ": it's still on"));
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
            List<Vessel> others = FlightGlobals.Vessels.Where(o => o != null && o != FlightGlobals.ActiveVessel && o.loaded && o.parts.Any(p => PRSPart.SerialOf(p) != null)).ToList();
            Note(others.Count + " pieces to recover");
            foreach (Vessel o in others)
            {
                string oName = o.vesselName;
                List<string> oSerials = o.parts.Select(PRSPart.SerialOf).Where(s => s != null).ToList();
                bool hasFinParent = finParentSerial != null && oSerials.Contains(finParentSerial);
                int hangarBefore = sc.Hangar.Count, binBefore = sc.Bin.Count, handledBefore = sc.RecoveriesHandled;
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

                int binned = sc.Bin.Count - binBefore;
                if (sc.Hangar.Count == hangarBefore + 1)
                {
                    HangarShip h = sc.Hangar[sc.Hangar.Count - 1];
                    Check(h.Placed.Count + binned == oSerials.Count, h.DisplayName + ": all " + oSerials.Count + " parts accounted for (hangar " + h.Placed.Count + ", bin " + binned + ")");
                    List<string> probs = Pieces.Validate(h.Craft);
                    Check(probs.Count == 0, h.DisplayName + ": hangar craft is valid " + string.Join("; ", probs.Take(3).ToArray()));
                    // The booster that lost its nose cone is missing it; every other piece left at a decoupler, so
                    // nothing else is missing (and whole pieces destroyed after recovery don't change that).
                    if (hasFinParent) Check(h.Missing.Count == 1 && h.Missing.Contains(finSerial), h.DisplayName + ": knows its nose cone broke off, and nothing else (" + h.Missing.Count + " missing)");
                    else Check(h.Missing.Count == 0, h.DisplayName + ": nothing missing (" + h.Missing.Count + ")");
                    Check(h.Fuel >= 0 && h.Fuel <= 1, h.DisplayName + ": fuel left " + h.Fuel.ToString("P0"));
                    expectedIds.Add(h.Id);
                }
                else Check(binned == oSerials.Count, oName + ": went to the parts bin (" + binned + " of " + oSerials.Count + " parts)");
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
                Check(ship.parts.Count == h.Placed.Count, h.DisplayName + ": loads in the VAB with " + h.Placed.Count + " parts (got " + ship.parts.Count + ")");
                var loaded = new HashSet<string>(ship.parts.Select(PRSPart.SerialOf).Where(s => s != null));
                Check(loaded.SetEquals(h.Placed), h.DisplayName + ": its parts keep their serial numbers in the VAB");
                Check(ship.parts.All(p => PRSPart.Of(p) == null || PRSPart.Of(p).flights == 1), h.DisplayName + ": its parts remember 1 flight");
            }
        }
    }
}
