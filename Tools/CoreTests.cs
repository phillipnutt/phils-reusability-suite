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
