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
        /// <summary>How full its tanks came back, 0 to 1 (fuel only, not electric charge).</summary>
        public double Fuel = 1;
        /// <summary>The loadable craft, with tanks and parts as they left the pad last time.</summary>
        public Node Craft;
        public readonly List<string> Placed = new List<string>();
        public readonly List<string> Missing = new List<string>();

        public string DisplayName => Name + " #" + Number;

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
            foreach (string s in Missing) n.AddValue("missing", s);
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
                Craft = KspNodes.FromKsp(c),
            };
            h.Missing.AddRange(n.GetValues("missing"));
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

        public void Save(ConfigNode n)
        {
            n.AddValue("serial", Serial ?? "");
            n.AddValue("part", Part ?? "");
            n.AddValue("flights", Flights);
        }

        public static BinPart Load(ConfigNode n)
        {
            return new BinPart
            {
                Serial = n.GetValue("serial"),
                Part = n.GetValue("part"),
                Flights = int.TryParse(n.GetValue("flights"), out int f) ? f : 0,
            };
        }
    }
}
