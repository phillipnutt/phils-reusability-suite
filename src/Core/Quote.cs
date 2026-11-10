using System;
using System.Collections.Generic;
using System.Linq;

namespace PhilsReusabilitySuite.Core
{
    public enum OwnerKind { New, Hangar, Bin }

    /// <summary>Where a part in a build comes from.</summary>
    public sealed class Owner
    {
        public OwnerKind Kind;
        /// <summary>For hangar hardware: the hangar ship it's part of (its id), its place in that ship's craft, the
        /// ship's name, its wear (one number per craft), whether it's fuelled, and what the part still held when it came home.</summary>
        public string ShipId;
        public int Index = -1;
        public string Label;
        public bool Ready;
        public double Wear;
        public IDictionary<string, double> Left;
        /// <summary>For hangar hardware: what was added to the part in the VAB (cargo, a bigger size), at full price.</summary>
        public double Added;
        /// <summary>For a part from the parts bin: its kind (part, Phil's Iterative Parts program and Mk, and price).
        /// Bin parts were refurbished as they went in, so they're like new parts that are already paid for.</summary>
        public string BinKey;
        /// <summary>
        /// A bin part that would fit, but its Phil's Iterative Parts program has moved on since it was built: it isn't
        /// used unless it's updated first (Phil: old-Mk parts never fly unless updated). The build asks.
        /// </summary>
        public bool Old;
        /// <summary>For an old bin part: how many Marks it's behind, what it is now and what it would become.</summary>
        public int Marks;
        public string Now, Current;
        /// <summary>What updating it costs (set by the quote).</summary>
        public double UpdatePrice;
        public static readonly Owner NewPart = new Owner { Kind = OwnerKind.New };
    }

    /// <summary>One part of a quote line, with what it costs.</summary>
    public sealed class QuoteItem
    {
        public Node Part;
        public double Price, Fuel;
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
        /// <summary>The hangar ship's wear (one craft's, so one number).</summary>
        public double Wear;
        public bool Ready;
        /// <summary>A hangar ship's parts the build leaves out: they go to the parts bin, refurbished with the rest
        /// (their price is in Price). Phil, round 4: bin parts are refurbished as they go in.</summary>
        public int LeftOut;
        public double LeftOutPrice;
        /// <summary>New parts: the design they were placed from (its Phil's Builds id), or "" for parts from no design.
        /// New parts are split by design: "Cabral I · 1 new part", "Shkhara 022 · 31 new parts".</summary>
        public string Design = "";
        public readonly List<QuoteItem> Items = new List<QuoteItem>();
    }

    /// <summary>What a build costs and how long it takes, piece by piece.</summary>
    public sealed class Quote
    {
        public readonly List<QuoteLine> Lines = new List<QuoteLine>();
        public double NewPrice, RefurbishPrice, FuelPrice;
        public double BuildPrice => NewPrice + RefurbishPrice;
        public double Total => BuildPrice + FuelPrice;
        public double Seconds;
        public int PartCount;
        /// <summary>Hangar ships whose hardware the build uses (by id).</summary>
        public readonly HashSet<string> HangarShips = new HashSet<string>();
        /// <summary>Which craft parts are hangar hardware: craft part to "hangarShipId:index".</summary>
        public readonly Dictionary<int, string> Owners = new Dictionary<int, string>();
        /// <summary>Parts the build takes from the parts bin, by kind.</summary>
        public readonly Dictionary<string, int> BinTaken = new Dictionary<string, int>();
        public int BinParts => BinTaken.Values.Sum();
        /// <summary>Bin parts that would fit if they were updated to their program's current Mk (built new until then).</summary>
        public readonly List<Owner> OldBin = new List<Owner>();
        public double UpdatePrice => OldBin.Sum(o => o.UpdatePrice);
        public int NewParts => Lines.Where(l => l.Kind == OwnerKind.New).Sum(l => l.Parts);
    }

    public static class Quotes
    {
        /// <param name="craft">The ship to build, as a craft node.</param>
        /// <param name="hardwareOf">For a part with a hardware note: the hangar ship it is (checked against the hangar), or null for a new part.</param>
        /// <param name="binMatch">For a new part: a matching part in the bin to use instead (counted), or null.</param>
        /// <param name="partsOf">A hangar ship's parts (by id), so the parts a build leaves out are priced too.</param>
        public static Quote Make(Node craft, string shipName, Func<Node, Owner> hardwareOf, ICostSource costs, Settings s,
                                 Func<Node, Owner> binMatch = null, Func<string, IEnumerable<Node>> partsOf = null)
        {
            var q = new Quote();
            var lines = new Dictionary<string, QuoteLine>();
            var used = new Dictionary<string, HashSet<int>>();
            var ships = new Dictionary<string, Owner>();
            int index = -1;
            foreach (Node part in craft.Children("PART"))
            {
                index++;
                q.PartCount++;
                Owner owner = Hardware.Of(part) != null ? hardwareOf(part) : null;
                // The same hardware twice (a copy that kept its note) is one part and a new one.
                if (owner != null && owner.Kind == OwnerKind.Hangar)
                {
                    if (!used.TryGetValue(owner.ShipId, out HashSet<int> set)) used[owner.ShipId] = set = new HashSet<int>();
                    if (!set.Add(owner.Index)) owner = null;
                }
                PartCost cost = costs.Cost(part) ?? new PartCost();
                if (owner == null && binMatch != null)
                {
                    Owner b = binMatch(part);
                    if (b != null && b.Old)
                    {
                        // Not used as it is: the build asks whether to update it first.
                        b.UpdatePrice = Pricing.PipUpdate(cost.Dry, b.Marks, s);
                        q.OldBin.Add(b);
                    }
                    else if (b != null) owner = b;
                }
                owner = owner ?? Owner.NewPart;

                // New parts cost their price. Hangar hardware costs its refurbish, by its craft's wear: any build
                // leaves the whole craft at 0%. Bin parts were refurbished as they went in, so they're already paid for.
                double added = owner.Kind == OwnerKind.Hangar ? Math.Min(cost.Dry, owner.Added) : 0;
                double price = owner.Kind == OwnerKind.New ? cost.Dry : owner.Kind == OwnerKind.Hangar ? (cost.Dry - added) * Pricing.RefurbishShare(owner.Wear, s) + added : 0;

                // Fuel: a hangar ship's part only tops up what it came home with (a fuelled one is full); every other
                // part is filled from empty (bin parts are emptied as they go in).
                double fuel = 0;
                foreach (KeyValuePair<string, double> res in cost.Amounts)
                {
                    double have = owner.Kind != OwnerKind.Hangar ? 0 : owner.Ready ? res.Value : owner.Left != null && owner.Left.TryGetValue(res.Key, out double l) ? l : 0;
                    fuel += Math.Max(0, res.Value - have) * costs.UnitCost(res.Key);
                }

                string design = owner.Kind == OwnerKind.New ? Hardware.Module(part)?.Get("design") ?? "" : "";
                string key = owner.Kind == OwnerKind.New ? "new:" + design : owner.Kind == OwnerKind.Bin ? "bin" : "ship:" + owner.ShipId;
                if (!lines.TryGetValue(key, out QuoteLine line))
                {
                    line = new QuoteLine
                    {
                        Kind = owner.Kind,
                        ShipId = owner.ShipId,
                        Ready = owner.Ready,
                        Wear = owner.Kind == OwnerKind.Hangar ? owner.Wear : 0,
                        Design = design,
                        Label = owner.Kind == OwnerKind.New ? shipName : owner.Kind == OwnerKind.Bin ? "From the parts bin" : owner.Label,
                    };
                    lines[key] = line;
                }
                line.Items.Add(new QuoteItem { Part = part, Price = price, Fuel = fuel });
                line.Parts++;
                line.Price += price;
                line.Fuel += fuel;
                if (owner.Kind == OwnerKind.New) q.NewPrice += price; else { q.RefurbishPrice += price - added; q.NewPrice += added; }
                q.FuelPrice += fuel;
                if (owner.Kind == OwnerKind.Hangar)
                {
                    q.HangarShips.Add(owner.ShipId);
                    q.Owners[index] = Hardware.Make(owner.ShipId, owner.Index);
                    ships[owner.ShipId] = owner;
                }
                if (owner.Kind == OwnerKind.Bin) q.BinTaken[owner.BinKey] = (q.BinTaken.TryGetValue(owner.BinKey, out int n) ? n : 0) + 1;
            }

            // A hangar ship's parts the build leaves out go to the parts bin, refurbished at that ship's wear like the rest.
            if (partsOf != null)
                foreach (KeyValuePair<string, Owner> kv in ships)
                {
                    QuoteLine line = lines["ship:" + kv.Key];
                    int i = -1;
                    foreach (Node p in partsOf(kv.Key) ?? new Node[0])
                    {
                        i++;
                        if (used[kv.Key].Contains(i)) continue;
                        double price = (costs.Cost(p) ?? new PartCost()).Dry * Pricing.RefurbishShare(kv.Value.Wear, s);
                        line.LeftOut++;
                        line.LeftOutPrice += price;
                        line.Price += price;
                        q.RefurbishPrice += price;
                    }
                }

            // Owned parts first, then the bin, then the new parts.
            q.Lines.AddRange(lines.Values.OrderBy(l => l.Kind == OwnerKind.Hangar ? 0 : l.Kind == OwnerKind.Bin ? 1 : 2));
            q.Seconds = Pricing.BuildSeconds(q.BuildPrice, s);
            return q;
        }
    }
}
