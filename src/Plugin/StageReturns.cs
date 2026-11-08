using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PhilsReusabilitySuite.Core;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// Stage recovery: when KSP deletes a stage falling through Kerbin's air out of range, the suite decides
    /// whether it lands safely (parachutes, or a powered landing) and brings it home to the hangar, with outer
    /// parts broken off by a hard landing. Drag is measured from the stage's own parts while it is still close
    /// by, which keeps resized (TweakScale) parts right. Steps aside while StageRecovery is installed.
    /// </summary>
    // Every scene where time passes: KSP deletes a falling ship in the Space Center and the Tracking Station too.
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class StageReturns : MonoBehaviour
    {
        public static StageReturns Instance { get; private set; }

        /// <summary>Drag area with chutes open, measured while the stage was loaded, by vessel id.</summary>
        static readonly Dictionary<uint, Profile> measured = new Dictionary<uint, Profile>();
        readonly HashSet<uint> handled = new HashSet<uint>();
        static readonly System.Random rng = new System.Random();

        public sealed class Profile
        {
            public double CdA;
            public int Chutes;
            public bool FromLiveParts;
        }

        /// <summary>What happened to the last stages handled (the self-test reads it).</summary>
        public readonly List<string> Outcomes = new List<string>();

        public static bool StageRecoveryInstalled => AssemblyLoader.loadedAssemblies.Any(a => a.name == "StageRecovery");

        void Start()
        {
            Instance = this;
            if (StageRecoveryInstalled) { Log.Warn("StageRecovery is installed, so the suite leaves stage recovery to it. Remove StageRecovery to have stages come home to the hangar."); return; }
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onVesselGoOnRails.Add(OnGoOnRails);
            GameEvents.onPartDeCoupleNewVesselComplete.Add(OnNewVessel);
            GameEvents.onGameSceneLoadRequested.Add(OnLeavingScene);
        }

        void OnDestroy()
        {
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onVesselGoOnRails.Remove(OnGoOnRails);
            GameEvents.onPartDeCoupleNewVesselComplete.Remove(OnNewVessel);
            GameEvents.onGameSceneLoadRequested.Remove(OnLeavingScene);
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Leaving flight: every ship still in the air is measured, including the one being flown, since it may
        /// come down while you're at the Space Center or the Tracking Station.
        /// </summary>
        void OnLeavingScene(GameScenes next)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            foreach (Vessel v in FlightGlobals.Vessels.Where(x => x != null && x.loaded && !x.LandedOrSplashed).ToList()) Remember(v);
        }

        /// <summary>
        /// The ship's speed over the ground, worked out from its orbit (it works on rails, in any scene): its
        /// velocity relative to the planet, less the ground's own turning. Not Orbit.GetVel(), which away from
        /// flight also carries the planet's own speed around the Sun.
        /// </summary>
        public static double SurfaceSpeed(Vessel v)
        {
            try { return (v.orbit.GetRelativeVel() - v.mainBody.getRFrmVelOrbit(v.orbit)).magnitude; }
            catch (Exception) { return v.srfSpeed; }
        }

        // ---- Measuring while close by ----

        void OnNewVessel(Vessel original, Vessel created)
        {
            Remember(created);
            Forecast(created);
            if (original != FlightGlobals.ActiveVessel) { Remember(original); Forecast(original); }
        }

        void OnGoOnRails(Vessel v)
        {
            if (v != FlightGlobals.ActiveVessel && !v.LandedOrSplashed) { Remember(v); Forecast(v); }
        }

        /// <summary>The measured drag of a ship, if it was measured close by (null if not).</summary>
        public static Profile MeasuredFor(uint persistentId) => measured.TryGetValue(persistentId, out Profile p) ? p : null;

        // ---- Telling the player at once ----

        /// <summary>What the suite expected of each piece as it dropped away (the self-tests read it).</summary>
        public readonly List<string> Forecasts = new List<string>();
        readonly Dictionary<uint, string> pendingForecasts = new Dictionary<uint, string>();   // one per ship: a piece that splits again replaces its own
        readonly HashSet<string> forecast = new HashSet<string>();   // ship id and part count: a piece that splits again is forecast again
        bool forecastQueued;

        /// <summary>
        /// When a piece drops away (or, if it was the ship being flown at the time, when it goes out of range),
        /// say at once whether it will come home: on its chutes, with a landing burn, hard, or not at all.
        /// The landing itself is still decided when KSP deletes the falling piece.
        /// </summary>
        void Forecast(Vessel v)
        {
            try
            {
                if (v == null || !v.loaded || v.isEVA || v.LandedOrSplashed || v == FlightGlobals.ActiveVessel || !forecast.Add(v.persistentId + ":" + v.parts.Count)) return;
                if (PRSScenario.Instance == null || Krash.SimActive || StageRecoveryInstalled) return;
                if (v.mainBody != FlightGlobals.GetHomeBody() || !v.mainBody.atmosphere) return;
                Profile prof = MeasuredFor(v.persistentId);
                if (prof == null) return;
                // A single loose part with no engine, probe core or crew (a spent decoupler, a nose cone) isn't news.
                if (v.parts.Count < 2 && !RecoveryValue.CanFly(v.parts.Select(p => p.partInfo.name)) && v.GetCrewCount() == 0) return;
                double mass = v.parts.Sum(p => p.mass + p.GetResourceMass());
                double vt = Landing.TerminalSpeed(mass, prof.CdA, v.mainBody.atmDensityASL);
                string chutes = prof.Chutes + (prof.Chutes == 1 ? " chute" : " chutes");
                string what;
                if (vt <= Landing.SafeSpeed) what = "will come home: about " + vt.ToString("0.0") + " m/s on " + chutes;
                else if (PoweredLanding(v.BackupVessel(), mass, vt, false)) what = "will come home with a landing burn";
                else if (vt <= Landing.LostSpeed) what = "will come down hard (" + vt.ToString("0.0") + " m/s): it may lose outer parts";
                else what = "won't survive the landing: " + Math.Round(vt) + " m/s" + (prof.Chutes == 0 ? " with no chutes" : " on " + chutes);
                pendingForecasts[v.persistentId] = PieceName(v) + " " + what + ".";
                if (!forecastQueued) { forecastQueued = true; StartCoroutine(PostForecasts()); }
            }
            catch (Exception ex) { Log.Warn("Couldn't forecast " + v?.vesselName + "'s landing: " + ex.Message); }
        }

        /// <summary>Pieces dropping together (six boosters) make one message, not six.</summary>
        IEnumerator PostForecasts()
        {
            yield return new WaitForSeconds(0.5f);
            var lines = pendingForecasts.Values.GroupBy(x => x).Select(g => (g.Count() > 1 ? g.Count() + " × " : "") + g.Key).ToList();
            pendingForecasts.Clear();
            forecastQueued = false;
            foreach (string l in lines) { Forecasts.Add(l); Log.Info("Forecast: " + l); }
            if (lines.Count > 0) Suite.Message(string.Join("\n", lines.ToArray()), 6f);
        }

        /// <summary>A piece by the hangar ship its parts belong to ("Kopje 005 #1"), else KSP's vessel name.</summary>
        static string PieceName(Vessel v)
        {
            string hull = v.parts.Select(p => PRSPart.Of(p)?.hull).Where(h => !string.IsNullOrEmpty(h))
                .GroupBy(h => h).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            return HangarShip.ParseHull(hull, out string name, out int number) ? name + " #" + number : Name(v);
        }

        static void Remember(Vessel v)
        {
            if (v == null || !v.loaded || v.isEVA || !v.parts.Any(p => PRSPart.SerialOf(p) != null)) return;
            try { measured[v.persistentId] = Measure(v.parts); }
            catch (Exception ex) { Log.Warn("Couldn't measure " + v.vesselName + "'s drag: " + ex.Message); }
        }

        /// <summary>Drag area (m², KSP's drag units) with every parachute open, from live parts.</summary>
        public static Profile Measure(List<Part> parts)
        {
            var prof = new Profile { FromLiveParts = true };
            double realChute = 0, stock = 0;
            RefreshDrag(parts);
            foreach (Part p in parts)
            {
                PartModule rc = p.Modules.Contains("RealChuteModule") ? p.Modules["RealChuteModule"] : null;
                if (rc != null)
                {
                    var n = new ConfigNode();
                    rc.Save(n);
                    foreach (ConfigNode chute in n.GetNodes("PARACHUTE")) { realChute += RealChuteArea(chute); prof.Chutes++; }
                    continue;
                }
                stock += StockDrag(p, ref prof.Chutes);
            }
            // As StageRecovery does: with RealChute on board, its canopies decide the landing.
            prof.CdA = realChute > 0 ? realChute : stock;
            return prof;
        }

        /// <summary>
        /// Brings these parts' drag up to date: every part's weights first, then how much each hides of its
        /// neighbours (which reads the neighbours' weights). In the editor KSP doesn't keep drag current, so
        /// without this the first check of a newly loaded ship read stale drag and gave different speeds from
        /// every later check.
        /// </summary>
        static void RefreshDrag(IEnumerable<Part> parts)
        {
            var live = parts.Where(p => p.DragCubes != null && !p.DragCubes.None && p.DragCubes.Cubes.Count > 0).ToList();
            foreach (Part p in live) { p.DragCubes.ForceUpdate(true, true); p.DragCubes.SetDragWeights(); }
            foreach (Part p in live) p.DragCubes.SetPartOcclusion();
        }

        /// <summary>A part's drag at a gentle fall (Mach 0.03), with a stock parachute counted as open. The part's
        /// drag state is put back afterwards, so nothing about the real part changes.</summary>
        static double StockDrag(Part p, ref int chutes)
        {
            DragCubeList dc = p.DragCubes;
            if (dc == null || dc.None || dc.Cubes.Count == 0) return 0;
            bool chute = p.FindModuleImplementing<ModuleParachute>() != null && dc.Cubes.Any(c => c.Name == "DEPLOYED");
            var saved = dc.Cubes.Select(c => c.Weight).ToList();
            float savedOcclusion = dc.OcclusionMultiplier;
            Quaternion savedRotation = dc.DragVectorRotation;
            try
            {
                if (chute)
                {
                    chutes++;
                    foreach (DragCube c in dc.Cubes) dc.SetCubeWeight(c.Name, c.Name == "DEPLOYED" ? 1 : 0);
                    // An open canopy isn't hidden behind the part it sits on, and faces the airflow.
                    dc.SetOcclusionMultiplier(0);
                    Quaternion rot = Quaternion.LookRotation(Vector3d.up);
                    try { if (p.partTransform != null) rot = Quaternion.LookRotation(p.partTransform.InverseTransformDirection(Vector3d.up)); } catch { }
                    dc.SetDragVectorRotation(rot);
                }
                dc.ForceUpdate(false, true);
                dc.SetDragWeights();
                dc.SetPartOcclusion();
                dc.SetDrag(Vector3.up, 0.03f);
                return dc.AreaDrag * PhysicsGlobals.DragCubeMultiplier * PhysicsGlobals.DragMultiplier;
            }
            finally
            {
                for (int i = 0; i < dc.Cubes.Count && i < saved.Count; i++) dc.SetCubeWeight(dc.Cubes[i].Name, saved[i]);
                dc.SetOcclusionMultiplier(savedOcclusion);
                dc.SetDragVectorRotation(savedRotation);
                dc.ForceUpdate(true, true);
                dc.SetDragWeights();
                dc.SetPartOcclusion();
            }
        }

        static Dictionary<string, double> materials;

        static double RealChuteArea(ConfigNode chute)
        {
            if (!double.TryParse(chute.GetValue("deployedDiameter"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)) return 0;
            if (materials == null)
            {
                materials = new Dictionary<string, double>();
                foreach (ConfigNode m in GameDatabase.Instance.GetConfigNodes("MATERIAL"))
                    if (m.GetValue("name") != null && double.TryParse(m.GetValue("dragCoefficient"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double cd))
                        materials[m.GetValue("name")] = cd;
            }
            double cdM = materials.TryGetValue(chute.GetValue("material") ?? "", out double v) ? v : 1;
            return cdM * Math.PI * d * d / 4;
        }

        /// <summary>The fallback when a stage was never measured close by: its parts' default (unscaled) drag.</summary>
        static Profile FromPrefabs(ProtoVessel pv)
        {
            var parts = pv.protoPartSnapshots.Select(ps => ps.partInfo?.partPrefab).Where(p => p != null).ToList();
            Profile prof = Measure(parts);
            prof.FromLiveParts = false;
            return prof;
        }

        // ---- Deciding the landing ----

        void OnVesselWillDestroy(Vessel v)
        {
            try
            {
                if (!ShouldHandle(v)) return;
                handled.Add(v.persistentId);
                Process(v);
            }
            catch (Exception ex) { Log.Error("Stage recovery of " + (v != null ? v.vesselName : "a stage") + " failed: " + ex); }
        }

        bool ShouldHandle(Vessel v)
        {
            GameScenes scene = HighLogic.LoadedScene;
            if (scene != GameScenes.FLIGHT && scene != GameScenes.SPACECENTER && scene != GameScenes.TRACKSTATION) return false;
            if (v == null || v.isEVA || handled.Contains(v.persistentId)) return false;
            if (scene == GameScenes.FLIGHT && v == FlightGlobals.ActiveVessel) return false;
            if (PRSScenario.Instance == null || Krash.SimActive) return false;
            if (v.loaded && !v.packed) return false;                 // destroyed in physics range: a real crash
            if (v.LandedOrSplashed) return false;                    // landed ships are recovered with the Recover button
            double altitude = !v.loaded && v.orbit != null ? v.orbit.altitude : v.altitude;   // on rails, from its orbit
            if (v.mainBody != FlightGlobals.GetHomeBody() || v.mainBody.GetPressure(altitude) <= 0) return false;
            if (v.situation != Vessel.Situations.FLYING && v.situation != Vessel.Situations.SUB_ORBITAL && v.situation != Vessel.Situations.ORBITING) return false;
            ProtoVessel pv = v.protoVessel;
            return pv != null && pv.protoPartSnapshots.Any(ps => !string.IsNullOrEmpty(SerialOf(ps)));
        }

        static string SerialOf(ProtoPartSnapshot ps) => ps.modules.FirstOrDefault(m => m.moduleName == Core.Craft.ModuleName)?.moduleValues?.GetValue("serial");

        public void Process(Vessel v)
        {
            ProtoVessel pv = v.loaded ? v.BackupVessel() : v.protoVessel;
            CelestialBody home = v.mainBody;
            double mass = 0;
            foreach (ProtoPartSnapshot ps in pv.protoPartSnapshots)
            {
                mass += ps.mass;
                foreach (ProtoPartResourceSnapshot r in ps.resources) mass += r.amount * (r.definition != null ? r.definition.density : 0);
            }
            Profile prof = measured.TryGetValue(v.persistentId, out Profile m) ? m : FromPrefabs(pv);
            double vt = Landing.TerminalSpeed(mass, prof.CdA, home.atmDensityASL);
            double landing = vt;
            string how = prof.Chutes > 0 ? "on " + prof.Chutes + (prof.Chutes == 1 ? " chute" : " chutes") : "";

            if (vt > Landing.SafeSpeed && TryPoweredLanding(pv, mass, vt))
            {
                landing = Landing.PoweredTouchdown;
                how = "with a landing burn";
            }

            double ablatorLeft = Fraction(pv, "Ablator"), speed = SurfaceSpeed(v);
            if (rng.NextDouble() < Landing.BurnUpChance(speed, ablatorLeft))
            {
                Lost(v, "burned up coming in at " + Math.Round(speed) + " m/s");
                return;
            }
            if (landing > Landing.LostSpeed)
            {
                Lost(v, "hit the ground at " + Math.Round(landing) + " m/s. Nothing survives above " + Landing.LostSpeed + " m/s");
                return;
            }

            // A hard landing: outer parts with nothing attached to them can break off.
            var broke = new List<string>();
            double chance = Landing.BreakChance(landing);
            if (chance > 0)
            {
                var parents = new HashSet<int>(pv.protoPartSnapshots.Select(ps => ps.parentIdx));
                for (int i = 0; i < pv.protoPartSnapshots.Count; i++)
                {
                    ProtoPartSnapshot ps = pv.protoPartSnapshots[i];
                    if (i == pv.rootIndex || parents.Contains(i) || ps.protoModuleCrew.Count > 0) continue;
                    string s = SerialOf(ps);
                    if (s != null && rng.NextDouble() < chance) broke.Add(s);
                }
            }

            double factor = RecoveryValue.Factor(pv), value = RecoveryValue.Value(pv), km = RecoveryValue.DistanceKm(pv);
            PRSScenario sc = PRSScenario.Instance;
            sc.LeftBehindOnNextRecovery.Clear();
            sc.LeftBehindOnNextRecovery.UnionWith(broke);
            sc.NextLandingNote = (km < 2 ? "landed at KSC" : "landed " + Math.Round(km).ToString("N0") + " km from KSC") + ", " + landing.ToString("0.0") + " m/s " + how;
            RecoverGate.Choices[pv.persistentId] = RecoverChoice.Hangar;
            var serials = pv.protoPartSnapshots.Select(SerialOf).Where(s => s != null).ToList();
            // KSP is part-way through deleting this vessel, so recover it without deleting anything ourselves:
            // announce the recovery (stock pays its refund, which the suite settles, returns the crew and credits
            // their science; PIP counts it), then take the crew off so the deletion doesn't kill them.
            GameEvents.onVesselRecovered.Fire(pv, true);
            foreach (ProtoPartSnapshot ps in pv.protoPartSnapshots)
            {
                foreach (ProtoCrewMember c in ps.protoModuleCrew.ToList()) ps.RemoveCrew(c);
            }
            sc.LeftBehindOnNextRecovery.Clear();

            HangarShip h = sc.Hangar.LastOrDefault(x => x.Placed.Any(serials.Contains));
            string where = km < 2 ? "at KSC" : Math.Round(km).ToString("N0") + " km from KSC";
            string money = Suite.Career ? " Transport " + Pricing.Funds(value * (1 - factor)) + "." : "";
            string text;
            if (h != null)
                text = h.DisplayName + (landing > Landing.SafeSpeed ? " came home hard" : " came home") + ": " + landing.ToString("0.0") + " m/s " + how + ", " + where + "."
                       + (broke.Count > 0 ? " Lost " + broke.Count + (broke.Count == 1 ? " outer part." : " outer parts.") : "")
                       + money + " In the hangar, now " + h.Flights + (h.Flights == 1 ? " flight." : " flights.");
            else
                text = Name(v) + " came down at " + landing.ToString("0.0") + " m/s " + how + ", " + where + ": " + (sc.LastBinReason ?? "it has no engine or probe core") + ", so its parts went to the parts bin."
                       + (broke.Count > 0 ? " Lost " + broke.Count + (broke.Count == 1 ? " outer part." : " outer parts.") : "") + money;
            Outcomes.Add(text);
            Log.Info("Stage recovery: " + text + Parts(pv) + (prof.FromLiveParts ? "" : " (drag from default part sizes: it was never measured close by)"));
            Suite.Message(text, 8f);
        }

        void Lost(Vessel v, string why)
        {
            string text = Name(v) + " was lost: it " + why + ".";
            Outcomes.Add(text);
            Log.Info("Stage recovery: " + text + Parts(v.loaded ? v.BackupVessel() : v.protoVessel));
            Suite.Message(text, 6f);
        }

        /// <summary>" [3 parts: HeatShield2, ...]" for the log, so a piece can be told apart from its neighbours.</summary>
        static string Parts(ProtoVessel pv)
        {
            if (pv == null) return "";
            var names = pv.protoPartSnapshots.Select(ps => ps.partName).GroupBy(n => n).Select(g => (g.Count() > 1 ? g.Count() + "×" : "") + g.Key);
            return " [" + pv.protoPartSnapshots.Count + " parts: " + string.Join(", ", names.ToArray()) + "]";
        }

        /// <summary>One piece that drops off a ship in the editor, and how it would land (empty of fuel).</summary>
        public sealed class EditorPiece
        {
            public string Label;
            public int Count = 1;
            public int Parts;
            public double Speed;
            public int Chutes;
            public bool CanBurn;
            /// <summary>Propellant (tonnes) it must keep for a landing burn, when it could make one.</summary>
            public double BurnPropellant;
            /// <summary>It could steer, but its engines can't hold it up even when empty.</summary>
            public bool TooWeakToBurn;
            /// <summary>The part names in the piece (for checking).</summary>
            public List<string> Names = new List<string>();
        }

        /// <summary>
        /// How much propellant a piece must still carry for a landing burn: the burn's own propellant makes the
        /// piece heavier and faster, so it's worked out a few times over. Zero if its liquid engines can't lift it.
        /// </summary>
        static double PropellantToKeep(List<Part> piece, double emptyMass, double cdA, double rho, out bool tooWeak)
        {
            double thrust = 0, flowIsp = 0;
            foreach (ModuleEngines e in piece.SelectMany(p => p.FindModulesImplementing<ModuleEngines>()))
            {
                if (e.propellants.Any(pr => pr.name == "SolidFuel")) continue;
                double isp = e.atmosphereCurve.Evaluate(1);
                if (isp <= 0) continue;
                thrust += e.maxThrust;
                flowIsp += e.maxThrust / isp;
            }
            tooWeak = thrust <= 0 || thrust < emptyMass * 9.81;
            if (tooWeak) return 0;
            double avgIsp = thrust / flowIsp, keep = 0;
            for (int i = 0; i < 4; i++)
            {
                double m = emptyMass + keep;
                keep = Landing.PropellantNeeded(m, Landing.TerminalSpeed(m, cdA, rho), avgIsp);
            }
            tooWeak = thrust < (emptyMass + keep) * 9.81;
            return tooWeak ? 0 : keep;
        }

        /// <summary>
        /// The VAB's landing check: the ship split at its decouplers, and for each piece that drops away, its
        /// landing speed with every parachute open (counted empty of fuel, as a spent stage).
        /// </summary>
        public static List<EditorPiece> CheckEditorShip(ShipConstruct ship)
        {
            var pieces = new List<EditorPiece>();
            if (ship == null || ship.parts.Count == 0) return pieces;
            CelestialBody home = FlightGlobals.GetHomeBody();
            double rho = home != null ? home.atmDensityASL : 1.225;
            var byKey = new Dictionary<string, EditorPiece>();
            RefreshDrag(ship.parts);   // the whole ship, since a piece's drag depends on the parts it touches
            foreach (Part d in ship.parts.Where(p => p.FindModuleImplementing<ModuleDecouplerBase>() != null && p.parent != null))
            {
                var piece = new List<Part>();
                Collect(d, piece);
                if (piece.Count == 0) continue;
                Profile prof = MeasureAlone(piece);
                // Empty of fuel, as a spent stage, but a heat shield keeps its ablator (it isn't burnt as fuel).
                double mass = piece.Sum(p => p.mass + p.Resources.Cast<PartResource>().Where(r => r.resourceName == "Ablator").Sum(r => r.amount * r.info.density));
                double vt = Landing.TerminalSpeed(mass, prof.CdA, rho);
                Part biggest = piece.OrderByDescending(p => p.mass).First();
                bool canBurn = piece.Any(p => p.FindModuleImplementing<ModuleSAS>() != null)
                               && piece.SelectMany(p => p.FindModulesImplementing<ModuleEngines>()).Any(e => !e.propellants.Any(pr => pr.name == "SolidFuel"));
                string key = string.Join(",", piece.Select(p => p.partInfo.name).OrderBy(n => n).ToArray()) + "|" + Math.Round(vt, 1);
                if (byKey.TryGetValue(key, out EditorPiece same)) { same.Count++; continue; }
                var e2 = new EditorPiece { Label = "Piece with " + biggest.partInfo.title, Parts = piece.Count, Speed = vt, Chutes = prof.Chutes, CanBurn = canBurn, Names = piece.Select(p => p.partInfo.name).ToList() };
                if (canBurn && vt > Landing.SafeSpeed)
                {
                    e2.BurnPropellant = PropellantToKeep(piece, mass, prof.CdA, rho, out bool weak);
                    e2.TooWeakToBurn = weak;
                }
                byKey[key] = e2;
                pieces.Add(e2);
            }
            return pieces;
        }

        /// <summary>
        /// A piece measured as it will fall: on its own. KSP hides a part's face where it's stacked against
        /// another part, so a piece measured while still in the ship counted the faces it shares with the parts
        /// above and below as hidden, and read far too fast (Kerbal X's heat shield: 619 m/s in the VAB, 62 m/s
        /// in flight). Its stack joints to the rest of the ship are unhooked for the measurement only, within
        /// this one call, and hooked straight back.
        /// </summary>
        public static Profile MeasureAlone(List<Part> piece)
        {
            var inPiece = new HashSet<Part>(piece);
            var cut = new List<KeyValuePair<AttachNode, Part>>();
            foreach (Part p in piece)
                foreach (AttachNode n in p.attachNodes)
                    if (n.attachedPart != null && !inPiece.Contains(n.attachedPart)) cut.Add(new KeyValuePair<AttachNode, Part>(n, n.attachedPart));
            try
            {
                foreach (var c in cut) c.Key.attachedPart = null;
                return Measure(piece);
            }
            finally
            {
                foreach (var c in cut) c.Key.attachedPart = c.Value;
                RefreshDrag(piece);
            }
        }

        /// <summary>
        /// A decoupler and everything under it, stopping at the next decoupler down. Launch clamps are left out:
        /// they stay on the pad, so they never fall with a stage.
        /// </summary>
        static void Collect(Part p, List<Part> into)
        {
            into.Add(p);
            foreach (Part c in p.children)
                if (c.FindModuleImplementing<ModuleDecouplerBase>() == null && c.FindModuleImplementing<LaunchClamp>() == null) Collect(c, into);
        }

        static string Name(Vessel v) => KSP.Localization.Localizer.Format(v.vesselName);

        static double Fraction(ProtoVessel pv, string resource)
        {
            double amount = 0, max = 0;
            foreach (ProtoPartSnapshot ps in pv.protoPartSnapshots)
                foreach (ProtoPartResourceSnapshot r in ps.resources)
                    if (r.resourceName == resource) { amount += r.amount; max += r.maxAmount; }
            return max > 0 ? amount / max : 0;
        }

        /// <summary>
        /// A powered landing, as StageRecovery allows it: the stage must be controlled (a pilot, or a part with
        /// SAS), have liquid engines with thrust-to-weight of at least 1, and carry the propellant to slow from
        /// its falling speed with a 1.5× margin. The propellant is used up.
        /// </summary>
        static bool TryPoweredLanding(ProtoVessel pv, double mass, double speed) => PoweredLanding(pv, mass, speed, true);

        /// <summary>Whether a powered landing would work; with burn, the propellant it takes is used up.</summary>
        static bool PoweredLanding(ProtoVessel pv, double mass, double speed, bool burn)
        {
            bool controlled = pv.protoPartSnapshots.Any(ps =>
                ps.protoModuleCrew.Any(c => c.experienceTrait != null && c.experienceTrait.TypeName == "Pilot")
                || (ps.partInfo?.partPrefab != null && ps.partInfo.partPrefab.FindModuleImplementing<ModuleSAS>() != null));
            if (!controlled) return false;
            double thrust = 0, flowIsp = 0;
            var propellants = new HashSet<string>();
            foreach (ProtoPartSnapshot ps in pv.protoPartSnapshots)
            {
                Part prefab = ps.partInfo?.partPrefab;
                if (prefab == null) continue;
                foreach (ModuleEngines e in prefab.FindModulesImplementing<ModuleEngines>())
                {
                    if (e.propellants.Any(pr => pr.name == "SolidFuel")) continue;
                    double isp = e.atmosphereCurve.Evaluate(1);
                    if (isp <= 0) continue;
                    thrust += e.maxThrust;
                    flowIsp += e.maxThrust / isp;
                    foreach (Propellant pr in e.propellants) if (!pr.ignoreForIsp) propellants.Add(pr.name);
                }
            }
            if (thrust <= 0 || thrust < mass * 9.81) return false;
            double avgIsp = thrust / flowIsp;
            double need = Landing.PropellantNeeded(mass, speed, avgIsp);
            var tanks = pv.protoPartSnapshots.SelectMany(ps => ps.resources).Where(r => propellants.Contains(r.resourceName) && r.definition != null && r.definition.density > 0).ToList();
            double have = tanks.Sum(r => r.amount * r.definition.density);
            if (have < need || have <= 0) return false;
            if (!burn) return true;
            double use = need / have;
            foreach (ProtoPartResourceSnapshot r in tanks) r.amount -= r.amount * use;
            return true;
        }
    }
}
