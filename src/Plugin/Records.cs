using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PhilsReusabilitySuite.Core;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// A ship: its name and number, the design it was first built as, its own blueprint, its own launches and its flight
    /// log. It lives while it exists anywhere (in the hangar, in a build, or in flight), and nothing about it is kept on
    /// its parts (Phil, round 4). However it's rebuilt, it stays this ship (Phil: "that ship is still the Peleus III #2").
    /// Launchers aren't ships (Phil: "whatever is saved as the payload - 'Peleus III #2' for example is the ship").
    /// </summary>
    public sealed class Ship
    {
        public string Id;
        public string Name;
        public int Number;
        /// <summary>The Phil's Builds design it was first built as (the design in its name), set once and never changed:
        /// Phil's Builds counts it there ("Peleus III · 1 in hangar: Peleus III #2"), whatever it's rebuilt into.</summary>
        public string DesignId;
        /// <summary>Its own launches (Phil: "the flight count is definitely the ship's own").</summary>
        public int Flights;
        /// <summary>Its flight log, newest first.</summary>
        public readonly List<string> Log = new List<string>();
        /// <summary>
        /// Its own blueprint (Phil, round 4: "couldn't it have a blueprint of the 'Peleus III #2' and restore uses that?"):
        /// the ship as it was last built, without the launcher under it. Kept here, never as a craft file. Restore puts
        /// back whatever of it didn't come home.
        /// </summary>
        public Node Blueprint;
        public int BlueprintParts => Blueprint?.Children("PART").Count() ?? 0;

        public string DisplayName => Name + " #" + Number;

        public void AddLog(string entry)
        {
            Log.Insert(0, entry);
            if (Log.Count > 30) Log.RemoveRange(30, Log.Count - 30);
        }

        public void Save(ConfigNode n)
        {
            n.AddValue("id", Id);
            n.AddValue("name", Name);
            n.AddValue("number", Number);
            if (!string.IsNullOrEmpty(DesignId)) n.AddValue("design", DesignId);
            n.AddValue("flights", Flights);
            foreach (string e in Log) n.AddValue("flight", e);
            if (Blueprint != null) n.AddNode(KspNodes.ToKsp(Blueprint, "BLUEPRINT"));
        }

        public static Ship Load(ConfigNode n)
        {
            if (n.GetValue("id") == null) return null;
            var s = new Ship
            {
                Id = n.GetValue("id"),
                Name = n.GetValue("name") ?? "Unnamed",
                Number = int.TryParse(n.GetValue("number"), out int num) ? num : 1,
                DesignId = n.GetValue("design"),
                Flights = int.TryParse(n.GetValue("flights"), out int f) ? f : 0,
            };
            s.Log.AddRange(n.GetValues("flight"));
            ConfigNode bp = n.GetNode("BLUEPRINT");
            if (bp != null) s.Blueprint = KspNodes.FromKsp(bp);
            return s;
        }
    }

    /// <summary>
    /// A launch exactly as it left the pad: the craft KSP launched, KSP's own id for each of its parts, the ship that went
    /// up in it (the suite started the launch, so it knows) and which parts are that ship's. When something comes home,
    /// the suite looks up the launch it came from (Phil: "see what launch that something came from and what ships (and
    /// their IDs) were used in that launch, and then categorize them accordingly").
    /// </summary>
    public sealed class LaunchRecord
    {
        /// <summary>KSP's id for the launch, on every part of it.</summary>
        public uint LaunchId;
        public double UT;
        /// <summary>What it launched as ("Kupe I #1").</summary>
        public string Name;
        public Node Craft;
        /// <summary>The launched craft's wear as it left (one number per craft).</summary>
        public double Wear;
        /// <summary>KSP's id for each part of the craft, in the craft's order.</summary>
        public readonly List<uint> Parts = new List<uint>();
        /// <summary>The ship that went up in it, or null (launched without being built: it's all parts).</summary>
        public string ShipId;
        /// <summary>The ship has come home from this launch: anything else of it that comes home later is parts.</summary>
        public bool ShipHome;
        /// <summary>Each part's place in the ship's blueprint, in the craft's order (-1: launcher parts, not the ship).</summary>
        public readonly List<int> Slots = new List<int>();

        /// <summary>A part's place in the ship's blueprint, by its place in the launch (-1: not the ship's).</summary>
        public int SlotOf(int index) => index >= 0 && index < Slots.Count ? Slots[index] : -1;

        public int IndexOf(uint flightId) => Parts.IndexOf(flightId);

        public void Save(ConfigNode n)
        {
            n.AddValue("launch", LaunchId.ToString(CultureInfo.InvariantCulture));
            n.AddValue("ut", UT.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("name", Name ?? "");
            n.AddValue("wear", Wear.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("parts", string.Join(",", Parts.Select(p => p.ToString(CultureInfo.InvariantCulture)).ToArray()));
            if (ShipId != null) n.AddValue("ship", ShipId);
            if (ShipHome) n.AddValue("home", true);
            n.AddValue("slots", string.Join(",", Slots.Select(s => s.ToString(CultureInfo.InvariantCulture)).ToArray()));
            n.AddNode(KspNodes.ToKsp(Craft, "CRAFT"));
        }

        public static LaunchRecord Load(ConfigNode n)
        {
            ConfigNode c = n.GetNode("CRAFT");
            if (c == null || !uint.TryParse(n.GetValue("launch") ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id)) return null;
            var r = new LaunchRecord { LaunchId = id, UT = Num(n.GetValue("ut")), Name = n.GetValue("name") ?? "", Wear = Num(n.GetValue("wear")), Craft = KspNodes.FromKsp(c), ShipId = n.GetValue("ship"), ShipHome = n.GetValue("home") == "True" };
            foreach (string p in (n.GetValue("parts") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                r.Parts.Add(uint.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint f) ? f : 0);
            r.Slots.AddRange(Ints(n.GetValue("slots")));
            return r;
        }

        internal static double Num(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;

        internal static IEnumerable<int> Ints(string csv) =>
            (csv ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out int i) ? i : -1);
    }

    /// <summary>A ship waiting in the hangar, exactly as it came back (or as it was built).</summary>
    public sealed class HangarShip
    {
        /// <summary>The ship it is.</summary>
        public Ship Ship;
        public string Id => Ship.Id;
        public string Name => Ship.Name;
        public int Number => Ship.Number;
        public string DisplayName => Ship.DisplayName;
        public int Flights => Ship.Flights;

        /// <summary>This copy of its craft. A new id whenever its parts change (built, flown, home again), so a VAB
        /// note from an older copy points at nothing.</summary>
        public string CraftId = NewId();
        public double UT;
        /// <summary>Fuelled and paid for (newly built, refurbished, or refuelled): it launches at no cost.</summary>
        public bool Ready;
        /// <summary>How hard its flights have been, 0 to 1 (Core.Wear): one number for the whole craft.</summary>
        public double Wear;
        /// <summary>How much of the fuel it launched with came back, 0 to 1 (fuel only, not electric charge).</summary>
        public double Fuel = 1;
        /// <summary>Resources each part still held when it came home, by its place in the craft (empty once refuelled).</summary>
        public readonly Dictionary<int, Dictionary<string, double>> CameBack = new Dictionary<int, Dictionary<string, double>>();
        /// <summary>The build that has taken this ship, if any (it's then shown greyed out).</summary>
        public string ReservedBy;
        /// <summary>The loadable craft, with tanks and parts as they left the pad last time.</summary>
        public Node Craft;
        /// <summary>Parts of its blueprint that didn't come home (Restore puts them back).</summary>
        public int Missing;
        /// <summary>Each of its parts' place in its ship's blueprint (-1: not in it, such as the launcher it's built on).</summary>
        public readonly List<int> Slots = new List<int>();

        public static string NewId() => Guid.NewGuid().ToString("N");

        int partCount = -1;
        Node counted;
        public int PartCount
        {
            get
            {
                if (!ReferenceEquals(counted, Craft)) { counted = Craft; partCount = Craft.Children("PART").Count(); }
                return partCount;
            }
        }


        /// <summary>The craft with every part noted as this ship's hardware (for the VAB, a launch or a quote).</summary>
        public Node Noted() => Hardware.Noted(Craft, CraftId);

        /// <summary>
        /// As it was just built: this craft, and its ship's blueprint is the craft without these parts (the launcher it's
        /// built on: Phil, "if I have it saved as just the ship, and slap a Boulder 001 underneath it").
        /// </summary>
        public void Built(Node craft, ICollection<int> notShip)
        {
            Craft = craft;
            Slots.Clear();
            PieceResult bp = Pieces.Build(craft, Enumerable.Range(0, PartCount).Where(i => !notShip.Contains(i)).ToList());
            Ship.Blueprint = Hardware.Plain(bp.Craft ?? craft.Clone());
            for (int i = 0; i < PartCount; i++) Slots.Add(bp.Craft != null ? bp.Placed.IndexOf(i) : i);
        }

        /// <summary>
        /// How much of its launch fuel came back: what each part held when it came home against what it held when it
        /// left the pad (its craft), so a booster loaded to 30% on purpose that comes back with all of that is full.
        /// </summary>
        public void UpdateFuel()
        {
            double launched = 0, back = 0;
            if (!Ready && PartResourceLibrary.Instance != null)
            {
                int i = -1;
                foreach (Node p in Craft.Children("PART"))
                {
                    i++;
                    CameBack.TryGetValue(i, out Dictionary<string, double> left);
                    foreach (Node r in p.Children("RESOURCE"))
                    {
                        string name = r.Get("name");
                        PartResourceDefinition def = name != null ? PartResourceLibrary.Instance.GetDefinition(name) : null;
                        if (def == null || def.density <= 0) continue;
                        launched += LaunchRecord.Num(r.Get("amount"));
                        if (left != null && left.TryGetValue(name, out double l)) back += l;
                    }
                }
            }
            Fuel = launched > 0 ? Math.Min(1, back / launched) : 1;
        }

        public void Save(ConfigNode n)
        {
            n.AddValue("ship", Ship.Id);
            n.AddValue("craftId", CraftId);
            n.AddValue("ut", UT.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("wear", Wear.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("ready", Ready);
            n.AddValue("missing", Missing);
            if (!string.IsNullOrEmpty(ReservedBy)) n.AddValue("reservedBy", ReservedBy);
            n.AddValue("slots", string.Join(",", Slots.Select(s => s.ToString(CultureInfo.InvariantCulture)).ToArray()));
            foreach (KeyValuePair<int, Dictionary<string, double>> kv in CameBack)
            {
                ConfigNode left = n.AddNode("LEFT");
                left.AddValue("part", kv.Key);
                foreach (KeyValuePair<string, double> r in kv.Value) left.AddValue(r.Key, r.Value.ToString("R", CultureInfo.InvariantCulture));
            }
            n.AddNode(KspNodes.ToKsp(Craft, "CRAFT"));
        }

        public static HangarShip Load(ConfigNode n, Dictionary<string, Ship> ships)
        {
            ConfigNode c = n.GetNode("CRAFT");
            if (c == null || n.GetValue("ship") == null || !ships.TryGetValue(n.GetValue("ship"), out Ship ship)) return null;
            var h = new HangarShip
            {
                Ship = ship,
                CraftId = n.GetValue("craftId") ?? NewId(),
                UT = LaunchRecord.Num(n.GetValue("ut")),
                Wear = Core.Pricing.Clamp01(LaunchRecord.Num(n.GetValue("wear"))),
                Ready = n.GetValue("ready") == "True",
                Missing = int.TryParse(n.GetValue("missing"), out int miss) ? miss : 0,
                ReservedBy = n.GetValue("reservedBy"),
                Craft = KspNodes.FromKsp(c),
            };
            h.Slots.AddRange(LaunchRecord.Ints(n.GetValue("slots")));
            foreach (ConfigNode left in n.GetNodes("LEFT"))
            {
                if (!int.TryParse(left.GetValue("part"), out int i)) continue;
                var amounts = new Dictionary<string, double>();
                foreach (ConfigNode.Value v in left.values)
                    if (v.name != "part") amounts[v.name] = LaunchRecord.Num(v.value);
                h.CameBack[i] = amounts;
            }
            h.UpdateFuel();
            return h;
        }
    }

    /// <summary>
    /// One kind of part in the parts bin, and how many (Phil, round 4: "hey we have this thing"). A part forgets its
    /// ship, its flights and its fuel as it goes in, and is refurbished then, so it's exactly like a new part that's
    /// already paid for. The kind is the part, its Phil's Iterative Parts program and Mk (Phil: "a mark 2 and mark 3
    /// are different parts"), and its price (which covers variants and rescaling).
    /// </summary>
    public sealed class BinKind
    {
        public string Key;
        public int Count;
        /// <summary>One of them, as a part node (empty of resources, no note).</summary>
        public Node Sample;

        public void Save(ConfigNode n)
        {
            n.AddValue("key", Key);
            n.AddValue("count", Count);
            n.AddNode(KspNodes.ToKsp(Sample, "PART"));
        }

        public static BinKind Load(ConfigNode n)
        {
            ConfigNode p = n.GetNode("PART");
            if (p == null || n.GetValue("key") == null) return null;
            return new BinKind { Key = n.GetValue("key"), Count = int.TryParse(n.GetValue("count"), out int c) ? c : 0, Sample = KspNodes.FromKsp(p) };
        }
    }

    /// <summary>A ship being built (or refurbished) on a build line.</summary>
    public sealed class BuildJob
    {
        public string Id;
        /// <summary>What it's built as (the design's name).</summary>
        public string Name;
        /// <summary>"VAB" or "SPH".</summary>
        public string Facility;
        /// <summary>The ship as it will roll out.</summary>
        public Node Craft;
        /// <summary>This version of its craft (a VAB note "craftId:index" points at its parts while it's queued, and at
        /// the hangar ship it becomes once it's done).</summary>
        public string CraftId = HangarShip.NewId();
        public double NewPrice, RefurbishPrice, FuelPrice;
        /// <summary>What was charged for the build itself (new parts, refurbishing and fuel): refunded on cancel.</summary>
        public double Paid;
        /// <summary>Share of the work done, 0 to 1.</summary>
        public double Progress;
        public bool Overtime;
        public double CreatedUT;
        /// <summary>The ship it keeps (a refit, refurbish or restore): when done, that same ship is ready again. Null: a new ship.</summary>
        public string ShipId;
        /// <summary>Its parts that are hangar hardware: craft part to "hangarShipId:index".</summary>
        public readonly Dictionary<int, string> Owners = new Dictionary<int, string>();
        /// <summary>Parts it took from the parts bin, by kind (given back if it's cancelled).</summary>
        public readonly Dictionary<string, int> BinTaken = new Dictionary<string, int>();

        public double BuildPrice => NewPrice + RefurbishPrice;
        public double Seconds(Core.Settings s) => Pricing.BuildSeconds(BuildPrice, s);
        public double Speed(Core.Settings s) => Overtime ? s.OvertimeSpeed : 1;
        public double SecondsLeft(Core.Settings s) => (1 - Progress) * Seconds(s) / Speed(s);

        public Node Noted() => Hardware.Noted(Craft, CraftId);

        public void Save(ConfigNode n)
        {
            n.AddValue("id", Id);
            n.AddValue("name", Name);
            n.AddValue("facility", Facility);
            n.AddValue("craftId", CraftId);
            n.AddValue("newPrice", NewPrice.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("refurbishPrice", RefurbishPrice.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("fuelPrice", FuelPrice.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("paid", Paid.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("progress", Progress.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("overtime", Overtime);
            n.AddValue("created", CreatedUT.ToString("R", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(ShipId)) n.AddValue("ship", ShipId);
            foreach (KeyValuePair<int, string> o in Owners) n.AddValue("owner", o.Key.ToString(CultureInfo.InvariantCulture) + "," + o.Value);
            foreach (KeyValuePair<string, int> b in BinTaken) n.AddValue("bin", b.Value.ToString(CultureInfo.InvariantCulture) + "," + b.Key);
            n.AddNode(KspNodes.ToKsp(Craft, "CRAFT"));
        }

        public static BuildJob Load(ConfigNode n)
        {
            ConfigNode c = n.GetNode("CRAFT");
            if (c == null) return null;
            var j = new BuildJob
            {
                Id = n.GetValue("id") ?? HangarShip.NewId(),
                Name = n.GetValue("name") ?? "Unnamed",
                Facility = n.GetValue("facility") == "SPH" ? "SPH" : "VAB",
                CraftId = n.GetValue("craftId") ?? HangarShip.NewId(),
                NewPrice = LaunchRecord.Num(n.GetValue("newPrice")),
                RefurbishPrice = LaunchRecord.Num(n.GetValue("refurbishPrice")),
                FuelPrice = LaunchRecord.Num(n.GetValue("fuelPrice")),
                Paid = LaunchRecord.Num(n.GetValue("paid")),
                Progress = LaunchRecord.Num(n.GetValue("progress")),
                Overtime = n.GetValue("overtime") == "True",
                CreatedUT = LaunchRecord.Num(n.GetValue("created")),
                ShipId = n.GetValue("ship"),
                Craft = KspNodes.FromKsp(c),
            };
            foreach (string o in n.GetValues("owner"))
            {
                int comma = o.IndexOf(',');
                if (comma > 0 && int.TryParse(o.Substring(0, comma), out int i)) j.Owners[i] = o.Substring(comma + 1);
            }
            foreach (string b in n.GetValues("bin"))
            {
                int comma = b.IndexOf(',');
                if (comma > 0 && int.TryParse(b.Substring(0, comma), out int count)) j.BinTaken[b.Substring(comma + 1)] = count;
            }
            return j;
        }
    }
}
