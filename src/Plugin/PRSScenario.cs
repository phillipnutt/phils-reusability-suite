using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PhilsReusabilitySuite.Core;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// The suite's save data and its flight hooks. Every launch is recorded as it leaves the pad, with the ships that
    /// went up in it (the suite starts every launch of a hangar ship, so it knows which). When something comes home,
    /// the suite looks up the launch it came from (KSP's own launch id is on every part) and which of that launch's
    /// ships it carries, and files it as that ship. Everything lives in the save, so KRASH's revert undoes a
    /// simulation's effects.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER, GameScenes.EDITOR, GameScenes.FLIGHT, GameScenes.TRACKSTATION)]
    public partial class PRSScenario : ScenarioModule
    {
        public static PRSScenario Instance { get; private set; }

        /// <summary>The save format. 2: ships, launches and the bin as their own records (round 4); older saves start fresh.</summary>
        public const int Format = 2;

        public readonly List<LaunchRecord> Launches = new List<LaunchRecord>();
        public readonly List<HangarShip> Hangar = new List<HangarShip>();
        public readonly List<BinKind> Bin = new List<BinKind>();
        public readonly Dictionary<string, Ship> Ships = new Dictionary<string, Ship>();
        /// <summary>Last number given out per ship name, so numbers are never reused.</summary>
        readonly Dictionary<string, int> lastNumber = new Dictionary<string, int>();

        /// <summary>The hangar ship the suite is launching right now, and when (only it can be that launch's ship).</summary>
        public string PendingLaunch;
        double pendingUT;

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
        }

        void OnDestroy()
        {
            GameEvents.OnVesselRollout.Remove(OnRollout);
            GameEvents.onFlightReady.Remove(OnFlightReady);
            GameEvents.onVesselRecovered.Remove(OnRecovered);
            if (Instance == this) Instance = null;
        }

        static double Now => Planetarium.fetch != null ? Planetarium.GetUniversalTime() : 0;

        // ---- Ships ----

        /// <summary>A new ship under this name, with the next number.</summary>
        public Ship NewShip(string name)
        {
            lastNumber.TryGetValue(name, out int last);
            int highest = Ships.Values.Where(s => s.Name == name).Select(s => s.Number).DefaultIfEmpty(0).Max();
            var ship = new Ship { Id = HangarShip.NewId(), Name = name, Number = Math.Max(last, highest) + 1 };
            lastNumber[name] = ship.Number;
            Ships[ship.Id] = ship;
            return ship;
        }

        /// <summary>The number a new ship of this name would get now, counting new ships of it already queued.</summary>
        public int NextNumber(string name, BuildJob except = null)
        {
            lastNumber.TryGetValue(name, out int last);
            int highest = Ships.Values.Where(s => s.Name == name).Select(s => s.Number).DefaultIfEmpty(0).Max();
            return Math.Max(last, highest) + 1 + Jobs.Count(j => j != except && j.ShipId == null && j.Name == name);
        }

        public HangarShip HangarOf(string shipId) => shipId == null ? null : Hangar.FirstOrDefault(h => h.Ship.Id == shipId);

        public Ship ShipOf(string shipId) => shipId != null && Ships.TryGetValue(shipId, out Ship s) ? s : null;

        public List<string> LogOf(HangarShip h) => h.Ship.Log;

        // ---- Launches ----

        /// <summary>The suite is about to launch this hangar ship (from the Space Center or the VAB).</summary>
        public void Launching(HangarShip h)
        {
            PendingLaunch = h.Id;
            pendingUT = Now;
        }

        void OnRollout(ShipConstruct ship)
        {
            if (ship == null) return;
            RecordLaunch(ship.parts, KSP.Localization.Localizer.Format(ship.shipName), "rollout", ship);
        }

        /// <summary>
        /// Backstop in case the rollout event came before this module woke up. KSP fires this on every flight scene
        /// load; a ship still on the pad (or quickloaded there) already has its record, by KSP's launch id.
        /// </summary>
        void OnFlightReady()
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null || v.situation != Vessel.Situations.PRELAUNCH) return;
            RecordLaunch(v.parts, v.vesselName, "flight ready", null);
        }

        /// <summary>The record of the launch a part came from, by KSP's launch id and its own part id.</summary>
        public LaunchRecord RecordOf(uint launchId, uint flightId) => Launches.FirstOrDefault(r => r.LaunchId == launchId && r.Parts.Contains(flightId));

        LaunchRecord RecordLaunch(List<Part> parts, string name, string how, ShipConstruct ship)
        {
            if (parts == null || parts.Count == 0) return null;
            if (RecordOf(parts[0].launchID, parts[0].flightID) != null) return null;   // back on the pad, or quickloaded
            if (Krash.SimActive) { Log.Info("KRASH simulation: launch of " + name + " not recorded"); EndNotes(parts); return null; }

            ConfigNode cfg = ShipConstruction.ShipConfig;
            if (cfg == null) { Log.Warn("No launched-ship config for " + name + "; launch not recorded"); return null; }
            Node craft = KspNodes.FromKsp(cfg);
            int[] at = PlacesIn(craft, parts);
            if (at == null) { Log.Warn("Launched-ship config of " + name + " doesn't match its " + parts.Count + " parts; launch not recorded"); return null; }

            var rec = new LaunchRecord { LaunchId = parts[0].launchID, UT = Now, Name = name, Craft = craft };
            rec.Parts.AddRange(Enumerable.Repeat(0u, parts.Count));
            for (int i = 0; i < parts.Count; i++) rec.Parts[at[i]] = parts[i].flightID;
            // Which ships went up: only a hangar ship the suite itself is launching, and only if this is all its hardware.
            HangarShip h = LaunchingShip(parts);
            if (h != null)
            {
                Craft read = Core.Craft.Read(craft);
                for (int i = 0; i < parts.Count; i++)
                    if (Hardware.Parse(PRSPart.NoteOf(parts[i]), out _, out int stored)) rec.Members[at[i]] = h.MemberOf(stored);
                // Each ship rides on its root part: this one on the craft's root, a flown stage inside it on its own highest part.
                rec.Ships.Add(new KeyValuePair<string, int>(h.Id, read.RootPart.Index));
                foreach (string piece in h.Pieces.Values.Distinct())
                {
                    int root = read.Highest(rec.Members.Where(m => m.Value == piece).Select(m => m.Key));
                    if (root >= 0) rec.Ships.Add(new KeyValuePair<string, int>(piece, root));
                }
                rec.Wear = h.Wear;
                foreach (KeyValuePair<string, int> s in rec.Ships) if (Ships.TryGetValue(s.Key, out Ship sh)) sh.Flights++;
                Hangar.Remove(h);
                Changed();
                string with = rec.Ships.Count > 1 ? " (with " + string.Join(", ", rec.Ships.Skip(1).Select(s => ShipOf(s.Key)?.DisplayName ?? "a ship").ToArray()) + ")" : "";
                if (ship != null)
                {
                    // KSP charges a craft's price at rollout; a hangar ship is already paid for.
                    float cost = ship.GetShipCosts(out float dry, out float fuel, ShipConstruction.ShipManifest);
                    if (Suite.Career) Funding.Instance.AddFunds(cost, TransactionReasons.VesselRollout);
                    Log.Info("Launched " + h.DisplayName + with + " from the hangar: already paid for, " + Pricing.Funds(cost) + " handed back");
                }
                else Log.Info("Launched " + h.DisplayName + with + " from the hangar");
            }
            else Log.Warn(name + " launched without being built: KSP charged its normal price, and it's no hangar ship");
            PendingLaunch = null;
            EndNotes(parts);
            Hardware.Plain(craft);
            Launches.Add(rec);
            Log.Info("Launch recorded (" + how + "): " + name + ", " + parts.Count + " parts, launch " + rec.LaunchId);
            return rec;
        }

        /// <summary>
        /// Each launched part's place in KSP's launched-ship config: by the craft's own id for it ("name_123"), so the
        /// order of the ship's part list doesn't matter. Null if they don't match one for one.
        /// </summary>
        static int[] PlacesIn(Node craft, List<Part> parts)
        {
            List<Node> nodes = craft.Children("PART").ToList();
            if (nodes.Count != parts.Count) return null;
            var byRef = new Dictionary<string, int>();
            for (int i = 0; i < nodes.Count; i++) byRef[nodes[i].Get("part") ?? ""] = i;
            var at = new int[parts.Count];
            var used = new HashSet<int>();
            bool byId = true;
            for (int i = 0; i < parts.Count && byId; i++)
            {
                string r = (parts[i].partInfo != null ? parts[i].partInfo.name : "") + "_" + parts[i].craftID.ToString(CultureInfo.InvariantCulture);
                byId = byRef.TryGetValue(r, out at[i]) && used.Add(at[i]);
            }
            if (byId) return at;
            // Else the order KSP built the ship in, part by part, if every part name matches.
            for (int i = 0; i < parts.Count; i++)
            {
                if (!(nodes[i].Get("part") ?? "").StartsWith((parts[i].partInfo?.name ?? "") + "_", StringComparison.Ordinal)) return null;
                at[i] = i;
            }
            return at;
        }

        /// <summary>The flight starts: every part starts a new record of its hardest moments, and the VAB's notes end.</summary>
        static void EndNotes(List<Part> parts)
        {
            foreach (PRSPart m in parts.Select(PRSPart.Of).Where(x => x != null)) { m.hw = ""; m.ClearPeaks(); }
        }

        /// <summary>The hangar ship this launch is: the one the suite is launching, if every part is exactly its hardware.</summary>
        HangarShip LaunchingShip(List<Part> parts)
        {
            if (PendingLaunch == null || Math.Abs(Now - pendingUT) > 600) return null;
            HangarShip h = HangarOf(PendingLaunch);
            if (h == null || h.ReservedBy != null || parts.Count != h.PartCount) return null;
            List<Node> stored = h.Craft.Children("PART").ToList();
            var seen = new HashSet<int>();
            foreach (Part p in parts)
                if (!Hardware.Parse(PRSPart.NoteOf(p), out string craftId, out int at) || craftId != h.CraftId || at >= stored.Count || !seen.Add(at)
                    || Hardware.PartName(stored[at]) != p.partInfo?.name) return null;
            return h;
        }

        /// <summary>
        /// Which of a launch's ships a piece is: the one whose root part it carries (the highest in the launch's tree,
        /// if it carries several; the others ride inside it as themselves). Null: it carries none, so it's a new piece.
        /// The one rule, for the hangar and for the flight forecasts.
        /// </summary>
        public static string ShipIn(LaunchRecord rec, ICollection<int> parts, Craft launched, out List<string> riding)
        {
            riding = new List<string>();
            if (rec == null) return null;
            var carried = rec.Ships.Where(s => parts.Contains(s.Value)).ToList();
            if (carried.Count == 0) return null;
            int top = launched.Highest(carried.Select(s => s.Value));
            string main = carried.First(s => s.Value == top).Key;
            riding.AddRange(carried.Where(s => s.Key != main).Select(s => s.Key).Distinct());
            return main;
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
        /// <summary>The hangar ship the last recovery made, if any (for messages).</summary>
        public HangarShip LastHangarShip;
        /// <summary>What the last recovery paid to refurbish parts into the parts bin.</summary>
        public double LastBinPrice;

        void Recover(ProtoVessel pv)
        {
            RecoveriesHandled++;
            LastBinReason = null;
            LastHangarShip = null;
            LastBinPrice = 0;
            List<ProtoPartSnapshot> parts = pv.protoPartSnapshots.Where(PRSPart.Has).ToList();
            if (parts.Count == 0) return;
            RecoverChoice choice = RecoverGate.Choices.TryGetValue(pv.persistentId, out RecoverChoice ch) ? ch : RecoverChoice.Hangar;
            if (choice == RecoverChoice.Sell)
            {
                Log.Info("Recovered " + pv.vesselName + ": sold for scrap (" + parts.Count + " parts)");
                return;
            }
            // Two ships docked together come home as what each launch made of them.
            foreach (IGrouping<uint, ProtoPartSnapshot> launch in parts.GroupBy(ps => ps.launchID)) RecoverLaunch(pv, launch.ToList());
            if (LastBinPrice > 0) Suite.Charge(LastBinPrice);
            Changed();
        }

        void RecoverLaunch(ProtoVessel pv, List<ProtoPartSnapshot> parts)
        {
            string vesselName = KSP.Localization.Localizer.Format(pv.vesselName);
            LaunchRecord rec = parts.Select(ps => RecordOf(ps.launchID, ps.flightID)).FirstOrDefault(r => r != null);
            if (rec == null)
            {
                LastBinReason = "it has no launch record";
                double w = Core.Wear.After(0, FlightWear(parts));
                foreach (ProtoPartSnapshot ps in parts) ToBin(PartNodeFrom(ps), w);
                Log.Info("Recovered " + vesselName + ": no launch record, " + parts.Count + " parts to the parts bin");
                return;
            }
            // Each part by its place in the launch; parts added in flight aren't in it (parts bin). Parts a hard landing
            // broke off didn't come home.
            var at = new Dictionary<int, ProtoPartSnapshot>();
            var strangers = new List<ProtoPartSnapshot>();
            var broke = new List<int>();
            foreach (ProtoPartSnapshot ps in parts)
            {
                int i = rec.IndexOf(ps.flightID);
                if (i < 0 || at.ContainsKey(i)) { strangers.Add(ps); continue; }
                if (LeftBehindOnNextRecovery.Contains(ps.flightID)) broke.Add(i);
                else at[i] = ps;
            }
            List<Node> launchParts = rec.Craft.Children("PART").ToList();
            // Its wear: the craft's wear when it launched, plus what this flight's hardest moments added.
            double wear = Core.Wear.After(rec.Wear, FlightWear(at.Values));
            PieceResult r = Pieces.Build(rec.Craft, at.Keys.ToList(), broke);
            List<string> problems = r.Craft != null ? Pieces.Validate(r.Craft) : new List<string> { "nothing placed" };
            string binReason = null;
            if (r.Craft == null || r.Placed.Count == 0 || problems.Count > 0)
            {
                binReason = "it couldn't be rebuilt as a ship";
                if (problems.Count > 0) Log.Error("Rebuilt " + vesselName + " isn't valid (" + string.Join("; ", problems.Take(3).ToArray()) + "); its parts go to the parts bin");
            }
            // Only a piece that can fly again is kept whole; anything else is just parts.
            else if (!RecoveryValue.CanFly(r.Placed.Select(i => at[i].partName))) binReason = "it has no engine or probe core";
            // Phil's rule: a piece of a ship that was never filed as a design in Phil's Builds is just parts.
            else if (S.RequireDesign && !r.Placed.Any(i => !string.IsNullOrEmpty(PRSPart.ValueIn(at[i], "design")))) binReason = "it wasn't built from a design filed in Phil's Builds";
            if (binReason != null)
            {
                LastBinReason = binReason;
                foreach (int i in at.Keys) ToBin(launchParts[i], wear);
                foreach (ProtoPartSnapshot ps in strangers) ToBin(PartNodeFrom(ps), wear);
                Log.Info("Recovered " + vesselName + ": " + binReason + ", so its " + (at.Count + strangers.Count) + " parts went to the parts bin");
                return;
            }

            // Who it is: the launch's ship whose root part it carries; otherwise a new piece, named after its design.
            Craft launched = Core.Craft.Read(rec.Craft);
            string shipId = ShipIn(rec, r.Placed, launched, out List<string> riding);
            Ship ship = ShipOf(shipId);
            bool isNew = ship == null;
            if (isNew)
            {
                // It belongs to its design for good (Phil's Builds counts it there), named after it.
                string designId = BuildsLink.DesignIdFor(r.Placed.Select(i => Hardware.Module(launchParts[i])?.Get("design")), Hardware.Module(launchParts[r.Root])?.Get("design"));
                ship = NewShip((designId != null ? BuildsLink.NameOf(designId) : null) ?? vesselName);
                ship.DesignId = designId;
                ship.Flights = 1;   // its first flight was this launch
            }
            // Its blueprint: the ship as it launched, less what it dropped on purpose, so Restore puts back what it lost.
            PieceResult whole = Pieces.Build(rec.Craft, r.Placed.Concat(r.Missing).ToList());
            var h = new HangarShip { Ship = ship, UT = Now, Craft = Hardware.Plain(r.Craft), Wear = wear, Missing = r.Missing.Count, Blueprint = Hardware.Plain(whole.Craft) };
            for (int k = 0; k < r.Placed.Count; k++)
            {
                int i = r.Placed[k];
                h.Slots.Add(whole.Placed.IndexOf(i));
                // Its inventory as it is now (parts used up or taken out in flight are gone), not as it launched.
                CarryInventory(at[i], h.Craft.Children("PART").ElementAt(k));
                if (rec.Members.TryGetValue(i, out string member) && member != ship.Id && riding.Contains(member)) h.Pieces[k] = member;
                // What each part still holds, so refuelling only tops it up.
                var left = new Dictionary<string, double>();
                foreach (ProtoPartResourceSnapshot res in at[i].resources)
                    left[res.resourceName] = (left.TryGetValue(res.resourceName, out double had) ? had : 0) + res.amount;
                if (left.Count > 0) h.CameBack[k] = left;
            }
            h.UpdateFuel();
            Hangar.Add(h);
            LastHangarShip = h;
            // The flight log: when, what it flew as, and how it came home. Ships riding inside it log it too.
            double km = RecoveryValue.DistanceKm(pv);
            string landed = NextLandingNote ?? (km < 2 ? "recovered at KSC" : "recovered " + Math.Round(km).ToString("N0") + " km from KSC");
            string tail = " · " + KSPUtil.PrintDateCompact(rec.UT, false) + " · " + rec.Name + " · " + landed
                          + (r.Missing.Count > 0 ? ", lost " + Suite.Parts(r.Missing.Count) : "") + " · wear " + Core.Wear.Percent(h.Wear);
            ship.AddLog(ship.Flights + tail);
            foreach (string id in riding) { Ship rs = ShipOf(id); if (rs != null) rs.AddLog(rs.Flights + tail + " · inside " + ship.DisplayName); }
            NextLandingNote = null;
            // Parts that came home but can't be placed, and struts whose other end is gone, go to the parts bin.
            foreach (int i in r.Unplaced.Concat(r.Dropped)) ToBin(launchParts[i], wear);
            foreach (ProtoPartSnapshot ps in strangers) ToBin(PartNodeFrom(ps), wear);
            int binned = r.Unplaced.Count + r.Dropped.Count + strangers.Count;
            Log.Info("Recovered " + vesselName + ": " + r.Placed.Count + " parts to the hangar as " + h.DisplayName + (isNew ? " (new)" : "")
                     + (riding.Count > 0 ? ", with " + string.Join(", ", riding.Select(id => ShipOf(id)?.DisplayName ?? "?").ToArray()) + " inside" : "")
                     + (r.Missing.Count > 0 ? ", " + r.Missing.Count + " missing" : "") + ", wear " + Core.Wear.Percent(h.Wear)
                     + (binned > 0 ? ", " + binned + " to the parts bin" : ""));
        }

        /// <summary>A part's inventory module (KSP's cargo) as the part came home, in place of the one it launched with.</summary>
        static void CarryInventory(ProtoPartSnapshot ps, Node part)
        {
            List<ProtoPartModuleSnapshot> now = ps.modules.Where(m => m.moduleName == "ModuleInventoryPart" && m.moduleValues != null).ToList();
            int k = 0;
            for (int j = 0; j < part.Nodes.Count && k < now.Count; j++)
            {
                if (part.Nodes[j].Name != "MODULE" || part.Nodes[j].Get("name") != "ModuleInventoryPart") continue;
                Node m = KspNodes.FromKsp(now[k++].moduleValues);
                m.Name = "MODULE";
                if (m.Get("name") == null) m.Values.Insert(0, new KeyValuePair<string, string>("name", "ModuleInventoryPart"));
                part.Nodes[j] = m;
            }
        }

        /// <summary>The wear this flight's hardest moments added, from any of these parts (Core.Wear).</summary>
        static double FlightWear(IEnumerable<ProtoPartSnapshot> parts)
        {
            double speed = 0, heat = 0, g = 0;
            foreach (ProtoPartSnapshot ps in parts)
            {
                speed = Math.Max(speed, Val(ps, "peakSpeed"));
                heat = Math.Max(heat, Val(ps, "peakHeat"));
                g = Math.Max(g, Val(ps, "peakG"));
            }
            double added = Core.Wear.OfFlight(speed, heat, g, S);
            Log.Info("This flight's hardest moments: " + Math.Round(speed) + " m/s, " + Math.Round(heat * 100) + "% of the heat limit, " + g.ToString("0.0") + " G: wear + " + Core.Wear.Percent(added));
            return added;
        }

        static double Val(ProtoPartSnapshot ps, string key) => LaunchRecord.Num(PRSPart.ValueIn(ps, key));

        readonly HashSet<uint> moneyDone = new HashSet<uint>();

        /// <summary>The hangar, bin or queue changed: Phil's Builds' window redraws its hangar copies.</summary>
        internal void Changed() { BuildsLink.HangarChanged(); }

        /// <summary>KSP part ids that broke off in a hard landing, set by stage recovery just before it recovers.</summary>
        public readonly HashSet<uint> LeftBehindOnNextRecovery = new HashSet<uint>();

        /// <summary>How the next recovered piece landed ("312 km downrange, 5.8 m/s on 3 chutes"), set by stage recovery.</summary>
        public string NextLandingNote;

        /// <summary>
        /// KSP has just paid its recovery refund (<paramref name="paid"/>: the value times the distance factor).
        /// The suite keeps the hardware instead, so it takes that refund back and charges the transport home
        /// (the value times what the distance rule holds back); for a sale it leaves half the refund as scrap.
        /// </summary>
        public void SettleRecovery(ProtoVessel pv, double paid, float factor)
        {
            if (pv == null || !Suite.Career || Krash.SimActive || !moneyDone.Add(pv.persistentId)) return;
            if (!pv.protoPartSnapshots.Any(PRSPart.Has)) return;
            RecoverChoice choice = RecoverGate.Choices.TryGetValue(pv.persistentId, out RecoverChoice ch) ? ch : RecoverChoice.Hangar;
            double transport = RecoveryValue.Value(pv) * (1 - factor);
            double adjust = choice == RecoverChoice.Hangar ? paid + transport : paid * (1 - S.ScrapValue);
            Suite.Charge(adjust);
            Log.Info(choice == RecoverChoice.Hangar
                ? "Recovery of " + pv.vesselName + ": kept the hardware, transport " + Pricing.Funds(transport) + " (KSP's refund of " + Pricing.Funds(paid) + " taken back, factor " + factor.ToString("0.00") + ")"
                : "Recovery of " + pv.vesselName + ": sold for scrap, " + Pricing.Funds(paid * S.ScrapValue));
        }

        /// <summary>
        /// What recovering this ship would pay to refurbish parts into the parts bin: all of them when it can't come home
        /// as a ship (no engine or probe core, or not from a filed design), else nothing. For the Recover prompt.
        /// </summary>
        public double BinPriceIfRecovered(ProtoVessel pv)
        {
            double total = 0;
            // As recovery does it: each launch in it on its own (two docked ships).
            foreach (IGrouping<uint, ProtoPartSnapshot> g in pv.protoPartSnapshots.Where(PRSPart.Has).GroupBy(ps => ps.launchID))
            {
                List<ProtoPartSnapshot> parts = g.ToList();
                LaunchRecord rec = parts.Select(ps => RecordOf(ps.launchID, ps.flightID)).FirstOrDefault(r => r != null);
                bool ship = rec != null && RecoveryValue.CanFly(parts.Select(ps => ps.partName))
                            && (!S.RequireDesign || parts.Any(ps => !string.IsNullOrEmpty(PRSPart.ValueIn(ps, "design"))));
                if (ship) continue;
                double wear = Core.Wear.After(rec?.Wear ?? 0, FlightWear(parts));
                total += parts.Sum(ps => KspCosts.Instance.Cost(PartNodeFrom(ps)).Dry) * Pricing.RefurbishShare(wear, S);
            }
            return total;
        }

        // ---- The parts bin ----

        /// <summary>A part's kind in the bin: the part, its Phil's Iterative Parts program and Mk, and its price.</summary>
        public static string BinKeyOf(Node part)
        {
            double dry = KspCosts.Instance.Cost(part).Dry;
            return Hardware.PartName(part) + "|" + (Pip.Title(part) ?? "") + "|" + Math.Round(dry).ToString("0", CultureInfo.InvariantCulture);
        }

        /// <summary>A part as the bin keeps it: forgetting its ship, its fuel and where it was attached.</summary>
        static Node BinSample(Node part)
        {
            Node n = part.Clone();
            n.Values.RemoveAll(kv => kv.Key == "link" || kv.Key == "sym" || kv.Key == "attN" || kv.Key == "srfN");
            foreach (Node r in n.Children("RESOURCE")) r.Set("amount", "0");
            Node m = Hardware.Module(n);
            if (m != null)
            {
                m.Values.Clear();
                m.Add("name", Core.Craft.ModuleName);
                m.Add("isEnabled", "True");
            }
            return n;
        }

        /// <summary>Puts parts of this kind in the bin (already refurbished: it's like a new part).</summary>
        public void AddToBin(Node part, int count = 1)
        {
            if (part == null || count <= 0) return;
            Node sample = BinSample(part);
            string key = BinKeyOf(sample);
            BinKind k = Bin.FirstOrDefault(b => b.Key == key);
            if (k == null) Bin.Add(k = new BinKind { Key = key, Sample = sample });
            k.Count += count;
        }

        /// <summary>A part goes into the bin from a craft at this wear: it's refurbished as it goes in (Phil, round 4), at this price.</summary>
        double ToBin(Node part, double wear)
        {
            double price = KspCosts.Instance.Cost(part).Dry * Pricing.RefurbishShare(wear, S);
            AddToBin(part);
            LastBinPrice += price;
            return price;
        }

        static Node PartNodeFrom(ProtoPartSnapshot ps)
        {
            var n = new Node("PART");
            n.Add("part", ps.partName + "_" + ps.flightID.ToString(CultureInfo.InvariantCulture));
            // What variants or rescaling add to its cost and mass, as KSP saves them, so it matches and sells as itself.
            n.Add("modCost", ps.moduleCosts.ToString("R", CultureInfo.InvariantCulture));
            n.Add("modMass", ps.moduleMass.ToString("R", CultureInfo.InvariantCulture));
            foreach (ProtoPartResourceSnapshot r in ps.resources)
            {
                Node rn = n.AddNode("RESOURCE");
                rn.Add("name", r.resourceName);
                rn.Add("amount", r.amount.ToString("R", CultureInfo.InvariantCulture));
                rn.Add("maxAmount", r.maxAmount.ToString("R", CultureInfo.InvariantCulture));
            }
            Node m = n.AddNode("MODULE");
            m.Add("name", Core.Craft.ModuleName);
            m.Add("isEnabled", "True");
            // Its own hardware (a Phil's Iterative Parts program and Mk), so the bin knows what it is.
            foreach (ProtoPartModuleSnapshot pm in ps.modules)
            {
                if (pm.moduleValues == null || Array.IndexOf(Hardware.HardwareModules, pm.moduleName) < 0) continue;
                Node hw = KspNodes.FromKsp(pm.moduleValues);
                hw.Name = "MODULE";
                if (hw.Get("name") == null) hw.Values.Insert(0, new KeyValuePair<string, string>("name", pm.moduleName));
                n.Nodes.Add(hw);
            }
            return n;
        }

        // ---- Tidying ----

        /// <summary>KSP's ids of every part still in flight.</summary>
        static HashSet<uint> Flying()
        {
            FlightState fs = HighLogic.CurrentGame?.flightState;
            return fs == null ? null : new HashSet<uint>(fs.protoVessels.SelectMany(pv => pv.protoPartSnapshots).Select(ps => ps.flightID));
        }

        /// <summary>Drops launch records none of whose parts still exist in flight.</summary>
        void Prune()
        {
            HashSet<uint> live = Flying();
            if (live == null) return;
            int before = Launches.Count;
            Launches.RemoveAll(r => !r.Parts.Any(live.Contains));
            if (Launches.Count != before) Log.Info("Dropped " + (before - Launches.Count) + " finished launch records");
        }

        /// <summary>
        /// Drops ships that no longer exist anywhere (taken apart, sold, lost), and empty bin kinds nothing holds. A ship
        /// still exists in flight while the part it rides on (its root part) is still flying.
        /// </summary>
        void Tidy()
        {
            HashSet<uint> live = Flying() ?? new HashSet<uint>();
            var alive = new HashSet<string>(Hangar.Select(h => h.Ship.Id));
            alive.UnionWith(Hangar.SelectMany(h => h.Pieces.Values));
            alive.UnionWith(Jobs.Where(j => j.ShipId != null).Select(j => j.ShipId));
            foreach (LaunchRecord r in Launches)
                foreach (KeyValuePair<string, int> s in r.Ships)
                    if (s.Value >= 0 && s.Value < r.Parts.Count && live.Contains(r.Parts[s.Value])) alive.Add(s.Key);
            int gone = Ships.Keys.Count(id => !alive.Contains(id));
            foreach (string id in Ships.Keys.Where(id => !alive.Contains(id)).ToList()) Ships.Remove(id);
            var held = new HashSet<string>(Jobs.SelectMany(j => j.BinTaken.Keys));
            Bin.RemoveAll(k => k.Count <= 0 && !held.Contains(k.Key));
            if (gone > 0) Log.Info("Dropped " + gone + " ships that no longer exist");
        }

        // ---- Saving ----

        /// <summary>Funds owed back from an older save's queued builds (round 4 starts older saves fresh).</summary>
        double refundDue;

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            node.AddValue("format", Format);
            node.AddValue("lastUT", lastUT.ToString("R", CultureInfo.InvariantCulture));
            if (refundDue > 0) node.AddValue("refundDue", refundDue.ToString("R", CultureInfo.InvariantCulture));
            if (PendingLaunch != null) node.AddValue("pending", PendingLaunch + "," + pendingUT.ToString("R", CultureInfo.InvariantCulture));
            foreach (Ship s in Ships.Values) s.Save(node.AddNode("SHIP"));
            foreach (LaunchRecord r in Launches) r.Save(node.AddNode("LAUNCH"));
            foreach (HangarShip h in Hangar) h.Save(node.AddNode("HANGAR_SHIP"));
            foreach (BinKind b in Bin) b.Save(node.AddNode("BIN"));
            foreach (BuildJob j in Jobs) j.Save(node.AddNode("BUILD"));
            foreach (KeyValuePair<string, int> kv in lastNumber)
            {
                ConfigNode n = node.AddNode("NUMBER");
                n.AddValue("name", kv.Key);
                n.AddValue("last", kv.Value);
            }
            foreach (KeyValuePair<string, Dictionary<int, string>> c in cancelled)
            {
                ConfigNode n = node.AddNode("CANCELLED");
                n.AddValue("craftId", c.Key);
                foreach (KeyValuePair<int, string> o in c.Value) n.AddValue("owner", o.Key.ToString(CultureInfo.InvariantCulture) + "," + o.Value);
            }
        }

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            Launches.Clear(); Hangar.Clear(); Bin.Clear(); Ships.Clear(); lastNumber.Clear(); Jobs.Clear(); cancelled.Clear();
            PendingLaunch = null;
            lastUT = node.HasValue("lastUT") ? LaunchRecord.Num(node.GetValue("lastUT")) : -1;
            if (lastUT <= 0) lastUT = -1;
            refundDue = LaunchRecord.Num(node.GetValue("refundDue"));
            foreach (ConfigNode n in node.GetNodes("NUMBER"))
                if (int.TryParse(n.GetValue("last"), out int last) && n.GetValue("name") != null) lastNumber[n.GetValue("name")] = last;
            int format = int.TryParse(node.GetValue("format"), out int f) ? f : 1;
            if (format < Format) { StartFresh(node); return; }

            string pending = node.GetValue("pending");
            if (pending != null && pending.IndexOf(',') > 0) { PendingLaunch = pending.Substring(0, pending.IndexOf(',')); pendingUT = LaunchRecord.Num(pending.Substring(pending.IndexOf(',') + 1)); }
            foreach (ConfigNode n in node.GetNodes("SHIP")) { Ship s = Ship.Load(n); if (s != null) Ships[s.Id] = s; }
            foreach (ConfigNode n in node.GetNodes("LAUNCH")) { LaunchRecord r = LaunchRecord.Load(n); if (r != null) Launches.Add(r); }
            foreach (ConfigNode n in node.GetNodes("HANGAR_SHIP")) { HangarShip h = HangarShip.Load(n, Ships); if (h != null) Hangar.Add(h); }
            foreach (ConfigNode n in node.GetNodes("BIN")) { BinKind b = BinKind.Load(n); if (b != null) Bin.Add(b); }
            foreach (ConfigNode n in node.GetNodes("BUILD")) { BuildJob j = BuildJob.Load(n); if (j != null) Jobs.Add(j); }
            foreach (ConfigNode n in node.GetNodes("CANCELLED"))
            {
                var owners = new Dictionary<int, string>();
                foreach (string o in n.GetValues("owner"))
                {
                    int comma = o.IndexOf(',');
                    if (comma > 0 && int.TryParse(o.Substring(0, comma), out int i)) owners[i] = o.Substring(comma + 1);
                }
                if (n.GetValue("craftId") != null) cancelled.Add(new KeyValuePair<string, Dictionary<int, string>>(n.GetValue("craftId"), owners));
            }
            if (HighLogic.LoadedScene == GameScenes.SPACECENTER) { Prune(); Tidy(); }
        }

        /// <summary>
        /// A save from before round 4 starts the suite fresh (Phil: start fresh, no converter): an empty
        /// hangar, bin and queue, with queued builds refunded. Ships still in flight have no launch record any more, so
        /// their parts go to the parts bin when they're recovered (Phil: "Parts bin"). Numbers already given out stay given.
        /// The save file itself isn't touched; the next save writes the new format.
        /// </summary>
        void StartFresh(ConfigNode node)
        {
            bool any = node.HasNode("HANGAR_SHIP") || node.HasNode("BIN_PART") || node.HasNode("BUILD") || node.HasNode("LAUNCH");
            if (!any) return;
            foreach (ConfigNode b in node.GetNodes("BUILD"))
            {
                double paid = LaunchRecord.Num(b.GetValue("paid")), progress = LaunchRecord.Num(b.GetValue("progress"));
                double price = LaunchRecord.Num(b.GetValue("newPrice")) + LaunchRecord.Num(b.GetValue("refurbishPrice") ?? b.GetValue("refitPrice"));
                refundDue += paid + (b.GetValue("overtime") == "True" ? Pricing.OvertimeCharge(price, 1 - progress, S) : 0);
            }
            Log.Warn("This save is from an older version of the suite: starting fresh (empty hangar, parts bin and build queue"
                     + (refundDue > 0 ? ", " + Pricing.Funds(refundDue) + " refunded for queued builds" : "") + "; ships still in flight go to the parts bin when recovered)");
        }
    }
}
