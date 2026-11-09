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
        RoundTwoChecks();
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
        Check(Pricing.RefurbishShare(0, s) == 0, "a craft with no wear costs nothing to refurbish");
        Check(Near(Pricing.RefurbishShare(0.2, s), 0.10, 1e-9), "20% wear: refurbishing costs 10% of the price (Phil's old refit)");
        Check(Near(Pricing.RefurbishShare(1, s), 0.5, 1e-9) && Near(Pricing.RefurbishShare(1.5, s), 0.5, 1e-9), "100% wear: half the price of new, never more");
        Check(Wear.OfFlight(0, 0, 1, s) == 0, "sitting still adds no wear");
        Check(Near(Wear.OfFlight(1250, 0.4, 2.5, s), 0.05, 1e-9), "half of 2,500 m/s adds 5%; heat under half its limit and G under 3 add nothing");
        Check(Near(Wear.OfFlight(2500, 0.75, 6.5, s), 0.10 + 0.15 + 0.10, 1e-9), "2,500 m/s 10%, three-quarters of the heat limit 15%, 6.5 G 10%");
        Check(Near(Wear.OfFlight(9000, 1.4, 25, s), 0.60, 1e-9), "each part of the rule stops at its top: 10% + 30% + 20%");
        Check(Wear.After(0.9, 0.3) == 1 && Wear.WornOut(1) && !Wear.WornOut(0.99) && Wear.WornOut(0.996), "wear stops at 100%, which forces a refurbish");
        Check(Wear.Percent(0.234) == "23%" && Wear.Percent(1.2) == "100%", "wear shows as a whole percent");
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

    static Node QuotePart(string serial, int flights, double dry, double fuel, double wear = 0)
    {
        var p = new Node("PART");
        p.Add("part", "tank_" + serial);
        p.Add("dry", dry.ToString(System.Globalization.CultureInfo.InvariantCulture));
        p.Add("fuel", fuel.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Node m = p.AddNode("MODULE");
        m.Add("name", Craft.ModuleName);
        m.Add("serial", serial);
        m.Add("flights", flights.ToString());
        if (wear > 0) m.Add("wear", wear.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return p;
    }

    // The mockup's Comsat II on Oak 005 #4, in round numbers.
    static void QuoteChecks()
    {
        var s = new Settings();
        var craft = new Node();
        craft.Nodes.Add(QuotePart("upper", 0, 16500, 100));     // new upper stage, 100 units of fuel
        craft.Nodes.Add(QuotePart("first", 1, 52000, 1000, 0.2));    // Oak 005 #4, flown once (20% worn), came back with 90 units
        craft.Nodes.Add(QuotePart("comsat", 0, 38400, 0));      // a ready Comsat II from the hangar
        craft.Nodes.Add(QuotePart("binned", 2, 1000, 0, 0.3));       // a battery from the parts bin, 30% worn
        Func<string, Owner> owner = sn =>
            sn == "first" ? new Owner { Kind = OwnerKind.Hangar, ShipId = "oak4", Label = "Oak 005 #4" } :
            sn == "comsat" ? new Owner { Kind = OwnerKind.Hangar, ShipId = "comsat", Label = "Comsat II", Ready = true } :
            sn == "binned" ? new Owner { Kind = OwnerKind.Bin } : null;
        Func<string, IDictionary<string, double>> left = sn => sn == "first" ? new Dictionary<string, double> { { "LiquidFuel", 90 } } : null;
        Quote q = Quotes.Make(craft, "Comsat II", owner, new FakeCosts(), left, s);
        Check(q.PartCount == 4 && q.Lines.Count == 4, "four parts in four lines (" + q.Lines.Count + ")");
        Check(Near(q.NewPrice, 16500, 0.01), "new parts cost their full price (" + q.NewPrice + ")");
        Check(Near(q.RefurbishPrice, 52000 * 0.2 * 0.5 + 1000 * 0.3 * 0.5, 0.01), "reused parts cost their refurbish by wear; the unworn ready payload costs nothing (" + q.RefurbishPrice + ")");
        Check(Near(q.Lines[0].Wear, 0.2, 1e-9) && q.Lines[1].Wear == 0, "each line knows its wear");
        Check(Near(q.FuelPrice, 100 * 0.8 + 910 * 0.8, 0.01), "fuel: new tanks filled, the flown stage topped up, the ready payload full (" + q.FuelPrice + ")");
        Check(Near(q.Seconds, Pricing.BuildSeconds(q.BuildPrice, s), 1e-6), "build time comes from new parts plus refurbishing");
        Check(q.Lines[0].Kind == OwnerKind.Hangar && q.Lines[2].Kind == OwnerKind.Bin && q.Lines[3].Kind == OwnerKind.New, "reused pieces first, then the bin, then new parts");
        Check(q.HangarShips.SetEquals(new[] { "oak4", "comsat" }) && q.BinSerials.SequenceEqual(new[] { "binned" }), "the build knows which hangar ships and bin parts it uses");

        // A new part with a matching loose part in the bin: the bin part is used instead, at its refurbish price.
        var craft2 = new Node();
        craft2.Nodes.Add(QuotePart("fresh", 0, 2000, 0));
        craft2.Nodes.Add(QuotePart("other", 0, 500, 0));
        Func<Node, Owner> match = p => p.Get("part") == "tank_fresh" ? new Owner { Kind = OwnerKind.Bin, BinSerial = "bin-7", Flights = 3, Wear = 0.4 } : null;
        Quote q2 = Quotes.Make(craft2, "Probe", sn => null, new FakeCosts(), sn => null, s, match);
        Check(Near(q2.RefurbishPrice, 2000 * 0.4 * 0.5, 0.01) && Near(q2.NewPrice, 500, 0.01), "a bin part swapped in costs its refurbish; the other part is new (" + q2.RefurbishPrice + ", " + q2.NewPrice + ")");
        Check(q2.BinSerials.SequenceEqual(new[] { "bin-7" }) && q2.BinSwaps.ContainsKey("fresh"), "the build knows which bin part it takes");
        Node built = q2.WithBinParts(craft2);
        Check(Wear.In(built.Children("PART").First().Children("MODULE").First()) == 0.4, "the built craft carries the bin part's wear");
        Check(Craft.SerialOf(built.Children("PART").First()) == "bin-7" && Craft.SerialOf(built.Children("PART").Last()) == "other", "the built craft carries the bin part's own serial");
        Check(Craft.SerialOf(craft2.Children("PART").First()) == "fresh", "the editor's craft is left as it was");
    }

    static Node PipModule(Node part, int mk)
    {
        Node m = part.AddNode("MODULE");
        m.Add("name", "ModulePipPart");
        m.Add("programId", "augur");
        m.Add("mk", mk.ToString());
        return m;
    }

    static string MkOf(Node part) => part.Children("MODULE").FirstOrDefault(x => x.Get("name") == "ModulePipPart")?.Get("mk");

    // Phil's second play-test of 2026-11-08: Phil's Iterative Parts Marks, bin fuel, new parts split by design.
    static void RoundTwoChecks()
    {
        var s = new Settings();
        Check(Near(Pricing.PipUpdate(1000, 2, s), 200, 1e-9) && Pricing.PipUpdate(1000, 0, s) == 0, "updating a PIP part costs 10% of its price per Mk gained");
        Check(Settings.FromNode(Node.Parse("pipUpdatePerMk = 0")).PipUpdatePerMk == 0, "the update can be made free");

        // An old-Mk bin part that would fit isn't used as it is: the build asks, and the part counts as new till then.
        var craft = new Node();
        craft.Nodes.Add(QuotePart("fresh", 0, 2000, 0));
        craft.Nodes.Add(QuotePart("other", 0, 500, 0));
        Func<Node, Owner> oldMatch = p => p.Get("part") == "tank_fresh" ? new Owner { Kind = OwnerKind.Bin, Old = true, BinSerial = "bin-old", Marks = 2, Now = "Augur I Mk. 1", Current = "Augur I Mk. 3" } : null;
        Quote q = Quotes.Make(craft, "Probe", sn => null, new FakeCosts(), sn => null, s, oldMatch);
        Check(q.OldBin.Count == 1 && q.BinSwaps.Count == 0 && q.BinSerials.Count == 0, "an old-Mk bin part is offered, not used");
        Check(Near(q.NewPrice, 2500, 0.01) && Near(q.UpdatePrice, 400, 0.01), "until it's updated it's built new; the update is priced (" + q.UpdatePrice + ")");

        // A bin part keeps what it came back with: a build only tops it up (Gallant I #6 paid for a whole load).
        var craft2 = new Node();
        craft2.Nodes.Add(QuotePart("pod", 0, 600, 100));
        Func<Node, Owner> binMatch = p => new Owner { Kind = OwnerKind.Bin, BinSerial = "bin-pod", Flights = 1 };
        Quote q2 = Quotes.Make(craft2, "Pod", sn => null, new FakeCosts(), sn => sn == "bin-pod" ? new Dictionary<string, double> { { "LiquidFuel", 60 } } : null, s, binMatch);
        Check(Near(q2.FuelPrice, 40 * 0.8, 0.01), "a bin part that came back with 60 of its 100 units pays for 40 (" + q2.FuelPrice + ")");
        Quote q2b = Quotes.Make(craft2, "Pod", sn => null, new FakeCosts(), sn => null, s, binMatch);
        Check(Near(q2b.FuelPrice, 100 * 0.8, 0.01), "a bin part with nothing known left is filled from empty");

        // New parts split by design: "Cabral I · 1 new part", "Shkhara 022 · 31 new parts".
        var craft3 = new Node();
        foreach (string[] d in new[] { new[] { "w", "cabral" }, new[] { "l1", "shkhara" }, new[] { "l2", "shkhara" } })
        {
            Node p = QuotePart(d[0], 0, 100, 0);
            p.Child("MODULE").Add("design", d[1]);
            craft3.Nodes.Add(p);
        }
        Quote q3 = Quotes.Make(craft3, "Cabral I", sn => null, new FakeCosts(), sn => null, s);
        Check(q3.Lines.Count == 2 && q3.Lines[0].Design == "cabral" && q3.Lines[0].Parts == 1 && q3.Lines[1].Design == "shkhara" && q3.Lines[1].Parts == 2,
              "new parts are one line per design, the top design first");
        Check(q3.NewParts == 3 && q3.Lines.Sum(l => l.Items.Count) == 3, "the lines still add up to every new part");

        // Flown hardware keeps its own Mk; everything else about it comes from the design.
        var design = new Node();
        var flown = new List<Node>();
        for (int i = 0; i < 2; i++)
        {
            Node dp = new Node("PART");
            dp.Add("part", "probe_" + (100 + i));
            Node pm = dp.AddNode("MODULE"); pm.Add("name", Craft.ModuleName); pm.Add("design", "D1"); pm.Add("slot", i.ToString()); pm.Add("serial", "");
            PipModule(dp, 1);
            Node dec = dp.AddNode("MODULE"); dec.Add("name", "ModuleDecouple"); dec.Add("isDecoupled", "False");
            design.Nodes.Add(dp);
        }
        Node fp = new Node("PART");
        fp.Add("part", "probe_555");
        Node fm = fp.AddNode("MODULE"); fm.Add("name", Craft.ModuleName); fm.Add("design", "D1"); fm.Add("slot", "0"); fm.Add("serial", "flown-0"); fm.Add("flights", "1");
        PipModule(fp, 2);
        Node fdec = fp.AddNode("MODULE"); fdec.Add("name", "ModuleDecouple"); fdec.Add("isDecoupled", "True");
        flown.Add(fp);
        CompletionResult r = Completion.Complete(design, "D1", flown);
        Node slot0 = r.Craft.Children("PART").First(), slot1 = r.Craft.Children("PART").Last();
        Check(MkOf(slot0) == "2", "Restore keeps the flown part's own Mk (play-test #6), not the design file's (" + MkOf(slot0) + ")");
        Check(slot0.Children("MODULE").First(x => x.Get("name") == "ModuleDecouple").Get("isDecoupled") == "False", "the rest of the part comes from the design: its decoupler isn't fired");
        Check(MkOf(slot1) == "1" && r.NewSlots.SequenceEqual(new[] { 1 }), "the new slot keeps the design's PIP data, for the current-Mk step");

        // A bin part put into a build brings its own Mk too.
        var craft4 = new Node();
        Node np = QuotePart("newone", 0, 700, 0);
        PipModule(np, 1);
        craft4.Nodes.Add(np);
        Node binNode = QuotePart("bin-9", 2, 700, 0);
        PipModule(binNode, 3);
        Quote q4 = Quotes.Make(craft4, "Probe", sn => null, new FakeCosts(), sn => null, s, p => new Owner { Kind = OwnerKind.Bin, BinSerial = "bin-9", Flights = 2, BinPart = binNode });
        Check(MkOf(q4.WithBinParts(craft4).Children("PART").First()) == "3", "the built craft carries the bin part's own PIP Mk");
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

    // A stock craft as a "design": a piece of it came home (a first stage), and completing it to the design
    // gives the whole craft back, with the flown parts in their own slots and every other slot new.
    static void CompletionChecks(string name, Node craft, Craft c, CraftPart realRoot)
    {
        var design = craft.Clone();
        int slot = 0;
        foreach (Node p in design.Children("PART"))
        {
            Node m = p.Children("MODULE").First(x => x.Get("name") == Craft.ModuleName);
            m.Set("serial", "");      // a design file carries no hardware
            m.Set("design", "D1");
            m.Set("slot", (slot++).ToString());
        }
        // The flown piece: the subtree under some part, as it was built from the design (stamped slots).
        CraftPart top = c.Parts.Where(p => p != realRoot && !p.IsCompound).OrderByDescending(p => Subtree(p).Count).FirstOrDefault();
        if (top == null) return;
        var flown = new List<Node>();
        foreach (CraftPart p in Subtree(top))
        {
            Node n = p.Node.Clone();
            Node m = n.Children("MODULE").First(x => x.Get("name") == Craft.ModuleName);
            m.Set("design", "D1");
            m.Set("slot", p.Index.ToString());
            m.Set("flights", "3");
            m.Set("hull", "Oak 005|4");
            flown.Add(n);
        }
        CompletionResult r = Completion.Complete(design, "D1", flown);
        string label = name + ": completing a flown piece";
        Check(r.Craft.Children("PART").Count() == c.Parts.Count, label + " gives the whole design back");
        Check(r.Used.Count == flown.Count && r.NewSlots.Count == c.Parts.Count - flown.Count, label + ": " + r.Used.Count + " flown parts, " + r.NewSlots.Count + " new");
        List<Node> parts = r.Craft.Children("PART").ToList();
        bool slotsRight = Subtree(top).All(p => Craft.SerialOf(parts[p.Index]) == p.Serial);
        Check(slotsRight, label + ": each flown part sits in its own slot");
        Check(r.NewSlots.All(i => Craft.SerialOf(parts[i]) == ""), label + ": new slots carry no serial");
        Check(parts.Where((p, i) => !r.NewSlots.Contains(i)).All(p => p.Children("MODULE").First(x => x.Get("name") == Craft.ModuleName).Get("hull") == "Oak 005|4"), label + ": flown parts keep their ship");
        Check(Pieces.Validate(r.Craft).Count == 0, label + ": the completed craft is valid");
        // A different design's parts don't fill this one.
        Check(Completion.Complete(design, "D2", flown).Used.Count == 0, label + ": parts from another design aren't used");
        // The design was saved over and a slot now holds another part: that flown part doesn't fit it.
        Node changed = design.Clone();
        Node firstFlownSlot = changed.Children("PART").ElementAt(top.Index);
        firstFlownSlot.Set("part", "someOtherPart_123");
        CompletionResult rc = Completion.Complete(changed, "D1", flown);
        Check(rc.Used.Count == flown.Count - 1 && rc.NewSlots.Contains(top.Index), label + ": a slot that changed part in the design is built new");
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

        CompletionChecks(name, craft, c, realRoot);

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

            // A leaf didn't come home: it was lost, unless it parted at a separator (a decoupler, docking port, claw
            // or launch clamp), which is how parts leave on purpose. One the landing broke off is lost either way.
            CraftPart leaf = sub.Where(p => p != top && p.Children.Count == 0 && !p.IsCompound).OrderBy(_ => rng.Next()).FirstOrDefault();
            if (leaf != null)
            {
                var home = sub.Where(p => p != leaf).Select(p => p.Serial).ToList();
                PieceResult b = Pieces.Build(craft, home);
                if (Pieces.IsSeparator(leaf) || Pieces.IsSeparator(leaf.Parent))
                    Check(b.Missing.Count == 0, label + ": " + leaf.Ref + " parted at a separator, so it isn't missing");
                else
                    Check(b.Missing.Count == 1 && b.Missing[0] == leaf.Serial, label + ": lost " + leaf.Ref + " is missing");
                Check(Pieces.Validate(b.Craft).Count == 0, label + ": valid without the lost part");
                PieceResult d = Pieces.Build(craft, home, new[] { leaf.Serial });
                Check(d.Missing.Count == 1 && d.Missing[0] == leaf.Serial, label + ": a part the landing broke off is missing");
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
