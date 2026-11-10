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
            Check(rec.ShipId == null && rec.Slots.All(s => s < 0), "launched by the test harness, not from the hangar: no ship went up in it");
            Check(Pieces.Validate(rec.Craft).Count == 0, "the launch record is a valid craft");
            // From here on it's the launch of a hangar ship, Kerbal X #1, whose blueprint is the whole craft (as if it
            // had been built and launched from the hangar): every part is the ship's.
            Ship kx = sc.NewShip("Kerbal X");
            kx.Blueprint = Hardware.Plain(rec.Craft.Clone());
            kx.Flights = 1;
            rec.ShipId = kx.Id;
            for (int i = 0; i < rec.Slots.Count; i++) rec.Slots[i] = i;

            // Break the nose cone off one radial booster, so that booster comes home missing it. A clamped ship
            // on the pad won't snap a joint physically, so run the code KSP itself runs when Unity reports a
            // broken joint (PartJoint.OnJointBreak): it reports the break, then detaches the part.
            // (The variables keep the name "fin" from the first version of this test.)
            Part fin = v.parts.FirstOrDefault(p => p.children.Count == 0 && p.parent != null && p.attachJoint != null
                                                   && p.partInfo.name.StartsWith("pointyNoseCone", StringComparison.Ordinal) && OnBooster(p));
            uint finId = fin != null ? fin.flightID : 0;
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

            // Recover every other piece with the suite's parts on it: the boosters and the nose cone are the ship's parts,
            // but with no pod or probe core of it they're just parts (Phil, round 4: the piece carrying the ship is the ship).
            List<Vessel> others = FlightGlobals.Vessels.Where(o => o != null && o != FlightGlobals.ActiveVessel && o.loaded && o.parts.Any(p => PRSPart.Of(p) != null)).ToList();
            Note(others.Count + " pieces to recover");
            int dropParts = 0;
            foreach (Vessel o in others)
            {
                string oName = o.vesselName;
                List<uint> oParts = o.parts.Select(p => p.flightID).ToList();
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
                dropParts += oParts.Count;
                Check(sc.Hangar.Count == hangarBefore && binned == oParts.Count && (sc.LastBinReason ?? "").Contains("pods or probe cores"),
                      oName + ": no pod or probe core, so all " + oParts.Count + " parts went to the parts bin (" + binned + "; " + sc.LastBinReason + ")");
            }

            // The core, with the pod, comes home as the ship itself, missing what it dropped and broke off.
            Vessel core = FlightGlobals.ActiveVessel;
            int coreParts = core.parts.Count, binBeforeCore = sc.Bin.Sum(k => k.Count);
            GameEvents.onVesselRecovered.Fire(core.BackupVessel(), true);
            yield return null;
            HangarShip home = sc.HangarOf(kx.Id);
            Check(home != null && home.PartCount == coreParts && sc.Bin.Sum(k => k.Count) == binBeforeCore, "the core, with the pod, came home as " + kx.DisplayName + " (" + home?.PartCount + " of " + coreParts + " parts)");
            if (home == null) yield break;
            List<string> probs = Pieces.Validate(home.Craft);
            Check(probs.Count == 0, "its hangar craft is valid " + string.Join("; ", probs.Take(3).ToArray()));
            Check(home.Flights == 1 && home.Missing == kx.BlueprintParts - coreParts && home.Missing == dropParts,
                  "on its first flight, missing the " + home.Missing + " parts it dropped or broke off (" + dropParts + " came home as parts)");
            Check(home.Fuel >= 0 && home.Fuel <= 1, "fuel left " + home.Fuel.ToString("P0"));
            // Restore puts the blueprint back together, from the parts bin first: the boosters' own parts are there.
            Node restored = sc.RestoreCraft(home, out int newParts);
            Quote rq = restored != null ? sc.QuoteFor(restored, kx.Name, null) : null;
            Check(restored != null && newParts == home.Missing && rq != null && rq.BinParts == home.Missing && rq.NewParts == 0,
                  "Restore puts back all " + home.Missing + " from the parts bin, nothing new (" + rq?.BinParts + " from the bin, " + rq?.NewParts + " new)");
            expectedIds.Add(home.Id);

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
