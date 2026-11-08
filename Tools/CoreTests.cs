// Checks for the KSP-free craft logic in src/Core. Run with Tools/Test-Core.ps1.
// It reads the stock craft that ship with KSP (Ships/VAB and Ships/SPH of a KSP install), gives every part a
// serial number, then pretends pieces came home and checks the rebuilt craft.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhilsReusabilitySuite.Core;

static class CoreTests
{
    static int checks, failures;

    static void Check(bool ok, string what)
    {
        checks++;
        if (ok) return;
        failures++;
        if (failures <= 40) Console.WriteLine("  FAIL: " + what);
    }

    static int Main(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("Give the folder of a KSP install (for its stock craft)."); return 2; }
        string ksp = args[0];
        var files = new List<string>();
        foreach (string sub in new[] { @"Ships\VAB", @"Ships\SPH" })
        {
            string dir = Path.Combine(ksp, sub);
            if (Directory.Exists(dir)) files.AddRange(Directory.GetFiles(dir, "*.craft"));
        }
        if (files.Count == 0) { Console.WriteLine("No stock craft found under " + ksp); return 2; }

        NodeBasics();
        RefParsing();
        PricingChecks();
        QuoteChecks();
        int craftCount = 0, pieces = 0;
        foreach (string f in files.OrderBy(x => x))
        {
            Node craft = Node.Parse(File.ReadAllText(f));
            if (!craft.Children("PART").Any()) continue;
            craftCount++;
            pieces += CraftChecks(Path.GetFileNameWithoutExtension(f), craft);
        }
        Console.WriteLine($"{craftCount} stock craft, {pieces} pieces tried, {checks} checks, {failures} failed");
        return failures == 0 ? 0 : 1;
    }

    static void NodeBasics()
    {
        Node n = Node.Parse("a = 1\nb = x = y // comment\nPART\n{\n\tpart = p_1\n\tMODULE\n\t{\n\t\tname = M\n\t}\n}\nEMPTY {}\n");
        Check(n.Get("a") == "1", "parse simple value");
        Check(n.Get("b") == "x = y", "value keeps '=' and drops the comment");
        Check(n.Child("PART")?.Child("MODULE")?.Get("name") == "M", "nested nodes");
        Check(n.Child("EMPTY") != null, "inline empty node");
        Node again = Node.Parse(n.WriteContents());
        Check(again.WriteContents() == n.WriteContents(), "write then parse gives the same text");
    }

    static void RefParsing()
    {
        Check(Craft.RefIn("bottom,HeatShield2_4294294368_0|-0.47924|0", out string h, out string t) == "HeatShield2_4294294368" && h == "bottom" && t == "_0|-0.47924|0", "attN ref with 1.6 coords");
        Check(Craft.RefIn("top,Null_0_0|0.9|0_0|1|0", out h, out t) == "Null_0" && t == "_0|0.9|0_0|1|0", "attN Null ref");
        Check(Craft.RefIn("srfAttach,Rockomax16.BW_4294219330", out h, out t) == "Rockomax16.BW_4294219330" && t == "", "srfN ref");
    }

    static bool Near(double a, double b, double tol) => Math.Abs(a - b) <= tol;

    // The agreed numbers from DESIGN.md.
    static void PricingChecks()
    {
        var s = new Settings();
        double day = s.DaySeconds;
        Check(Near(Pricing.BuildSeconds(20000, s) / day, 1.0, 1e-9), "20k of work takes 1 day");
        Check(Near(Pricing.BuildSeconds(300685, s) / day, 8.7, 0.05), "DESU - Full (301k) takes about 8.7 days");
        Check(Near(Pricing.BuildSeconds(47199, s) / day, 2.0, 0.05), "Comsat I (47k) takes about 2.0 days");
        Check(Near(Pricing.BuildSeconds(1000000, s) / day, 22.9, 0.05), "a 1M ship takes about 22.9 days");
        Check(Pricing.BuildSeconds(0, s) == 0, "no work takes no time");
        Check(Pricing.RefitShare(0, s) == 0, "a part that has never flown needs no refit");
        Check(Near(Pricing.RefitShare(1, s), 0.11, 1e-9), "refit for a part with 1 flight is 11%");
        Check(Near(Pricing.RefitShare(4, s), 0.14, 1e-9), "refit for its 5th flight is 14%");
        Check(Near(Pricing.RefitShare(19, s), 0.29, 1e-9), "refit for its 20th flight is 29%");
        Check(Near(Pricing.OvertimeCharge(22220, 1, s), 5555, 0.01), "overtime on a fresh 22,220 build is +5,555");
        Check(Near(Pricing.OvertimeCharge(22220, 0.38, s), 2110.9, 0.1), "overtime with 38% left is 25% of that share");
        Check(Near(Pricing.OutsourceCharge(22220, 0.38, s), 8443.6, 0.1), "outsourcing with 38% left pays that share again");
        Check(Pricing.Duration(1.088 * day, s) == "1.1 days", "duration text in days (" + Pricing.Duration(1.088 * day, s) + ")");
        Check(Pricing.Duration(1.36 * 3600, s) == "1.4 hours", "duration text in hours (" + Pricing.Duration(1.36 * 3600, s) + ")");
        Settings t = Settings.FromNode(Node.Parse("buildBase = 10000\novertimeCost = 0.5\nbogus = 1\nlinesPerLevel = 2"));
        Check(t.BuildBase == 10000 && t.OvertimeCost == 0.5 && t.LinesPerLevel == 2 && t.BuildExponent == 0.8, "settings read from a config node, defaults kept");
    }

    sealed class FakeCosts : ICostSource
    {
        public PartCost Cost(Node part)
        {
            var c = new PartCost { Dry = double.Parse(part.Get("dry"), System.Globalization.CultureInfo.InvariantCulture) };
            string fuel = part.Get("fuel");
            if (fuel != null) c.Amounts["LiquidFuel"] = double.Parse(fuel, System.Globalization.CultureInfo.InvariantCulture);
            return c;
        }
        public double UnitCost(string resource) => resource == "LiquidFuel" ? 0.8 : 0;
    }

    static Node QuotePart(string serial, int flights, double dry, double fuel)
    {
        var p = new Node("PART");
        p.Add("part", "tank_" + serial);
        p.Add("dry", dry.ToString(System.Globalization.CultureInfo.InvariantCulture));
        p.Add("fuel", fuel.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Node m = p.AddNode("MODULE");
        m.Add("name", Craft.ModuleName);
        m.Add("serial", serial);
        m.Add("flights", flights.ToString());
        return p;
    }

    // The mockup's Comsat II on Oak 005 #4, in round numbers.
    static void QuoteChecks()
    {
        var s = new Settings();
        var craft = new Node();
        craft.Nodes.Add(QuotePart("upper", 0, 16500, 100));     // new upper stage, 100 units of fuel
        craft.Nodes.Add(QuotePart("first", 1, 52000, 1000));    // Oak 005 #4, flown once, came back with 90 units
        craft.Nodes.Add(QuotePart("comsat", 0, 38400, 0));      // a ready Comsat II from the hangar
        craft.Nodes.Add(QuotePart("binned", 2, 1000, 0));       // a battery from the parts bin
        Func<string, Owner> owner = sn =>
            sn == "first" ? new Owner { Kind = OwnerKind.Hangar, ShipId = "oak4", Label = "Oak 005 #4" } :
            sn == "comsat" ? new Owner { Kind = OwnerKind.Hangar, ShipId = "comsat", Label = "Comsat II", RefitDone = true } :
            sn == "binned" ? new Owner { Kind = OwnerKind.Bin } : null;
        Func<string, IDictionary<string, double>> left = sn => sn == "first" ? new Dictionary<string, double> { { "LiquidFuel", 90 } } : null;
        Quote q = Quotes.Make(craft, "Comsat II", owner, new FakeCosts(), left, s);
        Check(q.PartCount == 4 && q.Lines.Count == 4, "four parts in four lines (" + q.Lines.Count + ")");
        Check(Near(q.NewPrice, 16500, 0.01), "new parts cost their full price (" + q.NewPrice + ")");
        Check(Near(q.RefitPrice, 52000 * 0.11 + 1000 * 0.12, 0.01), "reused parts cost their refit; the ready payload costs nothing (" + q.RefitPrice + ")");
        Check(Near(q.FuelPrice, 100 * 0.8 + 910 * 0.8, 0.01), "fuel: new tanks filled, the flown stage topped up, the ready payload full (" + q.FuelPrice + ")");
        Check(Near(q.Seconds, Pricing.BuildSeconds(q.BuildPrice, s), 1e-6), "build time comes from new parts plus refit");
        Check(q.Lines[0].Kind == OwnerKind.Hangar && q.Lines[2].Kind == OwnerKind.Bin && q.Lines[3].Kind == OwnerKind.New, "reused pieces first, then the bin, then new parts");
        Check(q.HangarShips.SetEquals(new[] { "oak4", "comsat" }) && q.BinSerials.SequenceEqual(new[] { "binned" }), "the build knows which hangar ships and bin parts it uses");

        // A new part with a matching loose part in the bin: the bin part is used instead, at refit price.
        var craft2 = new Node();
        craft2.Nodes.Add(QuotePart("fresh", 0, 2000, 0));
        craft2.Nodes.Add(QuotePart("other", 0, 500, 0));
        Func<Node, Owner> match = p => p.Get("part") == "tank_fresh" ? new Owner { Kind = OwnerKind.Bin, BinSerial = "bin-7", Flights = 3 } : null;
        Quote q2 = Quotes.Make(craft2, "Probe", sn => null, new FakeCosts(), sn => null, s, match);
        Check(Near(q2.RefitPrice, 2000 * 0.13, 0.01) && Near(q2.NewPrice, 500, 0.01), "a bin part swapped in costs its refit; the other part is new (" + q2.RefitPrice + ", " + q2.NewPrice + ")");
        Check(q2.BinSerials.SequenceEqual(new[] { "bin-7" }) && q2.BinSwaps.ContainsKey("fresh"), "the build knows which bin part it takes");
        Node built = q2.WithBinParts(craft2);
        Check(Craft.SerialOf(built.Children("PART").First()) == "bin-7" && Craft.SerialOf(built.Children("PART").Last()) == "other", "the built craft carries the bin part's own serial");
        Check(Craft.SerialOf(craft2.Children("PART").First()) == "fresh", "the editor's craft is left as it was");
    }

    static void AddSerials(Node craft)
    {
        int i = 0;
        foreach (Node p in craft.Children("PART"))
        {
            Node m = p.AddNode("MODULE");
            m.Add("name", Craft.ModuleName);
            m.Add("serial", "s" + (i++));
            m.Add("flights", "1");
        }
    }

    static List<CraftPart> Subtree(CraftPart p)
    {
        var list = new List<CraftPart> { p };
        foreach (CraftPart c in p.Children) list.AddRange(Subtree(c));
        return list;
    }

    static int CraftChecks(string name, Node craft)
    {
        AddSerials(craft);
        Craft c = Craft.Read(craft);
        List<string> orig = Pieces.Validate(craft);
        Check(orig.Count == 0, name + ": stock craft is valid (" + string.Join("; ", orig.Take(3)) + ")");
        if (orig.Count > 0) return 0;

        // Everything came home.
        CraftPart realRoot = c.Parts.First(p => p.Parent == null);
        PieceResult all = Pieces.Build(craft, c.Parts.Select(p => p.Serial).ToList(), null);
        Check(all.Placed.Count == c.Parts.Count, name + ": whole craft placed (" + all.Placed.Count + " of " + c.Parts.Count + ")");
        Check(all.RootSerial == realRoot.Serial, name + ": whole craft keeps its root");
        Check(Craft.SerialOf(all.Craft.Children("PART").First()) == realRoot.Serial, name + ": root is written first");
        Check(Pieces.Validate(all.Craft).Count == 0, name + ": whole craft rebuilt valid");
        Check(Node.Parse(all.Craft.WriteContents()).Children("PART").Count() == c.Parts.Count, name + ": whole craft survives write and parse");

        var rng = new Random(name.GetHashCode() & 0x7fffffff);
        int tried = 1;
        var candidates = c.Parts.Where(p => p != realRoot && !p.IsCompound).ToList();
        foreach (CraftPart top in candidates.OrderBy(_ => rng.Next()).Take(12))
        {
            tried++;
            List<CraftPart> sub = Subtree(top);
            var subIds = new HashSet<string>(sub.Select(p => p.Id));
            int dropped = sub.Count(p => p.IsCompound && p != top && p.Children.Count == 0 && p.TargetId != null && !subIds.Contains(p.TargetId));
            string label = name + ": piece under " + top.Ref;

            // The piece came home whole (it was decoupled from the rest).
            PieceResult r = Pieces.Build(craft, sub.Select(p => p.Serial).ToList(), new string[0]);
            Check(r.RootSerial == top.Serial, label + ": new root is the piece's top part");
            Check(r.Placed.Count == sub.Count - dropped, label + ": placed " + r.Placed.Count + ", expected " + (sub.Count - dropped));
            Check(r.Dropped.Count == dropped, label + ": dropped struts/fuel lines " + r.Dropped.Count + ", expected " + dropped);
            Check(r.Unplaced.Count == 0 && r.Missing.Count == 0, label + ": nothing unplaced or missing");
            List<string> probs = Pieces.Validate(r.Craft);
            Check(probs.Count == 0, label + ": rebuilt craft valid (" + string.Join("; ", probs.Take(3)) + ")");
            Node reparsed = Node.Parse(r.Craft.WriteContents());
            Check(Pieces.Validate(reparsed).Count == 0, label + ": valid after write and parse");

            // A leaf broke off in flight: it's missing. If it left by decoupling instead, it isn't.
            CraftPart leaf = sub.Where(p => p != top && p.Children.Count == 0 && !p.IsCompound).OrderBy(_ => rng.Next()).FirstOrDefault();
            if (leaf != null)
            {
                var home = sub.Where(p => p != leaf).Select(p => p.Serial).ToList();
                PieceResult b = Pieces.Build(craft, home, new[] { leaf.Serial });
                Check(b.Missing.Count == 1 && b.Missing[0] == leaf.Serial, label + ": broken-off " + leaf.Ref + " is missing");
                Check(Pieces.Validate(b.Craft).Count == 0, label + ": valid without the broken-off part");
                PieceResult d = Pieces.Build(craft, home, new string[0]);
                Check(d.Missing.Count == 0, label + ": a part that left by decoupling isn't missing");
            }

            // Strangers came home attached (docked or grabbed in flight): they can't be placed.
            if (sub.Count >= 4)
            {
                var outside = c.Parts.Where(p => !sub.Contains(p) && p.Parent != null && !sub.Contains(p.Parent) && p.Children.Count == 0).OrderBy(_ => rng.Next()).Take(2).ToList();
                var home = sub.Select(p => p.Serial).Concat(outside.Select(p => p.Serial)).Concat(new[] { "unknown-serial" }).ToList();
                PieceResult x = Pieces.Build(craft, home, new string[0]);
                Check(x.RootSerial == top.Serial, label + ": strangers don't change the root");
                Check(x.Unplaced.Count == outside.Count + 1, label + ": strangers unplaced " + x.Unplaced.Count + ", expected " + (outside.Count + 1));
            }
        }
        return tried;
    }
}
