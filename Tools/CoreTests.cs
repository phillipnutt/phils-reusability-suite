// Checks for the KSP-free craft logic in src/Core. Run with Tools/Test-Core.ps1.
// It reads the stock craft that ship with KSP (Ships/VAB and Ships/SPH of a KSP install), names every part by its
// place, then pretends pieces came home and checks the rebuilt craft.
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
        RoundFourChecks();
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
            var c = new PartCost { Dry = double.Parse(part.Get("dry") ?? "0", System.Globalization.CultureInfo.InvariantCulture) };
            string fuel = part.Get("fuel");
            if (fuel != null) c.Amounts["LiquidFuel"] = double.Parse(fuel, System.Globalization.CultureInfo.InvariantCulture);
            return c;
        }
        public double UnitCost(string resource) => resource == "LiquidFuel" ? 0.8 : 0;
    }

    static int ids;

    /// <summary>A part node for quotes: a name, a dry price, fuel, and a hardware note if it's a stored ship's part.</summary>
    static Node QuotePart(string name, double dry, double fuel, string note = null, string design = null, string link = null)
    {
        var p = new Node("PART");
        p.Add("part", name + "_" + (1000 + ids++));
        if (link != null) p.Add("link", link);
        p.Add("dry", dry.ToString(System.Globalization.CultureInfo.InvariantCulture));
        p.Add("fuel", fuel.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Node m = p.AddNode("MODULE");
        m.Add("name", Craft.ModuleName);
        if (design != null) m.Add("design", design);
        if (note != null) m.Add(Hardware.Field, note);
        return p;
    }

    /// <summary>A pretend hangar: each ship's parts, wear, whether it's fuelled, and what parts came back with.</summary>
    sealed class FakeHangar
    {
        public readonly Dictionary<string, List<Node>> Parts = new Dictionary<string, List<Node>>();
        public readonly Dictionary<string, double> Wear = new Dictionary<string, double>();
        public readonly HashSet<string> Ready = new HashSet<string>();
        public readonly Dictionary<string, IDictionary<string, double>> Left = new Dictionary<string, IDictionary<string, double>>();

        public Owner OwnerOf(Node part)
        {
            if (!Hardware.Parse(Hardware.Of(part), out string ship, out int at) || !Parts.TryGetValue(ship, out List<Node> list) || at >= list.Count) return null;
            if (Hardware.PartName(list[at]) != Hardware.PartName(part)) return null;
            Left.TryGetValue(ship + ":" + at, out IDictionary<string, double> left);
            return new Owner { Kind = OwnerKind.Hangar, ShipId = ship, Index = at, Label = ship + " #1", Wear = Wear.TryGetValue(ship, out double w) ? w : 0, Ready = Ready.Contains(ship), Left = left };
        }

        public IEnumerable<Node> PartsOf(string ship) => Parts.TryGetValue(ship, out List<Node> l) ? l : null;
    }

    // The mockup's Comsat II on Oak 005 #4, in round numbers.
    static void QuoteChecks()
    {
        var s = new Settings();
        var hangar = new FakeHangar();
        hangar.Parts["oak"] = new List<Node> { QuotePart("first", 52000, 1000), QuotePart("fin", 400, 0) };
        hangar.Wear["oak"] = 0.2;
        hangar.Left["oak:0"] = new Dictionary<string, double> { { "LiquidFuel", 90 } };
        hangar.Parts["comsat"] = new List<Node> { QuotePart("comsat", 38400, 0) };
        hangar.Ready.Add("comsat");
        var craft = new Node();
        craft.Nodes.Add(QuotePart("upper", 16500, 100));                       // new upper stage, 100 units of fuel
        craft.Nodes.Add(QuotePart("first", 52000, 1000, "oak:0"));            // Oak 005 #4, 20% worn, came back with 90 units
        craft.Nodes.Add(QuotePart("comsat", 38400, 0, "comsat:0"));           // a ready Comsat II from the hangar
        craft.Nodes.Add(QuotePart("battery", 1000, 50));                      // a battery the bin has one of
        int binHas = 1;
        Func<Node, Owner> bin = p => Hardware.PartName(p) == "battery" && binHas-- > 0 ? new Owner { Kind = OwnerKind.Bin, BinKey = "battery|1000" } : null;
        Quote q = Quotes.Make(craft, "Comsat II", hangar.OwnerOf, new FakeCosts(), s, bin, hangar.PartsOf);
        Check(q.PartCount == 4 && q.Lines.Count == 4, "four parts in four lines (" + q.Lines.Count + ")");
        Check(Near(q.NewPrice, 16500, 0.01), "new parts cost their full price (" + q.NewPrice + ")");
        Check(Near(q.RefurbishPrice, (52000 + 400) * 0.2 * 0.5, 0.01), "hangar parts cost their refurbish by their ship's wear, the fin it leaves out too; the unworn ready payload and the bin battery cost nothing (" + q.RefurbishPrice + ")");
        QuoteLine oak = q.Lines.First(l => l.ShipId == "oak");
        Check(oak.LeftOut == 1 && Near(oak.LeftOutPrice, 400 * 0.2 * 0.5, 0.01), "the part Oak 005 #4 leaves out goes to the bin, refurbished with the rest");
        Check(Near(q.FuelPrice, 100 * 0.8 + 910 * 0.8 + 50 * 0.8, 0.01), "fuel: new tanks filled, the flown stage topped up, the ready payload full, the bin battery filled from empty (" + q.FuelPrice + ")");
        Check(Near(q.Seconds, Pricing.BuildSeconds(q.BuildPrice, s), 1e-6), "build time comes from new parts plus refurbishing");
        Check(q.Lines[0].Kind == OwnerKind.Hangar && q.Lines[2].Kind == OwnerKind.Bin && q.Lines[3].Kind == OwnerKind.New, "hangar ships first, then the bin, then new parts");
        Check(q.HangarShips.SetEquals(new[] { "oak", "comsat" }) && q.BinTaken.Count == 1 && q.BinTaken["battery|1000"] == 1, "the build knows which hangar ships it uses and what it takes from the bin");
        Check(q.Owners.Count == 2 && q.Owners[1] == "oak:0" && q.Owners[2] == "comsat:0", "the build knows which of its parts are which ship's hardware");
        Check(q.Lines.First(l => l.Kind == OwnerKind.Bin).Price == 0, "bin parts are already paid for");
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

    // Phil's second play-test of 2026-11-08: Phil's Iterative Parts Marks, new parts split by design, hardware's own Mk.
    static void RoundTwoChecks()
    {
        var s = new Settings();
        Check(Near(Pricing.PipUpdate(1000, 2, s), 200, 1e-9) && Pricing.PipUpdate(1000, 0, s) == 0, "updating a PIP part costs 10% of its price per Mk gained");
        Check(Settings.FromNode(Node.Parse("pipUpdatePerMk = 0")).PipUpdatePerMk == 0, "the update can be made free");

        // An old-Mk bin part that would fit isn't used as it is: the build asks, and the part counts as new till then.
        var craft = new Node();
        craft.Nodes.Add(QuotePart("fresh", 2000, 0));
        craft.Nodes.Add(QuotePart("other", 500, 0));
        Func<Node, Owner> oldMatch = p => Hardware.PartName(p) == "fresh" ? new Owner { Kind = OwnerKind.Bin, Old = true, BinKey = "fresh-mk1", Marks = 2, Now = "Augur I Mk. 1", Current = "Augur I Mk. 3" } : null;
        Quote q = Quotes.Make(craft, "Probe", p => null, new FakeCosts(), s, oldMatch);
        Check(q.OldBin.Count == 1 && q.BinTaken.Count == 0, "an old-Mk bin part is offered, not used");
        Check(Near(q.NewPrice, 2500, 0.01) && Near(q.UpdatePrice, 400, 0.01), "until it's updated it's built new; the update is priced (" + q.UpdatePrice + ")");

        // New parts split by design: "Cabral I · 1 new part", "Shkhara 022 · 31 new parts".
        var craft3 = new Node();
        craft3.Nodes.Add(QuotePart("w", 100, 0, null, "cabral"));
        craft3.Nodes.Add(QuotePart("l1", 100, 0, null, "shkhara"));
        craft3.Nodes.Add(QuotePart("l2", 100, 0, null, "shkhara"));
        Quote q3 = Quotes.Make(craft3, "Cabral I", p => null, new FakeCosts(), s);
        Check(q3.Lines.Count == 2 && q3.Lines[0].Design == "cabral" && q3.Lines[0].Parts == 1 && q3.Lines[1].Design == "shkhara" && q3.Lines[1].Parts == 2,
              "new parts are one line per design, the top design first");
        Check(q3.NewParts == 3 && q3.Lines.Sum(l => l.Items.Count) == 3, "the lines still add up to every new part");

        // Flown hardware keeps its own Mk; everything else about it comes from the design.
        var design = new Node();
        for (int i = 0; i < 2; i++)
        {
            Node dp = new Node("PART");
            dp.Add("part", "probe_" + (100 + i));
            Node pm = dp.AddNode("MODULE"); pm.Add("name", Craft.ModuleName); pm.Add("design", "D1"); pm.Add("slot", i.ToString());
            PipModule(dp, 1);
            Node dec = dp.AddNode("MODULE"); dec.Add("name", "ModuleDecouple"); dec.Add("isDecoupled", "False");
            design.Nodes.Add(dp);
        }
        Node fp = new Node("PART");
        fp.Add("part", "probe_555");
        Node fm = fp.AddNode("MODULE"); fm.Add("name", Craft.ModuleName); fm.Add("design", "D1"); fm.Add("slot", "0"); fm.Add(Hardware.Field, "craftA:3");
        PipModule(fp, 2);
        Node fdec = fp.AddNode("MODULE"); fdec.Add("name", "ModuleDecouple"); fdec.Add("isDecoupled", "True");
        // The ship has one of the blueprint's two parts (its place in the blueprint: 0), at its own Mk 2.
        Node r = Hardware.Restore(design, new[] { 0 }, new[] { fp }, "craftA", out List<int> newSlots);
        Node slot0 = r.Children("PART").First(), slot1 = r.Children("PART").Last();
        Check(MkOf(slot0) == "2", "Restore keeps the flown part's own Mk (play-test #6), not the blueprint's (" + MkOf(slot0) + ")");
        Check(Hardware.Of(slot0) == "craftA:0", "the part the ship still has fills its place as that hardware (its note)");
        Check(slot0.Children("MODULE").First(x => x.Get("name") == "ModuleDecouple").Get("isDecoupled") == "False", "the rest of the part comes from the blueprint: its decoupler isn't fired");
        Check(MkOf(slot1) == "1" && Hardware.Of(slot1) == null && newSlots.SequenceEqual(new[] { 1 }), "the part it lost is new, with the blueprint's PIP data, for the current-Mk step");

        // A flown part updated to its program's next version is another part ("PIP.b" for "PIP.a"): it still fits its
        // slot, stays the part it is, and every link to the slot follows it.
        var design2 = new Node();
        Node top = new Node("PART");
        top.Add("part", "PIP.a_100");
        Node tm = top.AddNode("MODULE"); tm.Add("name", Craft.ModuleName);
        design2.Nodes.Add(top);
        Node below = new Node("PART");
        below.Add("part", "tank_200");
        below.Add("attN", "top,PIP.a_100_0|0.5|0");
        below.Add("link", "PIP.a_1001");
        Node bm = below.AddNode("MODULE"); bm.Add("name", Craft.ModuleName);
        design2.Nodes.Add(below);
        Node updated = new Node("PART");
        updated.Add("part", "PIP.b_777");
        Node um = updated.AddNode("MODULE"); um.Add("name", Craft.ModuleName); um.Add("design", "D2"); um.Add("slot", "0"); um.Add(Hardware.Field, "craftB:0");
        PipModule(updated, 1);
        Func<Node, string> family = p => (p.Get("part") ?? "").StartsWith("PIP.") ? "PIP.b" : (p.Get("part") ?? "");
        Node r2 = Hardware.Restore(design2, new[] { 0 }, new[] { updated }, "craftB", out List<int> new2);
        Node t2 = r2.Children("PART").First(), b2 = r2.Children("PART").Last();
        Check(Hardware.Of(t2) == "craftB:0" && t2.Get("part") == "PIP.b_100" && new2.SequenceEqual(new[] { 1 }), "an updated part fills its place as itself (" + t2.Get("part") + ")");
        Check(b2.Get("attN") == "top,PIP.b_100_0|0.5|0" && b2.Get("link") == "PIP.a_1001", "links to it follow it, and a longer id that only starts the same is left alone");
    }

    // Phil's round-4 play-test of 2026-11-09: ship identity is the suite's own record, not copies on parts.
    static void RoundFourChecks()
    {
        var s = new Settings();
        Check(Hardware.Parse("abc:12", out string c, out int i) && c == "abc" && i == 12 && !Hardware.Parse("abc", out _, out _) && !Hardware.Parse("abc:-1", out _, out _) && !Hardware.Parse("", out _, out _),
              "a hardware note reads as a stored craft and a place in it");
        var hangar = new FakeHangar();
        hangar.Parts["peleus"] = new List<Node> { QuotePart("pod", 600, 10), QuotePart("tank", 300, 100), QuotePart("engine", 900, 0) };
        hangar.Wear["peleus"] = 0.3;

        // Peleus III #2 in the VAB with a barometer added: its own parts and one new part.
        var refit = new Node();
        refit.Nodes.Add(QuotePart("pod", 600, 10, "peleus:0"));
        refit.Nodes.Add(QuotePart("tank", 300, 100, "peleus:1", null, null));
        refit.Nodes.Add(QuotePart("engine", 900, 0, "peleus:2"));
        refit.Nodes.Add(QuotePart("barometer", 50, 0));
        Quote q = Quotes.Make(refit, "Peleus IV", hangar.OwnerOf, new FakeCosts(), s, null, hangar.PartsOf);
        Check(q.HangarShips.SetEquals(new[] { "peleus" }) && q.Owners.Count == 3 && Near(q.NewPrice, 50, 0.01), "three of its parts are Peleus III #2's hardware, the barometer is new");
        Check(Near(q.RefurbishPrice, 1800 * 0.3 * 0.5, 0.01), "its parts are refurbished by its wear (no flight counts in it)");

        // A note that points at another part, a stale index, or the same part twice: none of them is hardware.
        var odd = new Node();
        odd.Nodes.Add(QuotePart("pod", 600, 10, "peleus:0"));
        odd.Nodes.Add(QuotePart("pod", 600, 10, "peleus:0"));      // a copy that kept the note
        odd.Nodes.Add(QuotePart("tank", 300, 100, "peleus:2"));    // that place holds the engine
        odd.Nodes.Add(QuotePart("tank", 300, 100, "peleus:9"));    // no such place
        odd.Nodes.Add(QuotePart("tank", 300, 100, "gone:1"));      // a ship that isn't in the hangar (taken apart)
        Quote qo = Quotes.Make(odd, "Odd", hangar.OwnerOf, new FakeCosts(), s, null, hangar.PartsOf);
        Check(qo.Owners.Count == 1 && Near(qo.NewPrice, 600 + 300 * 3, 0.01), "only the first pod is Peleus III #2's hardware; a copy, a wrong place and a ship that's gone are new parts (" + qo.NewPrice + ")");
        Check(qo.Lines.First(l => l.Kind == OwnerKind.Hangar).LeftOut == 2, "the parts of Peleus III #2 the build doesn't use go to the bin");

        // Something added to a hangar part in the VAB (cargo, a bigger size) is new: full price on top of its refurbish.
        var grown = new Node();
        grown.Nodes.Add(QuotePart("pod", 700, 0, "peleus:0"));
        Quote qg = Quotes.Make(grown, "Grown", p => { Owner o = hangar.OwnerOf(p); if (o != null) o.Added = 100; return o; }, new FakeCosts(), s, null, hangar.PartsOf);
        double share = Pricing.RefurbishShare(0.3, s);
        Check(Near(qg.NewPrice, 100, 0.01) && Near(qg.Lines[0].Price, 600 * share + 100 + (300 + 900) * share, 0.01), "a pod with 100 of cargo added costs its refurbish plus the cargo new (" + qg.NewPrice + ")");

        // The bin is counts: two batteries in the bin, three in the craft: two from the bin (free, filled from empty), one new.
        var bat = new Node();
        for (int k = 0; k < 3; k++) bat.Nodes.Add(QuotePart("battery", 80, 20));
        int binCount = 2;
        Quote qb = Quotes.Make(bat, "Batteries", p => null, new FakeCosts(), s, p => binCount-- > 0 ? new Owner { Kind = OwnerKind.Bin, BinKey = "battery" } : null);
        Check(qb.BinTaken["battery"] == 2 && qb.BinParts == 2 && Near(qb.NewPrice, 80, 0.01) && qb.RefurbishPrice == 0, "two batteries from the bin cost nothing, the third is new");
        Check(Near(qb.FuelPrice, 3 * 20 * 0.8, 0.01), "bin parts are emptied as they go in, so every battery is filled (" + qb.FuelPrice + ")");

        // A piece of a launch as a craft of its own: what came home, or a ship without the launcher under it.
        var launch = new Node();
        launch.Nodes.Add(Linked("pod", 0, 1));        // 0: the Kupe's pod, root of the launch
        launch.Nodes.Add(Linked("dec", 1, 2));        // 1: decoupler under it
        launch.Nodes.Add(Linked("stage", 2, 3));      // 2: the launcher's top
        launch.Nodes.Add(Linked("engine", 3, -1));    // 3
        PieceResult stage = Pieces.Build(launch, new[] { 2, 3 });
        Check(stage.Root == 2 && stage.Placed.SequenceEqual(new[] { 2, 3 }), "the first stage on its own is a craft rooted at its top");
        PieceResult whole = Pieces.Build(launch, new[] { 0, 1, 2, 3 });
        Check(whole.Root == 0 && whole.Placed.SequenceEqual(new[] { 0, 1, 2, 3 }), "the whole launch comes home in its own order");
        PieceResult ship = Pieces.Build(launch, new[] { 0, 1 });
        Check(ship.Placed.SequenceEqual(new[] { 0, 1 }) && Pieces.Validate(ship.Craft).Count == 0 && ship.Craft.Children("PART").Count() == 2, "the Kupe without the launcher under it is a valid craft of its own: its blueprint");
        PieceResult top = Pieces.Build(launch, new[] { 0, 1, 9 });
        Check(top.Unplaced.SequenceEqual(new[] { 9 }), "a part the launch never had can't be placed");
    }

    static Node Linked(string name, int id, int child)
    {
        var p = new Node("PART");
        p.Add("part", name + "_" + id);
        if (child >= 0) p.Add("link", new[] { "pod", "dec", "stage", "engine" }[child] + "_" + child);
        if (name == "dec") { Node m = p.AddNode("MODULE"); m.Add("name", "ModuleDecouple"); }
        return p;
    }

    // A stock craft as a ship's blueprint: a piece of it came home (a first stage), and Restore gives the whole craft back,
    // with the parts it still has in their own places as its hardware and every other part new.
    static void RestoreChecks(string name, Node craft, Craft c, CraftPart realRoot)
    {
        CraftPart top = c.Parts.Where(p => p != realRoot && !p.IsCompound).OrderByDescending(p => Subtree(p).Count).FirstOrDefault();
        if (top == null) return;
        PieceResult piece = Pieces.Build(craft, Subtree(top).Select(p => p.Index).ToList());
        List<Node> current = piece.Craft.Children("PART").ToList();
        var slots = piece.Placed.ToList();   // the piece's part k is the blueprint's part Placed[k]
        Node r = Hardware.Restore(craft, slots, current, "oak4", out List<int> fresh);
        string label = name + ": restoring a flown piece";
        List<Node> parts = r.Children("PART").ToList();
        Check(parts.Count == c.Parts.Count, label + " gives the whole ship back");
        Check(fresh.Count == c.Parts.Count - current.Count, label + ": " + current.Count + " of its own parts, " + fresh.Count + " new");
        Check(slots.Select((s, k) => Hardware.Of(parts[s]) == Hardware.Make("oak4", k)).All(x => x), label + ": each of its parts sits in its own place, as that hardware");
        Check(fresh.All(i => Hardware.Of(parts[i]) == null), label + ": the rest are new parts");
        Check(Pieces.Validate(r).Count == 0, label + ": the restored craft is valid");
    }

    static List<CraftPart> Subtree(CraftPart p)
    {
        var list = new List<CraftPart> { p };
        foreach (CraftPart c in p.Children) list.AddRange(Subtree(c));
        return list;
    }

    static int CraftChecks(string name, Node craft)
    {
        Craft c = Craft.Read(craft);
        List<string> orig = Pieces.Validate(craft);
        Check(orig.Count == 0, name + ": stock craft is valid (" + string.Join("; ", orig.Take(3)) + ")");
        if (orig.Count > 0) return 0;

        // Everything came home.
        CraftPart realRoot = c.Parts.First(p => p.Parent == null);
        PieceResult all = Pieces.Build(craft, c.Parts.Select(p => p.Index).ToList());
        Check(all.Placed.Count == c.Parts.Count, name + ": whole craft placed (" + all.Placed.Count + " of " + c.Parts.Count + ")");
        Check(all.Root == realRoot.Index, name + ": whole craft keeps its root");
        Check(all.Craft.Children("PART").First().Get("part") == realRoot.Ref && all.Placed[0] == realRoot.Index, name + ": root is written first");
        Check(Pieces.Validate(all.Craft).Count == 0, name + ": whole craft rebuilt valid");
        Check(Node.Parse(all.Craft.WriteContents()).Children("PART").Count() == c.Parts.Count, name + ": whole craft survives write and parse");
        List<Node> rebuilt = all.Craft.Children("PART").ToList();
        Check(all.Placed.Select((i, k) => rebuilt[k].Get("part") == c.Parts[i].Ref).All(x => x), name + ": the rebuilt craft's part k is the launch's part Placed[k]");

        RestoreChecks(name, craft, c, realRoot);

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
            PieceResult r = Pieces.Build(craft, sub.Select(p => p.Index).ToList());
            Check(r.Root == top.Index, label + ": new root is the piece's top part");
            Check(r.Placed.Count == sub.Count - dropped, label + ": placed " + r.Placed.Count + ", expected " + (sub.Count - dropped));
            Check(r.Dropped.Count == dropped, label + ": dropped struts/fuel lines " + r.Dropped.Count + ", expected " + dropped);
            Check(r.Unplaced.Count == 0, label + ": nothing unplaced");
            List<string> probs = Pieces.Validate(r.Craft);
            Check(probs.Count == 0, label + ": rebuilt craft valid (" + string.Join("; ", probs.Take(3)) + ")");
            Node reparsed = Node.Parse(r.Craft.WriteContents());
            Check(Pieces.Validate(reparsed).Count == 0, label + ": valid after write and parse");

            // A leaf didn't come home (dropped, broken off or lost): the piece is still a valid craft without it.
            CraftPart leaf = sub.Where(p => p != top && p.Children.Count == 0 && !p.IsCompound).OrderBy(_ => rng.Next()).FirstOrDefault();
            if (leaf != null)
            {
                PieceResult b = Pieces.Build(craft, sub.Where(p => p != leaf).Select(p => p.Index).ToList());
                Check(!b.Placed.Contains(leaf.Index) && Pieces.Validate(b.Craft).Count == 0, label + ": valid without " + leaf.Ref);
            }

            // Strangers came home attached (docked or grabbed in flight): they can't be placed.
            if (sub.Count >= 4)
            {
                var outside = c.Parts.Where(p => !sub.Contains(p) && p.Parent != null && !sub.Contains(p.Parent) && p.Children.Count == 0).OrderBy(_ => rng.Next()).Take(2).ToList();
                var home = sub.Select(p => p.Index).Concat(outside.Select(p => p.Index)).Concat(new[] { c.Parts.Count + 7 }).ToList();
                PieceResult x = Pieces.Build(craft, home);
                Check(x.Root == top.Index, label + ": strangers don't change the root");
                Check(x.Unplaced.Count == outside.Count + 1, label + ": strangers unplaced " + x.Unplaced.Count + ", expected " + (outside.Count + 1));
            }
        }
        return tried;
    }
}
