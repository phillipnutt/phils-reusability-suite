using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PhilsReusabilitySuite.Core;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// The suite's save data and its flight hooks: records each launch as it leaves the pad, notices parts
    /// breaking off, and turns recovered ships into hangar ships by crossing off what didn't come home.
    /// Everything lives in the save, so KRASH's revert undoes a simulation's effects.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER, GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public partial class PRSScenario : ScenarioModule
    {
        public static PRSScenario Instance { get; private set; }

        public readonly List<LaunchRecord> Launches = new List<LaunchRecord>();
        public readonly List<HangarShip> Hangar = new List<HangarShip>();
        public readonly List<BinPart> Bin = new List<BinPart>();
        /// <summary>Serials of parts that broke off (not decoupled) since their last launch.</summary>
        public readonly HashSet<string> BrokeOff = new HashSet<string>();
        /// <summary>Last hangar number given out per ship name, so numbers are never reused.</summary>
        readonly Dictionary<string, int> lastNumber = new Dictionary<string, int>();

        readonly Dictionary<Part, int> separatedFrame = new Dictionary<Part, int>();
        readonly HashSet<uint> recoveredThisScene = new HashSet<uint>();
        /// <summary>How many recoveries this scene handled (the self-test reads it).</summary>
        public int RecoveriesHandled;

        public override void OnAwake()
        {
            base.OnAwake();
            Instance = this;
            GameEvents.OnVesselRollout.Add(OnRollout);
            GameEvents.onFlightReady.Add(OnFlightReady);
            GameEvents.onVesselRecovered.Add(OnRecovered);
            GameEvents.onPartJointBreak.Add(OnPartJointBroke);
            GameEvents.onPartDeCouple.Add(OnSeparated);
            GameEvents.onPartUndock.Add(OnSeparated);
            GameEvents.onPartDie.Add(OnPartDie);
        }

        void OnDestroy()
        {
            GameEvents.OnVesselRollout.Remove(OnRollout);
            GameEvents.onFlightReady.Remove(OnFlightReady);
            GameEvents.onVesselRecovered.Remove(OnRecovered);
            GameEvents.onPartJointBreak.Remove(OnPartJointBroke);
            GameEvents.onPartDeCouple.Remove(OnSeparated);
            GameEvents.onPartUndock.Remove(OnSeparated);
            GameEvents.onPartDie.Remove(OnPartDie);
            if (Instance == this) Instance = null;
        }

        // ---- Launches ----

        void OnRollout(ShipConstruct ship)
        {
            if (ship == null) return;
            if (!ReadyShipRollout(ship) && !Krash.SimActive) RenewTakenSerials(ship.parts);
            RecordLaunch(ship.parts, KSP.Localization.Localizer.Format(ship.shipName), "rollout");
        }

        /// <summary>
        /// Backstop in case the rollout event came before this module woke up. KSP fires this on every flight
        /// scene load, and a ship on the pad keeps a mission time of 0, so a launch is told apart from going back
        /// to a ship still on the pad (or quickloading it) by the vessel each record belongs to.
        /// </summary>
        void OnFlightReady()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.situation != Vessel.Situations.PRELAUNCH) return;
            LaunchRecord recent = RecentRecordOf(v.parts);
            if (recent != null) { Claim(recent, v); return; }
            // KSP reuses a vessel id when the same craft launches again, so a record only counts as this ship's if
            // it also shares its hardware.
            var serials = new HashSet<string>(v.parts.Select(PRSPart.SerialOf).Where(s => s != null));
            if (Launches.Any(r => r.VesselId == v.persistentId && r.Serials.Overlaps(serials))) return;
            LaunchRecord rec = RecordLaunch(v.parts, v.vesselName, "flight ready");
            if (rec != null) Claim(rec, v);
        }

        /// <summary>This record is the vessel's launch; an older record with the same (reused) vessel id isn't.</summary>
        void Claim(LaunchRecord rec, Vessel v)
        {
            foreach (LaunchRecord r in Launches) if (r != rec && r.VesselId == v.persistentId) r.VesselId = 0;
            rec.VesselId = v.persistentId;
        }

        /// <summary>The record made for these parts in the last few seconds (by the rollout), if any.</summary>
        LaunchRecord RecentRecordOf(List<Part> parts)
        {
            double now = Planetarium.GetUniversalTime();
            foreach (Part p in parts)
            {
                string s = PRSPart.SerialOf(p);
                LaunchRecord r = s != null ? Launches.FirstOrDefault(x => Math.Abs(x.UT - now) < 10 && x.Serials.Contains(s)) : null;
                if (r != null) return r;
            }
            return null;
        }

        LaunchRecord RecordLaunch(List<Part> parts, string name, string how)
        {
            if (Krash.SimActive) { Log.Info("KRASH simulation: launch of " + name + " not recorded"); return null; }
            if (parts == null || parts.Count == 0) return null;
            if (RecentRecordOf(parts) != null) return null;
            var mods = parts.Select(PRSPart.Of).ToList();
            foreach (PRSPart m in mods)
                if (m != null && string.IsNullOrEmpty(m.serial)) m.serial = PRSPart.NewSerial();

            ConfigNode cfg = ShipConstruction.ShipConfig;
            if (cfg == null) { Log.Warn("No launched-ship config for " + name + "; launch not recorded"); return null; }
            Node craft = KspNodes.FromKsp(cfg);
            List<Node> partNodes = craft.Children("PART").ToList();
            if (partNodes.Count != parts.Count) { Log.Warn("Launched-ship config of " + name + " has " + partNodes.Count + " parts, the ship " + parts.Count + "; launch not recorded"); return null; }
            for (int i = 0; i < parts.Count; i++)
            {
                string want = parts[i].partInfo != null ? parts[i].partInfo.name : null;
                string have = partNodes[i].Get("part") ?? "";
                if (want != null && !have.StartsWith(want + "_", StringComparison.Ordinal))
                {
                    Log.Warn("Launched-ship config of " + name + " doesn't match its parts (" + have + " vs " + want + "); launch not recorded");
                    return null;
                }
            }

            // It's a launch: only now does every part count a flight.
            foreach (PRSPart m in mods)
            {
                if (m == null) continue;
                m.flights++;
                BrokeOff.Remove(m.serial);   // a fresh flight
            }
            for (int i = 0; i < parts.Count; i++) if (mods[i] != null) Stamp(partNodes[i], mods[i]);

            var rec = new LaunchRecord { Id = Guid.NewGuid().ToString("N"), UT = Planetarium.GetUniversalTime(), Ship = name, Craft = craft };
            rec.IndexSerials();
            Launches.Add(rec);
            Log.Info("Launch recorded (" + how + "): " + name + ", " + rec.Serials.Count + " parts");
            return rec;
        }

        static void Stamp(Node partNode, PRSPart m)
        {
            Node mod = partNode.Children("MODULE").FirstOrDefault(x => x.Get("name") == Core.Craft.ModuleName);
            if (mod == null)
            {
                mod = partNode.AddNode("MODULE");
                mod.Add("name", Core.Craft.ModuleName);
                mod.Add("isEnabled", "True");
            }
            mod.Set("serial", m.serial);
            mod.Set("flights", m.flights.ToString());
        }

        /// <summary>The launch these serials flew on: most serials in common, then the latest.</summary>
        public LaunchRecord FindRecord(ICollection<string> serials)
        {
            LaunchRecord best = null;
            int bestCount = 0;
            foreach (LaunchRecord r in Launches)
            {
                int n = serials.Count(r.Serials.Contains);
                if (n > bestCount || (n == bestCount && n > 0 && best != null && r.UT > best.UT)) { best = r; bestCount = n; }
            }
            return best;
        }

        // ---- Parts breaking off ----

        // How KSP 1.12 reports separations (read from its code):
        //  - a decoupler or undocking announces itself first (onPartDeCouple / onPartUndock), then removes the
        //    part's joint, which KSP also reports as a joint break;
        //  - a joint that physics snaps is reported as a joint break first, and KSP decouples the part after.
        // So a joint break counts only if nothing announced a separation of that part in the same frame.
        // (Not named OnJointBreak: Unity would take that for its own one-argument message.)
        void OnPartJointBroke(PartJoint joint, float force)
        {
            Part child = joint != null ? joint.Child : null;
            string s = PRSPart.SerialOf(child);
            if (s == null || BrokeOff.Contains(s)) return;
            if (separatedFrame.TryGetValue(child, out int frame) && frame == Time.frameCount) return;
            // KSP also reports joint breaks that aren't, for example landing legs when a ship unpacks on the
            // pad. So only count it if the part really came away.
            StartCoroutine(ConfirmBreak(child, s, child.vessel));
        }

        void OnSeparated(Part p)
        {
            if (p == null) return;
            if (separatedFrame.Count > 500)
            {
                foreach (Part old in separatedFrame.Keys.Where(k => k == null || separatedFrame[k] != Time.frameCount).ToList())
                    separatedFrame.Remove(old);
            }
            separatedFrame[p] = Time.frameCount;
        }

        IEnumerator ConfirmBreak(Part part, string serial, Vessel was)
        {
            float until = Time.realtimeSinceStartup + 0.5f;
            while (Time.realtimeSinceStartup < until) yield return null;
            // Broken off means it came away on its own: it is now the root of a ship of its own, or gone. (A part
            // that only moved to a new ship along with its neighbours, when a decoupler fired, didn't break.)
            bool gone = part == null || part.vessel == null || (part.vessel != was && part.parent == null && part.vessel.rootPart == part);
            if (!gone) { Log.Info("Ignored a joint break on " + part.partInfo.title + ": it's still attached"); yield break; }
            if (BrokeOff.Add(serial)) Log.Info((part != null ? part.partInfo.title : "A part") + " broke off " + (was != null ? was.vesselName : "a ship"));
        }

        void OnPartDie(Part p)
        {
            if (!HighLogic.LoadedSceneIsFlight || p == null) return;
            string s = PRSPart.SerialOf(p);
            // Only a part destroyed while still attached to others counts as lost from its ship.
            if (s != null && (p.parent != null || p.children.Count > 0)) BrokeOff.Add(s);
        }

        // ---- Recovery ----

        void OnRecovered(ProtoVessel pv, bool quick)
        {
            if (pv == null) return;
            if (Krash.SimActive) { Log.Info("KRASH simulation: recovery of " + pv.vesselName + " ignored"); return; }
            if (!recoveredThisScene.Add(pv.persistentId)) return;
            try { Recover(pv); }
            catch (Exception ex) { Log.Error("Recovering " + pv.vesselName + " failed: " + ex); }
        }

        /// <summary>Why the last recovered piece went to the parts bin, or null if it went to the hangar (for messages).</summary>
        public string LastBinReason;

        void Recover(ProtoVessel pv)
        {
            RecoveriesHandled++;
            LastBinReason = null;
            var serials = new List<string>();
            var info = new Dictionary<string, ProtoPartSnapshot>();
            foreach (ProtoPartSnapshot ps in pv.protoPartSnapshots)
            {
                ProtoPartModuleSnapshot m = ps.modules.FirstOrDefault(x => x.moduleName == Core.Craft.ModuleName);
                string s = m?.moduleValues?.GetValue("serial");
                if (string.IsNullOrEmpty(s) || info.ContainsKey(s)) continue;
                serials.Add(s);
                info[s] = ps;
            }
            if (serials.Count == 0) return;
            // Parts lost in a hard landing didn't come home: they count as broken off.
            foreach (string s in serials.Where(LeftBehindOnNextRecovery.Contains).ToList()) { serials.Remove(s); BrokeOff.Add(s); }

            RecoverChoice choice = RecoverGate.Choices.TryGetValue(pv.persistentId, out RecoverChoice ch) ? ch : RecoverChoice.Hangar;
            if (choice == RecoverChoice.Sell)
            {
                Log.Info("Recovered " + pv.vesselName + ": sold for scrap (" + serials.Count + " parts)");
                return;
            }

            LaunchRecord rec = FindRecord(serials);
            if (rec == null)
            {
                LastBinReason = "it has no launch record";
                foreach (string s in serials) ToBin(s, info);
                Log.Info("Recovered " + pv.vesselName + ": no launch record, " + serials.Count + " parts to the parts bin");
                return;
            }

            PieceResult r = Pieces.Build(rec.Craft, serials, BrokeOff);
            List<string> problems = r.Craft != null ? Pieces.Validate(r.Craft) : new List<string> { "nothing placed" };
            if (r.Craft == null || r.Placed.Count == 0 || problems.Count > 0)
            {
                LastBinReason = "it couldn't be rebuilt as a ship";
                if (problems.Count > 0) Log.Error("Rebuilt " + pv.vesselName + " isn't valid (" + string.Join("; ", problems.Take(3).ToArray()) + "); its parts go to the parts bin");
                foreach (string s in serials) ToBin(s, info, Core.Craft.Read(rec.Craft));
                return;
            }

            // Only a piece that can fly again is kept whole; anything else is just parts.
            Craft recCraft = Core.Craft.Read(rec.Craft);
            if (!RecoveryValue.CanFly(r.Placed.Select(s => info[s].partName)))
            {
                LastBinReason = "it has no engine or probe core";
                foreach (string s in serials) ToBin(s, info, recCraft);
                Log.Info("Recovered " + pv.vesselName + ": no engine or probe core, so its " + serials.Count + " parts went to the parts bin");
                Changed();
                return;
            }
            // Phil's rule: a piece of a ship that was never filed as a design in Phil's Builds is just parts.
            if (S.RequireDesign && !r.Placed.Any(s => !string.IsNullOrEmpty(info[s].modules.FirstOrDefault(x => x.moduleName == Core.Craft.ModuleName)?.moduleValues?.GetValue("design"))))
            {
                LastBinReason = "it wasn't built from a design filed in Phil's Builds";
                foreach (string s in serials) ToBin(s, info, recCraft);
                Log.Info("Recovered " + pv.vesselName + ": not built from a design filed in Phil's Builds, so its " + serials.Count + " parts went to the parts bin");
                Changed();
                return;
            }

            // A piece comes home as the hangar ship most of its parts belong to ("Oak 005 #4" again); a piece
            // flying for the first time gets the next number under its vessel name.
            string hull = r.Placed.Select(s => info[s].modules.FirstOrDefault(x => x.moduleName == Core.Craft.ModuleName)?.moduleValues?.GetValue("hull"))
                .Where(x => !string.IsNullOrEmpty(x)).GroupBy(x => x).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            string name;
            int number;
            if (!HangarShip.ParseHull(hull, out name, out number))
            {
                name = KSP.Localization.Localizer.Format(pv.vesselName);
                lastNumber.TryGetValue(name, out int last);
                number = last + 1;
            }
            // Which piece is the ship it launched as? The one holding the launched ship's root part keeps its name,
            // number and flight log. A piece that came off it (side boosters on chutes, the launcher under a
            // payload) is new hardware coming home on its own: it gets an identity of its own, named after its
            // design if it's a copy of one ("Kopje 005 #2"). Reused hardware carrying an identity of its own (a
            // flown "Kopje 005 #1" under a new payload) keeps that, wherever it was in the stack.
            CraftPart launchRoot = recCraft.Parts.FirstOrDefault(p => p.Parent == null);
            string rootHull = launchRoot?.Node.Children("MODULE").FirstOrDefault(m => m.Get("name") == Core.Craft.ModuleName)?.Get("hull");
            bool holdsRoot = launchRoot?.Serial != null && r.Placed.Contains(launchRoot.Serial);
            if (!string.IsNullOrEmpty(hull) && hull == rootHull && !holdsRoot)
            {
                string designId = BuildsLink.SingleDesign(r.Placed.Select(s => info[s].modules.FirstOrDefault(x => x.moduleName == Core.Craft.ModuleName)?.moduleValues?.GetValue("design")));
                string designName = designId != null ? BuildsLink.NameOf(designId) : null;
                if (!string.IsNullOrEmpty(designName)) name = designName;
                lastNumber.TryGetValue(name, out int lastOfName);
                number = Math.Max(lastOfName, name == designName ? 0 : number) + 1;
            }
            if (!lastNumber.TryGetValue(name, out int lastGiven) || lastGiven < number) lastNumber[name] = number;
            var h = new HangarShip
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Number = number,
                UT = Planetarium.GetUniversalTime(),
                LaunchId = rec.Id,
                RootSerial = r.RootSerial,
                Craft = r.Craft,
                Fuel = FuelLeft(r.Placed, info),
            };
            h.StampHull(true);
            h.Placed.AddRange(r.Placed);
            h.Missing.AddRange(r.Missing);
            h.Flights = r.Placed.Select(s => FlightsOf(info[s])).DefaultIfEmpty(0).Max();
            // What each part still holds, so a refit only tops it up.
            foreach (string s in r.Placed)
            {
                var left = new Dictionary<string, double>();
                foreach (ProtoPartResourceSnapshot res in info[s].resources)
                    left[res.resourceName] = (left.TryGetValue(res.resourceName, out double had) ? had : 0) + res.amount;
                if (left.Count > 0) h.CameBack[s] = left;
            }
            Hangar.Add(h);
            // The ship's flight log: when, what it flew as, and how it came home.
            double km = RecoveryValue.DistanceKm(pv);
            string landed = NextLandingNote ?? (km < 2 ? "recovered at KSC" : "recovered " + Math.Round(km).ToString("N0") + " km from KSC");
            AddToLog(h.Hull, h.Flights + " · " + KSPUtil.PrintDateCompact(rec.UT, false) + " · " + rec.Ship + " · " + landed
                     + (r.Missing.Count > 0 ? ", lost " + r.Missing.Count + (r.Missing.Count == 1 ? " part" : " parts") : ""));
            NextLandingNote = null;
            // Broken-off parts stay noted even when they come home on their own: the ship they came off is
            // still missing them. The note clears when the part next launches.
            foreach (string s in r.Unplaced.Concat(r.Dropped)) ToBin(s, info, recCraft);
            Log.Info("Recovered " + name + ": " + r.Placed.Count + " parts to the hangar as " + h.DisplayName
                     + (r.Missing.Count > 0 ? ", " + r.Missing.Count + " missing" : "")
                     + (r.Unplaced.Count + r.Dropped.Count > 0 ? ", " + (r.Unplaced.Count + r.Dropped.Count) + " to the parts bin" : ""));
            Changed();
        }

        readonly HashSet<uint> moneyDone = new HashSet<uint>();

        /// <summary>The hangar, bin or queue changed: Phil's Builds' window redraws its hangar copies.</summary>
        internal void Changed() { BuildsLink.HangarChanged(); }

        /// <summary>Serials that broke off in a hard landing, set by stage recovery just before it recovers.</summary>
        public readonly HashSet<string> LeftBehindOnNextRecovery = new HashSet<string>();

        /// <summary>How the next recovered piece landed ("312 km downrange, 5.8 m/s on 3 chutes"), set by stage recovery.</summary>
        public string NextLandingNote;

        /// <summary>Each hangar ship's flight log, by identity ("Name|Number"), newest first.</summary>
        public readonly Dictionary<string, List<string>> FlightLogs = new Dictionary<string, List<string>>();

        public List<string> LogOf(HangarShip h) => FlightLogs.TryGetValue(h.Hull, out List<string> l) ? l : new List<string>();

        void AddToLog(string hull, string entry)
        {
            if (!FlightLogs.TryGetValue(hull, out List<string> l)) FlightLogs[hull] = l = new List<string>();
            l.Insert(0, entry);
            if (l.Count > 30) l.RemoveRange(30, l.Count - 30);
        }

        /// <summary>
        /// KSP has just paid its recovery refund (<paramref name="paid"/>: the value times the distance factor).
        /// The suite keeps the hardware instead, so it takes that refund back and charges the transport home
        /// (the value times what the distance rule holds back); for a sale it leaves half the refund as scrap.
        /// </summary>
        public void SettleRecovery(ProtoVessel pv, double paid, float factor)
        {
            if (pv == null || !Suite.Career || Krash.SimActive || !moneyDone.Add(pv.persistentId)) return;
            bool ours = pv.protoPartSnapshots.Any(ps => !string.IsNullOrEmpty(ps.modules.FirstOrDefault(x => x.moduleName == Core.Craft.ModuleName)?.moduleValues?.GetValue("serial")));
            if (!ours) return;
            RecoverChoice choice = RecoverGate.Choices.TryGetValue(pv.persistentId, out RecoverChoice ch) ? ch : RecoverChoice.Hangar;
            double transport = RecoveryValue.Value(pv) * (1 - factor);
            double adjust = choice == RecoverChoice.Hangar ? paid + transport : paid * (1 - S.ScrapValue);
            Suite.Charge(adjust);
            Log.Info(choice == RecoverChoice.Hangar
                ? "Recovery of " + pv.vesselName + ": kept the hardware, transport " + Pricing.Funds(transport) + " (KSP's refund of " + Pricing.Funds(paid) + " taken back, factor " + factor.ToString("0.00") + ")"
                : "Recovery of " + pv.vesselName + ": sold for scrap, " + Pricing.Funds(paid * S.ScrapValue));
        }

        void ToBin(string serial, Dictionary<string, ProtoPartSnapshot> info, Craft from = null)
        {
            if (serial == null || !info.TryGetValue(serial, out ProtoPartSnapshot ps)) return;
            // From its launch record when there is one; otherwise made from the part itself, so the bin part can
            // still be matched to a new build and sold for what it's worth.
            Node partNode = from != null && from.BySerial.TryGetValue(serial, out CraftPart cp) ? Builds.Loose(cp.Node) : PartNodeFrom(ps, serial);
            Bin.Add(new BinPart { Serial = serial, Part = ps.partName, Flights = FlightsOf(ps), PartNode = partNode });
        }

        static Node PartNodeFrom(ProtoPartSnapshot ps, string serial)
        {
            var n = new Node("PART");
            n.Add("part", ps.partName + "_" + ps.flightID.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // What variants or rescaling add to its cost and mass, as KSP saves them, so it matches and sells as itself.
            n.Add("modCost", ps.moduleCosts.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            n.Add("modMass", ps.moduleMass.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            foreach (ProtoPartResourceSnapshot r in ps.resources)
            {
                Node rn = n.AddNode("RESOURCE");
                rn.Add("name", r.resourceName);
                rn.Add("amount", r.amount.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                rn.Add("maxAmount", r.maxAmount.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            }
            Node m = n.AddNode("MODULE");
            m.Add("name", Core.Craft.ModuleName);
            m.Add("isEnabled", "True");
            m.Add("serial", serial);
            m.Add("flights", FlightsOf(ps).ToString(System.Globalization.CultureInfo.InvariantCulture));
            return n;
        }

        static int FlightsOf(ProtoPartSnapshot ps)
        {
            ProtoPartModuleSnapshot m = ps.modules.FirstOrDefault(x => x.moduleName == Core.Craft.ModuleName);
            return int.TryParse(m?.moduleValues?.GetValue("flights"), out int f) ? f : 0;
        }

        static double FuelLeft(IEnumerable<string> placed, Dictionary<string, ProtoPartSnapshot> info)
        {
            double amount = 0, max = 0;
            foreach (string s in placed)
            {
                if (!info.TryGetValue(s, out ProtoPartSnapshot ps)) continue;
                foreach (ProtoPartResourceSnapshot res in ps.resources)
                {
                    PartResourceDefinition def = PartResourceLibrary.Instance.GetDefinition(res.resourceName);
                    if (def == null || def.density <= 0) continue;
                    amount += res.amount;
                    max += res.maxAmount;
                }
            }
            return max > 0 ? amount / max : 1;
        }

        // ---- Tidying ----

        /// <summary>Drops launch records none of whose parts still exist in flight.</summary>
        void Prune()
        {
            FlightState fs = HighLogic.CurrentGame?.flightState;
            if (fs == null) return;
            var live = new HashSet<string>();
            foreach (ProtoVessel pv in fs.protoVessels)
                foreach (ProtoPartSnapshot ps in pv.protoPartSnapshots)
                {
                    ProtoPartModuleSnapshot m = ps.modules.FirstOrDefault(x => x.moduleName == Core.Craft.ModuleName);
                    string s = m?.moduleValues?.GetValue("serial");
                    if (!string.IsNullOrEmpty(s)) live.Add(s);
                }
            int before = Launches.Count;
            Launches.RemoveAll(r => !r.Serials.Any(live.Contains));
            var known = new HashSet<string>(Launches.SelectMany(r => r.Serials));
            BrokeOff.RemoveWhere(s => !known.Contains(s));
            if (Launches.Count != before) Log.Info("Dropped " + (before - Launches.Count) + " finished launch records");
        }

        // ---- Saving ----

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            foreach (LaunchRecord r in Launches) r.Save(node.AddNode("LAUNCH"));
            foreach (HangarShip h in Hangar) h.Save(node.AddNode("HANGAR_SHIP"));
            foreach (BinPart b in Bin) b.Save(node.AddNode("BIN_PART"));
            foreach (BuildJob j in Jobs) j.Save(node.AddNode("BUILD"));
            node.AddValue("lastUT", lastUT.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            foreach (KeyValuePair<string, List<string>> kv in FlightLogs)
            {
                ConfigNode fl = node.AddNode("FLIGHT_LOG");
                fl.AddValue("ship", kv.Key);
                foreach (string e in kv.Value) fl.AddValue("flight", e);
            }
            ConfigNode broke = node.AddNode("BROKE_OFF");
            foreach (string s in BrokeOff) broke.AddValue("serial", s);
            foreach (KeyValuePair<string, int> kv in lastNumber)
            {
                ConfigNode n = node.AddNode("NUMBER");
                n.AddValue("name", kv.Key);
                n.AddValue("last", kv.Value);
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            Launches.Clear(); Hangar.Clear(); Bin.Clear(); BrokeOff.Clear(); lastNumber.Clear(); Jobs.Clear(); FlightLogs.Clear();
            foreach (ConfigNode fl in node.GetNodes("FLIGHT_LOG"))
                if (fl.GetValue("ship") != null) FlightLogs[fl.GetValue("ship")] = fl.GetValues("flight").ToList();
            foreach (ConfigNode n in node.GetNodes("LAUNCH")) { LaunchRecord r = LaunchRecord.Load(n); if (r != null) Launches.Add(r); }
            foreach (ConfigNode n in node.GetNodes("HANGAR_SHIP")) { HangarShip h = HangarShip.Load(n); if (h != null) Hangar.Add(h); }
            foreach (ConfigNode n in node.GetNodes("BIN_PART")) Bin.Add(BinPart.Load(n));
            foreach (ConfigNode n in node.GetNodes("BUILD")) { BuildJob j = BuildJob.Load(n); if (j != null) Jobs.Add(j); }
            lastUT = node.HasValue("lastUT") ? LaunchRecord.Num(node.GetValue("lastUT")) : -1;
            if (lastUT <= 0) lastUT = -1;
            ConfigNode broke = node.GetNode("BROKE_OFF");
            if (broke != null) foreach (string s in broke.GetValues("serial")) BrokeOff.Add(s);
            foreach (ConfigNode n in node.GetNodes("NUMBER"))
                if (int.TryParse(n.GetValue("last"), out int last) && n.GetValue("name") != null) lastNumber[n.GetValue("name")] = last;
            if (HighLogic.LoadedScene == GameScenes.SPACECENTER) Prune();
        }
    }
}
