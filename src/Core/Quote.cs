using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PhilsReusabilitySuite.Core
{
    public enum OwnerKind { New, Hangar, Bin }

    /// <summary>Where a part in a build comes from.</summary>
    public sealed class Owner
    {
        public OwnerKind Kind;
        public string ShipId;
        public string Label;
        /// <summary>A hangar ship that is ready to fly: already refitted and refuelled.</summary>
        public bool RefitDone;
        /// <summary>For a bin part swapped in for a new one: its serial and flights.</summary>
        public string BinSerial;
        public int Flights = -1;
        public static readonly Owner NewPart = new Owner { Kind = OwnerKind.New };
    }

    /// <summary>A part's price without resources, and the resources it holds in the craft.</summary>
    public sealed class PartCost
    {
        public double Dry;
        public readonly Dictionary<string, double> Amounts = new Dictionary<string, double>();
    }

    public interface ICostSource
    {
        PartCost Cost(Node part);
        double UnitCost(string resource);
    }

    public sealed class QuoteLine
    {
        public string Label;
        public OwnerKind Kind;
        public string ShipId;
        public int Parts;
        public double Price;
        public double Fuel;
        public int MaxFlights;
        public bool RefitDone;
    }

    /// <summary>What a build costs and how long it takes, piece by piece.</summary>
    public sealed class Quote
    {
        public readonly List<QuoteLine> Lines = new List<QuoteLine>();
        public double NewPrice, RefitPrice, FuelPrice;
        public double BuildPrice => NewPrice + RefitPrice;
        public double Total => BuildPrice + FuelPrice;
        public double Seconds;
        public int PartCount;
        public readonly HashSet<string> HangarShips = new HashSet<string>();
        public readonly List<string> BinSerials = new List<string>();
        public readonly List<string> Serials = new List<string>();
        /// <summary>New parts in the craft that the build takes from the parts bin instead: craft serial to bin part.</summary>
        public readonly Dictionary<string, Owner> BinSwaps = new Dictionary<string, Owner>();
        public int NewParts => Lines.Where(l => l.Kind == OwnerKind.New).Sum(l => l.Parts);

        /// <summary>The craft as the build will make it: bin parts swapped in carry their own serial and flights.</summary>
        public Node WithBinParts(Node craft)
        {
            Node c = craft.Clone();
            foreach (Node part in c.Children("PART"))
            {
                Node mod = part.Children("MODULE").FirstOrDefault(m => m.Get("name") == Craft.ModuleName);
                string serial = mod?.Get("serial");
                if (serial == null || !BinSwaps.TryGetValue(serial, out Owner b)) continue;
                mod.Set("serial", b.BinSerial);
                mod.Set("flights", Math.Max(0, b.Flights).ToString(CultureInfo.InvariantCulture));
            }
            return c;
        }
    }

    public static class Quotes
    {
        /// <param name="craft">The ship to build, as a craft node with each part's PRSPart serial and flights.</param>
        /// <param name="ownerOf">Who owns a serial: the hangar, the parts bin, or nobody (a new part).</param>
        /// <param name="cameBack">Resources a reused part still holds from its last flight, by serial (or null).</param>
        /// <param name="binMatch">For a new part: a matching loose part from the bin to use instead, or null.</param>
        public static Quote Make(Node craft, string shipName, Func<string, Owner> ownerOf, ICostSource costs,
                                 Func<string, IDictionary<string, double>> cameBack, Settings s, Func<Node, Owner> binMatch = null)
        {
            var q = new Quote();
            var lines = new Dictionary<string, QuoteLine>();
            foreach (Node part in craft.Children("PART"))
            {
                q.PartCount++;
                Node mod = part.Children("MODULE").FirstOrDefault(m => m.Get("name") == Craft.ModuleName);
                string serial = mod?.Get("serial");
                int flights = int.TryParse(mod?.Get("flights"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int f) ? f : 0;
                Owner owner = (string.IsNullOrEmpty(serial) ? null : ownerOf(serial)) ?? Owner.NewPart;
                if (owner.Kind == OwnerKind.New && binMatch != null && !string.IsNullOrEmpty(serial))
                {
                    Owner b = binMatch(part);
                    if (b != null)
                    {
                        owner = b;
                        flights = Math.Max(0, b.Flights);
                        q.BinSwaps[serial] = b;
                    }
                }
                if (!string.IsNullOrEmpty(serial)) q.Serials.Add(serial);
                PartCost cost = costs.Cost(part) ?? new PartCost();

                double price;
                if (owner.Kind == OwnerKind.New) price = cost.Dry;
                else price = owner.RefitDone ? 0 : cost.Dry * Pricing.RefitShare(flights, s);

                // Fuel: a new part is filled from empty; a reused one only tops up what it came back with; a
                // ready ship is already full.
                double fuel = 0;
                IDictionary<string, double> left = owner.Kind == OwnerKind.Hangar && !owner.RefitDone ? cameBack(serial) : null;
                foreach (KeyValuePair<string, double> res in cost.Amounts)
                {
                    double have = owner.RefitDone ? res.Value : (left != null && left.TryGetValue(res.Key, out double l) ? l : 0);
                    fuel += Math.Max(0, res.Value - have) * costs.UnitCost(res.Key);
                }

                string key = owner.Kind == OwnerKind.New ? "new" : owner.Kind == OwnerKind.Bin ? "bin" : "ship:" + owner.ShipId;
                if (!lines.TryGetValue(key, out QuoteLine line))
                {
                    line = new QuoteLine
                    {
                        Kind = owner.Kind,
                        ShipId = owner.ShipId,
                        RefitDone = owner.RefitDone,
                        Label = owner.Kind == OwnerKind.New ? shipName : owner.Kind == OwnerKind.Bin ? "From the parts bin" : owner.Label,
                    };
                    lines[key] = line;
                }
                line.Parts++;
                line.Price += price;
                line.Fuel += fuel;
                line.MaxFlights = Math.Max(line.MaxFlights, flights);
                if (owner.Kind == OwnerKind.New) q.NewPrice += price; else q.RefitPrice += price;
                q.FuelPrice += fuel;
                if (owner.Kind == OwnerKind.Hangar) q.HangarShips.Add(owner.ShipId);
                if (owner.Kind == OwnerKind.Bin) q.BinSerials.Add(owner.BinSerial ?? serial);
            }
            // Reused pieces first (the flown booster), then the bin, then the new parts.
            q.Lines.AddRange(lines.Values.OrderBy(l => l.Kind == OwnerKind.Hangar ? 0 : l.Kind == OwnerKind.Bin ? 1 : 2));
            q.Seconds = Pricing.BuildSeconds(q.BuildPrice, s);
            return q;
        }
    }
}
