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
        /// <summary>The vessel this launch became (0 until it's known), so going back to it on the pad isn't a new launch.</summary>
        public uint VesselId;

        public void Save(ConfigNode n)
        {
            n.AddValue("id", Id);
            n.AddValue("ut", UT.ToString("R", CultureInfo.InvariantCulture));
            n.AddValue("ship", Ship);
            if (VesselId != 0) n.AddValue("vessel", VesselId.ToString(CultureInfo.InvariantCulture));
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
            if (uint.TryParse(n.GetValue("vessel") ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out uint vid)) r.VesselId = vid;
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
        /// <summary>Fuelled and paid for (newly built, refurbished, or refuelled): it launches at no cost.</summary>
        public bool Ready;
        /// <summary>How hard its flights have been, 0 to 1 (Core.Wear): one number for the whole craft.</summary>
        public double Wear;
        /// <summary>How much of the fuel it launched with came back, 0 to 1 (fuel only, not electric charge).</summary>
        public double Fuel = 1;
        /// <summary>Resources each part still held when it came home, by serial (empty once refuelled).</summary>
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

        /// <summary>Sets the craft's wear, on the ship and on every part (which carry it into the editor and into
        /// flight), and clears the parts' record of their last flight.</summary>
        public void SetWear(double wear)
        {
            Wear = Core.Pricing.Clamp01(wear);
            foreach (Node p in Craft.Children("PART"))
            {
                Node m = p.Children("MODULE").FirstOrDefault(x => x.Get("name") == Core.Craft.ModuleName);
                if (m == null) continue;
                m.Set("wear", Wear.ToString("R", CultureInfo.InvariantCulture));
                m.Set("peakSpeed", "0"); m.Set("peakHeat", "0"); m.Set("peakG", "0");
            }
        }

        /// <summary>
        /// How much of its launch fuel came back: what each part held when it came home against what it held when it
        /// left the pad (its craft), so a booster loaded to 30% on purpose that comes back with all of that is full.
        /// </summary>
        public void UpdateFuel()
        {
            double launched = 0, back = 0;
            if (!Ready && PartResourceLibrary.Instance != null)
                foreach (Node p in Craft.Children("PART"))
                {
                    string s = Core.Craft.SerialOf(p);
                    CameBack.TryGetValue(s ?? "", out Dictionary<string, double> left);
                    foreach (Node r in p.Children("RESOURCE"))
                    {
                        string name = r.Get("name");
                        PartResourceDefinition def = name != null ? PartResourceLibrary.Instance.GetDefinition(name) : null;
                        if (def == null || def.density <= 0) continue;
                        launched += Num(r.Get("amount"));
                        if (left != null && left.TryGetValue(name, out double l)) back += l;
                    }
                }
            Fuel = launched > 0 ? Math.Min(1, back / launched) : 1;
        }

        static double Num(string s) => LaunchRecord.Num(s);

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
            n.AddValue("wear", Wear.ToString("R", CultureInfo.InvariantCulture));
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
                Wear = Core.Pricing.Clamp01(LaunchRecord.Num(n.GetValue("wear"))),
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
            h.UpdateFuel();
            return h;
        }
    }

    /// <summary>A loose recovered part.</summary>
    public sealed class BinPart
    {
        public string Serial;
        public string Part;
        public int Flights;
        /// <summary>The wear of the craft it came off (kept in its part).</summary>
        public double Wear => Core.Wear.In(PartNode?.Children("MODULE").FirstOrDefault(m => m.Get("name") == Core.Craft.ModuleName));
        /// <summary>The build using this part, if any.</summary>
        public string ReservedBy;
        /// <summary>The part as it was in its craft (for reuse in a new build), if known.</summary>
        public Node PartNode;
        /// <summary>
        /// What it still holds (a pod's monopropellant), so a build only tops it up. Null when not known (parts binned
        /// before this was kept): then it's filled from empty. Play-test #9: Gallant I #6 paid for a whole load again.
        /// </summary>
        public Dictionary<string, double> Left;

        public void Save(ConfigNode n)
        {
            n.AddValue("serial", Serial ?? "");
            n.AddValue("part", Part ?? "");
            n.AddValue("flights", Flights);
            if (!string.IsNullOrEmpty(ReservedBy)) n.AddValue("reservedBy", ReservedBy);
            if (PartNode != null) n.AddNode(KspNodes.ToKsp(PartNode, "PART"));
            if (Left != null)
            {
                ConfigNode left = n.AddNode("LEFT");
                foreach (KeyValuePair<string, double> r in Left) left.AddValue(r.Key, r.Value.ToString("R", CultureInfo.InvariantCulture));
            }
        }

        public static BinPart Load(ConfigNode n)
        {
            ConfigNode p = n.GetNode("PART");
            var b = new BinPart
            {
                Serial = n.GetValue("serial"),
                Part = n.GetValue("part"),
                Flights = int.TryParse(n.GetValue("flights"), out int f) ? f : 0,
                ReservedBy = n.GetValue("reservedBy"),
                PartNode = p != null ? KspNodes.FromKsp(p) : null,
            };
            ConfigNode l = n.GetNode("LEFT");
            if (l != null)
            {
                b.Left = new Dictionary<string, double>();
                foreach (ConfigNode.Value v in l.values) b.Left[v.name] = LaunchRecord.Num(v.value);
            }
            return b;
        }

        /// <summary>What a part holds by its craft node's resources (a ready ship's part is as loaded).</summary>
        public static Dictionary<string, double> Held(Node part)
        {
            var d = new Dictionary<string, double>();
            foreach (Node r in part.Children("RESOURCE"))
            {
                string name = r.Get("name");
                if (name != null) d[name] = (d.TryGetValue(name, out double had) ? had : 0) + LaunchRecord.Num(r.Get("amount"));
            }
            return d;
        }
    }

    /// <summary>A ship being built (or refurbished) on a build line.</summary>
    public sealed class BuildJob
    {
        public string Id;
        public string Name;
        /// <summary>"VAB" or "SPH".</summary>
        public string Facility;
        /// <summary>The ship as it will roll out, with every part's serial.</summary>
        public Node Craft;
        public double NewPrice, RefurbishPrice, FuelPrice;
        /// <summary>What was charged for the build itself (new parts, refurbishing and fuel): refunded on cancel.</summary>
        public double Paid;
        /// <summary>Share of the work done, 0 to 1.</summary>
        public double Progress;
        public bool Overtime;
        public double CreatedUT;
        /// <summary>A refurbish or restore of one hangar ship: when done, that same ship is ready again, at 0% wear.</summary>
        public string FromShipId;

        public double BuildPrice => NewPrice + RefurbishPrice;
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
            n.AddValue("refurbishPrice", RefurbishPrice.ToString("R", CultureInfo.InvariantCulture));
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
                RefurbishPrice = LaunchRecord.Num(n.GetValue("refurbishPrice") ?? n.GetValue("refitPrice")),
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
