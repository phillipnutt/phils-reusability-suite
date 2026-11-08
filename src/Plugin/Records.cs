using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PhilsReusabilitySuite.Core;

namespace PhilsReusabilitySuite
{
    /// <summary>A ship exactly as it left the pad: the craft KSP launched, with every part's serial in it.</summary>
    public sealed class LaunchRecord
    {
        public string Id;
        public double UT;
        public string Ship;
        public Node Craft;
        public readonly HashSet<string> Serials = new HashSet<string>();

        public void Save(ConfigNode n)
        {
            n.AddValue("id", Id);
            n.AddValue("ut", UT.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("ship", Ship);
            n.AddNode(KspNodes.ToKsp(Craft, "CRAFT"));
        }

        public static LaunchRecord Load(ConfigNode n)
        {
            ConfigNode c = n.GetNode("CRAFT");
            if (c == null) return null;
            var r = new LaunchRecord
            {
                Id = n.GetValue("id") ?? Guid.NewGuid().ToString("N"),
                UT = Num(n.GetValue("ut")),
                Ship = n.GetValue("ship") ?? "",
                Craft = KspNodes.FromKsp(c),
            };
            r.IndexSerials();
            return r;
        }

        public void IndexSerials()
        {
            Serials.Clear();
            foreach (Node p in Craft.Children("PART"))
            {
                string s = Core.Craft.SerialOf(p);
                if (!string.IsNullOrEmpty(s)) Serials.Add(s);
            }
        }

        internal static double Num(string s)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
        }
    }

    /// <summary>A recovered ship waiting in the hangar, exactly as it came back.</summary>
    public sealed class HangarShip
    {
        public string Id;
        public string Name;
        public int Number;
        public double UT;
        public string LaunchId;
        public string RootSerial;
        public int Flights;
        /// <summary>Refitted and refuelled (or newly built): it can launch as it is.</summary>
        public bool Ready;
        /// <summary>How full its tanks came back, 0 to 1 (fuel only, not electric charge).</summary>
        public double Fuel = 1;
        /// <summary>Resources each part still held when it came home, by serial (empty once refitted).</summary>
        public readonly Dictionary<string, Dictionary<string, double>> CameBack = new Dictionary<string, Dictionary<string, double>>();
        /// <summary>The build that has taken this ship, if any (it's then shown greyed out).</summary>
        public string ReservedBy;
        /// <summary>The loadable craft, with tanks and parts as they left the pad last time.</summary>
        public Node Craft;
        public readonly List<string> Placed = new List<string>();
        public readonly List<string> Missing = new List<string>();

        public string DisplayName => Name + " #" + Number;

        /// <summary>The identity its parts carry: "Name|Number".</summary>
        public string Hull => Name + "|" + Number;

        /// <summary>Marks the craft's parts as belonging to this ship (only parts with no ship yet, unless told).</summary>
        public void StampHull(bool overwrite)
        {
            foreach (Node p in Craft.Children("PART"))
            {
                Node m = p.Children("MODULE").FirstOrDefault(x => x.Get("name") == Core.Craft.ModuleName);
                if (m == null) continue;
                if (overwrite || string.IsNullOrEmpty(m.Get("hull"))) m.Set("hull", Hull);
            }
        }

        public static bool ParseHull(string hull, out string name, out int number)
        {
            name = null; number = 0;
            if (string.IsNullOrEmpty(hull)) return false;
            int bar = hull.LastIndexOf('|');
            if (bar <= 0 || !int.TryParse(hull.Substring(bar + 1), out number)) return false;
            name = hull.Substring(0, bar);
            return true;
        }

        public void Save(ConfigNode n)
        {
            n.AddValue("id", Id);
            n.AddValue("name", Name);
            n.AddValue("number", Number);
            n.AddValue("ut", UT.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("launch", LaunchId ?? "");
            n.AddValue("root", RootSerial ?? "");
            n.AddValue("flights", Flights);
            n.AddValue("fuel", Fuel.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("ready", Ready);
            if (!string.IsNullOrEmpty(ReservedBy)) n.AddValue("reservedBy", ReservedBy);
            foreach (string s in Missing) n.AddValue("missing", s);
            foreach (KeyValuePair<string, Dictionary<string, double>> kv in CameBack)
            {
                ConfigNode left = n.AddNode("LEFT");
                left.AddValue("serial", kv.Key);
                foreach (KeyValuePair<string, double> r in kv.Value) left.AddValue(r.Key, r.Value.ToString("R", CultureInfo.InvariantCulture));
            }
            n.AddNode(KspNodes.ToKsp(Craft, "CRAFT"));
        }

        public static HangarShip Load(ConfigNode n)
        {
            ConfigNode c = n.GetNode("CRAFT");
            if (c == null) return null;
            var h = new HangarShip
            {
                Id = n.GetValue("id") ?? Guid.NewGuid().ToString("N"),
                Name = n.GetValue("name") ?? "Unnamed",
                Number = int.TryParse(n.GetValue("number"), out int num) ? num : 1,
                UT = LaunchRecord.Num(n.GetValue("ut")),
                LaunchId = n.GetValue("launch"),
                RootSerial = n.GetValue("root"),
                Flights = int.TryParse(n.GetValue("flights"), out int f) ? f : 0,
                Fuel = LaunchRecord.Num(n.GetValue("fuel") ?? "1"),
                Ready = n.GetValue("ready") == "True",
                ReservedBy = n.GetValue("reservedBy"),
                Craft = KspNodes.FromKsp(c),
            };
            h.Missing.AddRange(n.GetValues("missing"));
            foreach (ConfigNode left in n.GetNodes("LEFT"))
            {
                string serial = left.GetValue("serial");
                if (serial == null) continue;
                var amounts = new Dictionary<string, double>();
                foreach (ConfigNode.Value v in left.values)
                    if (v.name != "serial") amounts[v.name] = LaunchRecord.Num(v.value);
                h.CameBack[serial] = amounts;
            }
            foreach (Node p in h.Craft.Children("PART"))
            {
                string s = Core.Craft.SerialOf(p);
                if (!string.IsNullOrEmpty(s)) h.Placed.Add(s);
            }
            return h;
        }
    }

    /// <summary>A loose recovered part.</summary>
    public sealed class BinPart
    {
        public string Serial;
        public string Part;
        public int Flights;
        /// <summary>The build using this part, if any.</summary>
        public string ReservedBy;
        /// <summary>The part as it was in its craft (for reuse in a new build), if known.</summary>
        public Node PartNode;

        public void Save(ConfigNode n)
        {
            n.AddValue("serial", Serial ?? "");
            n.AddValue("part", Part ?? "");
            n.AddValue("flights", Flights);
            if (!string.IsNullOrEmpty(ReservedBy)) n.AddValue("reservedBy", ReservedBy);
            if (PartNode != null) n.AddNode(KspNodes.ToKsp(PartNode, "PART"));
        }

        public static BinPart Load(ConfigNode n)
        {
            ConfigNode p = n.GetNode("PART");
            return new BinPart
            {
                Serial = n.GetValue("serial"),
                Part = n.GetValue("part"),
                Flights = int.TryParse(n.GetValue("flights"), out int f) ? f : 0,
                ReservedBy = n.GetValue("reservedBy"),
                PartNode = p != null ? KspNodes.FromKsp(p) : null,
            };
        }
    }

    /// <summary>A ship being built (or refitted) on a build line.</summary>
    public sealed class BuildJob
    {
        public string Id;
        public string Name;
        /// <summary>"VAB" or "SPH".</summary>
        public string Facility;
        /// <summary>The ship as it will roll out, with every part's serial.</summary>
        public Node Craft;
        public double NewPrice, RefitPrice, FuelPrice;
        /// <summary>What was charged for the build itself (new parts, refit and fuel): refunded on cancel.</summary>
        public double Paid;
        /// <summary>Share of the work done, 0 to 1.</summary>
        public double Progress;
        public bool Overtime;
        public double CreatedUT;
        /// <summary>A refit of one hangar ship: when done, that same ship is ready again.</summary>
        public string FromShipId;

        public double BuildPrice => NewPrice + RefitPrice;
        public double Seconds(Core.Settings s) => Pricing.BuildSeconds(BuildPrice, s);
        public double Speed(Core.Settings s) => Overtime ? s.OvertimeSpeed : 1;
        public double SecondsLeft(Core.Settings s) => (1 - Progress) * Seconds(s) / Speed(s);

        public IEnumerable<string> Serials()
        {
            foreach (Node p in Craft.Children("PART"))
            {
                string s = Core.Craft.SerialOf(p);
                if (!string.IsNullOrEmpty(s)) yield return s;
            }
        }

        public void Save(ConfigNode n)
        {
            n.AddValue("id", Id);
            n.AddValue("name", Name);
            n.AddValue("facility", Facility);
            n.AddValue("newPrice", NewPrice.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("refitPrice", RefitPrice.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("fuelPrice", FuelPrice.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("paid", Paid.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("progress", Progress.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("overtime", Overtime);
            n.AddValue("created", CreatedUT.ToString("R", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(FromShipId)) n.AddValue("fromShip", FromShipId);
            n.AddNode(KspNodes.ToKsp(Craft, "CRAFT"));
        }

        public static BuildJob Load(ConfigNode n)
        {
            ConfigNode c = n.GetNode("CRAFT");
            if (c == null) return null;
            return new BuildJob
            {
                Id = n.GetValue("id") ?? Guid.NewGuid().ToString("N"),
                Name = n.GetValue("name") ?? "Unnamed",
                Facility = n.GetValue("facility") == "SPH" ? "SPH" : "VAB",
                NewPrice = LaunchRecord.Num(n.GetValue("newPrice")),
                RefitPrice = LaunchRecord.Num(n.GetValue("refitPrice")),
                FuelPrice = LaunchRecord.Num(n.GetValue("fuelPrice")),
                Paid = LaunchRecord.Num(n.GetValue("paid")),
                Progress = LaunchRecord.Num(n.GetValue("progress")),
                Overtime = n.GetValue("overtime") == "True",
                CreatedUT = LaunchRecord.Num(n.GetValue("created")),
                FromShipId = n.GetValue("fromShip"),
                Craft = KspNodes.FromKsp(c),
            };
        }
    }
}
