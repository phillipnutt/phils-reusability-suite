using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// The in-game check the identity rebuild stands on (round 4): KSP's own launch id and part id stay with every
    /// part through separating, docking, undocking, saving and loading. Started by the test harness on a launched
    /// Kerbal X through the hidden PRSSelfTestIds event. Docking is KSP's own coupling step (what a docking port
    /// calls, Part.Couple) and undocking its own Part.Undock, run on a dropped booster.
    /// </summary>
    static class SelfTestIds
    {
        static bool running;

        public static void Begin()
        {
            if (running) return;
            running = true;
            var go = new GameObject("PRSSelfTestIds");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<SelfTestIdsRunner>();
        }
    }

    class SelfTestIdsRunner : MonoBehaviour
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
            yield return new WaitForSeconds(1f);
            IEnumerator body = Body();
            while (true)
            {
                object cur;
                try { if (!body.MoveNext()) break; cur = body.Current; }
                catch (Exception ex) { Check(false, "the id check threw " + ex); break; }
                yield return cur;
            }
            Note("done: " + pass + " passed, " + fail + " failed");
            Debug.Log("[PRS-TEST-DONE]");
            Destroy(gameObject);
        }

        /// <summary>Every part of every ship in flight: part id to launch id.</summary>
        static Dictionary<uint, uint> AllIds()
        {
            var d = new Dictionary<uint, uint>();
            foreach (Vessel v in FlightGlobals.Vessels.Where(x => x != null))
            {
                if (v.loaded) foreach (Part p in v.parts) d[p.flightID] = p.launchID;
                else foreach (ProtoPartSnapshot ps in v.protoVessel.protoPartSnapshots) d[ps.flightID] = ps.launchID;
            }
            return d;
        }

        bool Same(Dictionary<uint, uint> before, string when)
        {
            Dictionary<uint, uint> now = AllIds();
            int kept = before.Count(kv => now.TryGetValue(kv.Key, out uint l) && l == kv.Value);
            Check(kept == before.Count, when + ": all " + before.Count + " parts keep their part id and launch id (" + kept + " do)");
            return kept == before.Count;
        }

        IEnumerator Body()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            Note("craft " + v.vesselName + ", " + v.parts.Count + " parts");
            uint launch = v.parts[0].launchID;
            Check(launch != 0 && v.parts.All(p => p.launchID == launch), "every part of the launch has one launch id (" + launch + ")");
            Check(v.parts.All(p => p.flightID != 0) && v.parts.Select(p => p.flightID).Distinct().Count() == v.parts.Count, "every part has its own part id");
            Dictionary<uint, uint> ids = AllIds();
            Check(ids.Count >= v.parts.Count, "the ids are read back from the ship (" + ids.Count + ")");

            // Each launched part is found in KSP's launched-ship config by the craft's own id for it ("name_123"), which is
            // how the launch record knows which part is which (the ship's part list can be reordered after launch).
            ConfigNode cfg = ShipConstruction.ShipConfig;
            var refs = new HashSet<string>((cfg != null ? cfg.GetNodes("PART") : new ConfigNode[0]).Select(n => n.GetValue("part")));
            var mine = v.parts.Select(p => p.partInfo.name + "_" + p.craftID).ToList();
            Check(mine.All(refs.Contains) && mine.Distinct().Count() == mine.Count, "every launched part is in KSP's launched-ship config by its craft id (" + mine.Count(refs.Contains) + " of " + mine.Count + ")");
            PRSScenario sc = PRSScenario.Instance;
            LaunchRecord rec = sc?.RecordOf(launch, v.parts[0].flightID);
            Check(rec != null && v.parts.All(p => rec.IndexOf(p.flightID) >= 0), "the suite's launch record knows every part by KSP's part id");

            // Separating: a radial booster drops off and becomes a ship of its own.
            ModuleAnchoredDecoupler dec = v.FindPartModulesImplementing<ModuleAnchoredDecoupler>().FirstOrDefault(d => !d.isDecoupled);
            if (dec == null) { Check(false, "found a radial decoupler"); yield break; }
            Part decPart = dec.part;
            Part anchor = decPart.parent;
            dec.Decouple();
            yield return new WaitForSeconds(1f);
            Vessel booster = decPart.vessel;
            Check(booster != v && booster != null, "the booster came off as a ship of its own (" + (booster != null ? booster.parts.Count : 0) + " parts)");
            Same(ids, "after separating");

            // Docking: KSP's own coupling step, as a docking port runs it.
            if (booster == null || anchor == null) yield break;
            int coreBefore = v.parts.Count;
            decPart.Couple(anchor);
            yield return new WaitForSeconds(1f);
            Check(decPart.vessel == anchor.vessel && anchor.vessel.parts.Count > coreBefore, "the booster docked back on (" + anchor.vessel.parts.Count + " parts together)");
            Same(ids, "after docking");

            // Undocking: KSP's own step, which makes it a ship again.
            var info = new DockedVesselInfo { name = "Booster", vesselType = VesselType.Debris, rootPartUId = decPart.flightID };
            decPart.Undock(info);
            yield return new WaitForSeconds(1f);
            Check(decPart.vessel != anchor.vessel, "the booster undocked as a ship of its own");
            Same(ids, "after undocking");

            // Saving and loading: the save keeps them.
            uint activeId = FlightGlobals.ActiveVessel.persistentId;
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            yield return null;
            Game game = GamePersistence.LoadGame("persistent", HighLogic.SaveFolder, true, false);
            Check(game != null, "the save loads back");
            if (game == null) yield break;
            var saved = new Dictionary<uint, uint>();
            foreach (ProtoVessel pv in game.flightState.protoVessels)
                foreach (ProtoPartSnapshot ps in pv.protoPartSnapshots) saved[ps.flightID] = ps.launchID;
            int inSave = ids.Count(kv => saved.TryGetValue(kv.Key, out uint l) && l == kv.Value);
            Check(inSave == ids.Count, "the saved game has every part with its part id and launch id (" + inSave + " of " + ids.Count + ")");
            Check(game.launchID > launch, "the save keeps KSP's launch counter past this launch (" + game.launchID + ")");
            int index = game.flightState.protoVessels.FindIndex(pv => pv.persistentId == activeId);
            FlightDriver.StartAndFocusVessel(game, Math.Max(0, index));
            float t0 = Time.realtimeSinceStartup;
            yield return new WaitForSeconds(2f);
            while (!(HighLogic.LoadedSceneIsFlight && FlightGlobals.ready && FlightGlobals.ActiveVessel != null))
            {
                if (Time.realtimeSinceStartup - t0 > 90f) { Check(false, "the flight loaded again"); yield break; }
                yield return null;
            }
            yield return new WaitForSeconds(3f);
            // KSP itself may recover a piece sitting on the pad as the flight loads (the undocked booster did): those
            // parts have left. Every part still flying keeps its ids and still finds its launch record.
            Dictionary<uint, uint> after = AllIds();
            // (This launch's parts only: the test save can hold parts of another ship.)
            var still = ids.Where(kv => kv.Value == launch && after.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            Note((ids.Count - still.Count) + " parts are no longer in flight after loading (recovered as the flight loaded)");
            Same(still, "after saving and loading the flight");
            sc = PRSScenario.Instance;
            int found = sc == null ? 0 : still.Count(kv => sc.RecordOf(kv.Value, kv.Key) != null);
            Note("after loading: " + (sc != null ? sc.Launches.Count : 0) + " launch records: " + (sc != null ? string.Join(", ", sc.Launches.Select(r => "launch " + r.LaunchId + " (" + r.Parts.Count + " parts)").ToArray()) : ""));
            Check(found == still.Count, "after loading, every part still in flight finds its launch record (" + found + " of " + still.Count + ")");
        }
    }
}
