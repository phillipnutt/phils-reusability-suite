using System;
using System.Collections.Generic;
using System.Linq;

namespace PhilsReusabilitySuite.Core
{
    /// <summary>One PART node of a craft, with its place in the part tree.</summary>
    public sealed class CraftPart
    {
        public Node Node;
        /// <summary>Its place in the craft (the order KSP writes them, root first).</summary>
        public int Index;
        /// <summary>The craft's id for the part, "name_123456".</summary>
        public string Ref;
        public string Id;
        public CraftPart Parent;
        public readonly List<CraftPart> Children = new List<CraftPart>();
        /// <summary>For struts and fuel lines: the craft id of the part at the other end.</summary>
        public string TargetId;
        public bool IsCompound => Node.Get("partName") == "CompoundPart";
        public string PartName => Ref.Contains("_") ? Ref.Substring(0, Ref.LastIndexOf('_')) : Ref;

        /// <summary>How far below the root it hangs (the root is 0).</summary>
        public int Depth { get { int d = 0; for (CraftPart p = Parent; p != null; p = p.Parent) d++; return d; } }
    }

    /// <summary>A craft (a .craft file or KSP's launched-ship config) read into a part tree.</summary>
    public sealed class Craft
    {
        public const string ModuleName = "PRSPart";

        public Node Root;
        public readonly List<CraftPart> Parts = new List<CraftPart>();
        public readonly Dictionary<string, CraftPart> ByRef = new Dictionary<string, CraftPart>();
        public readonly Dictionary<string, CraftPart> ById = new Dictionary<string, CraftPart>();
        public readonly List<string> Problems = new List<string>();

        public static Craft Read(Node root)
        {
            var c = new Craft { Root = root };
            foreach (Node n in root.Children("PART"))
            {
                var p = new CraftPart { Node = n, Index = c.Parts.Count, Ref = n.Get("part") ?? "" };
                p.Id = p.Ref.Contains("_") ? p.Ref.Substring(p.Ref.LastIndexOf('_') + 1) : p.Ref;
                p.TargetId = n.Child("PARTDATA")?.Get("tgt");
                if (p.TargetId == "0" || p.TargetId == "") p.TargetId = null;   // a strut left unconnected
                if (c.ByRef.ContainsKey(p.Ref)) c.Problems.Add("duplicate part id " + p.Ref);
                else c.ByRef[p.Ref] = p;
                c.ById[p.Id] = p;
                c.Parts.Add(p);
            }
            foreach (CraftPart p in c.Parts)
                foreach (string link in p.Node.GetAll("link"))
                {
                    if (!c.ByRef.TryGetValue(link, out CraftPart child)) { c.Problems.Add(p.Ref + " links to missing " + link); continue; }
                    if (child.Parent != null) { c.Problems.Add(child.Ref + " has two parents"); continue; }
                    child.Parent = p;
                    p.Children.Add(child);
                }
            return c;
        }

        /// <summary>The root part: the one with no parent (KSP writes it first).</summary>
        public CraftPart RootPart => Parts.FirstOrDefault(p => p.Parent == null) ?? Parts.FirstOrDefault();

        /// <summary>Of these parts, the one highest in the tree (nearest the root; the first written on a tie), or -1.</summary>
        public int Highest(IEnumerable<int> indices)
        {
            int best = -1, bestDepth = int.MaxValue;
            foreach (int i in indices)
            {
                if (i < 0 || i >= Parts.Count) continue;
                int d = Parts[i].Depth;
                if (d < bestDepth || (d == bestDepth && i < best)) { best = i; bestDepth = d; }
            }
            return best;
        }

        /// <summary>
        /// The part id named in an attN or srfN value ("bottom,tank_123_0|1|0" gives "tank_123"; "top,Null_0_..."
        /// gives "Null_0"), and whatever follows it.
        /// </summary>
        public static string RefIn(string attValue, out string head, out string tail)
        {
            int comma = attValue.IndexOf(',');
            head = comma >= 0 ? attValue.Substring(0, comma) : "";
            string rest = comma >= 0 ? attValue.Substring(comma + 1) : attValue;
            // Newer srfN values go on after the part id: "srfAttach,tank_123,Collider,0|0|0,...".
            int c2 = rest.IndexOf(',');
            string seg = c2 >= 0 ? rest.Substring(0, c2) : rest;
            string after = c2 >= 0 ? rest.Substring(c2) : "";
            int u1 = seg.IndexOf('_');
            int u2 = u1 >= 0 ? seg.IndexOf('_', u1 + 1) : -1;
            tail = (u2 >= 0 ? seg.Substring(u2) : "") + after;
            return u2 >= 0 ? seg.Substring(0, u2) : seg;
        }

        public static bool IsNullRef(string r) => r.StartsWith("Null", StringComparison.Ordinal);
    }

    /// <summary>What came home as one piece, turned back into a craft the VAB can load. Parts are named by their
    /// place in the launch's craft.</summary>
    public sealed class PieceResult
    {
        /// <summary>The loadable craft: the launch's parts that came home, as they left the pad.</summary>
        public Node Craft;
        /// <summary>The launch part that is the piece's root.</summary>
        public int Root = -1;
        /// <summary>Launch parts placed in the craft, in the craft's own order (so the craft's part k is Placed[k]).</summary>
        public readonly List<int> Placed = new List<int>();
        /// <summary>Launch parts that came home but can't be placed (docked, grabbed or added in flight): parts bin.</summary>
        public readonly List<int> Unplaced = new List<int>();
        /// <summary>Launch parts that broke off this piece and didn't come home.</summary>
        public readonly List<int> Missing = new List<int>();
        /// <summary>Struts and fuel lines whose other end didn't come home: parts bin.</summary>
        public readonly List<int> Dropped = new List<int>();
    }

    public static class Pieces
    {
        /// <summary>
        /// Builds the craft for a piece that came home. <paramref name="launch"/> is the ship as it left the
        /// pad; <paramref name="cameHome"/> its parts that came home (by their place in it); <paramref name="lostOnLanding"/>
        /// parts the suite's own landing check broke off (they count as lost even if they're separators).
        /// </summary>
        public static PieceResult Build(Node launch, ICollection<int> cameHome, ICollection<int> lostOnLanding = null)
        {
            var result = new PieceResult();
            Craft craft = Craft.Read(launch);
            var present = new HashSet<CraftPart>();
            foreach (int i in cameHome)
            {
                if (i >= 0 && i < craft.Parts.Count) present.Add(craft.Parts[i]);
                else result.Unplaced.Add(i);
            }
            if (present.Count == 0) return result;

            // Group the parts by the highest part above them that also came home. A real piece is one group;
            // anything else (an odd docking, say) can't be placed.
            var groups = new Dictionary<CraftPart, List<CraftPart>>();
            foreach (CraftPart p in present)
            {
                CraftPart top = p;
                while (top.Parent != null && present.Contains(top.Parent)) top = top.Parent;
                if (!groups.TryGetValue(top, out List<CraftPart> g)) groups[top] = g = new List<CraftPart>();
                g.Add(p);
            }
            KeyValuePair<CraftPart, List<CraftPart>> main = groups.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Key.Index).First();
            CraftPart root = main.Key;
            var keep = new HashSet<CraftPart>(main.Value);
            foreach (KeyValuePair<CraftPart, List<CraftPart>> kv in groups)
                if (kv.Key != root) result.Unplaced.AddRange(kv.Value.Select(p => p.Index));

            // A strut or fuel line whose other end didn't come home goes to the bin, not into the craft.
            var keepIds = new HashSet<string>(keep.Select(p => p.Id));
            foreach (CraftPart p in keep.ToList())
                if (p.IsCompound && p != root && p.Children.Count == 0 && p.TargetId != null && !keepIds.Contains(p.TargetId))
                {
                    keep.Remove(p);
                    result.Dropped.Add(p.Index);
                }

            // Parts this piece lost (not separated), and anything attached to them.
            var landing = new HashSet<int>(lostOnLanding ?? new int[0]);
            FindMissing(root, keep, present, landing, result.Missing);

            var refs = new HashSet<string>(keep.Select(p => p.Ref));
            var outNode = new Node(launch.Name);
            outNode.Values.AddRange(launch.Values);
            foreach (Node n in launch.Nodes)
                if (n.Name != "PART") outNode.Nodes.Add(n.Clone());
            foreach (CraftPart p in PreOrder(root, keep))
            {
                outNode.Nodes.Add(Rewrite(p, p == root, refs));
                result.Placed.Add(p.Index);
            }
            result.Craft = outNode;
            result.Root = root.Index;
            return result;
        }

        /// <summary>
        /// What a piece lost, read from the ship as it launched: a part that didn't come home, hanging from one
        /// that did, was lost, unless the two parted at a separator (a decoupler, docking port, claw or launch
        /// clamp), which is how stages and payloads leave on purpose. Anything hanging from a lost part went with
        /// it. One look when the piece comes home, instead of following KSP's joint-break, decouple and
        /// destruction events through the flight.
        /// </summary>
        static void FindMissing(CraftPart p, HashSet<CraftPart> keep, HashSet<CraftPart> present, HashSet<int> lostOnLanding, List<int> missing)
        {
            foreach (CraftPart c in p.Children)
            {
                if (keep.Contains(c)) { FindMissing(c, keep, present, lostOnLanding, missing); continue; }
                if (present.Contains(c)) continue;   // came home, but goes to the bin (a strut whose other end is gone)
                bool landing = lostOnLanding.Contains(c.Index);
                if (landing || !(IsSeparator(p) || IsSeparator(c))) AddSubtree(c, keep, missing);
            }
        }

        /// <summary>A part made to let go of what's attached to it: decouplers, docking ports, claws, launch clamps.</summary>
        public static bool IsSeparator(CraftPart p)
        {
            foreach (Node m in p.Node.Children("MODULE"))
            {
                string n = m.Get("name") ?? "";
                if (n.Contains("Decouple") || n == "ModuleDockingNode" || n == "ModuleGrappleNode" || n == "LaunchClamp") return true;
            }
            return false;
        }

        static void AddSubtree(CraftPart p, HashSet<CraftPart> keep, List<int> into)
        {
            if (keep.Contains(p)) return;
            into.Add(p.Index);
            foreach (CraftPart c in p.Children) AddSubtree(c, keep, into);
        }

        static IEnumerable<CraftPart> PreOrder(CraftPart p, HashSet<CraftPart> keep)
        {
            yield return p;
            foreach (CraftPart c in p.Children)
                if (keep.Contains(c))
                    foreach (CraftPart d in PreOrder(c, keep)) yield return d;
        }

        static Node Rewrite(CraftPart p, bool isRoot, HashSet<string> refs)
        {
            Node n = p.Node.Clone();
            var vals = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, string> kv in n.Values)
            {
                switch (kv.Key)
                {
                    case "link":
                    case "sym":
                        if (refs.Contains(kv.Value)) vals.Add(kv);
                        break;
                    case "srfN":
                    {
                        string r = Craft.RefIn(kv.Value, out _, out _);
                        if (!isRoot && refs.Contains(r)) vals.Add(kv);
                        break;
                    }
                    case "attN":
                    {
                        string r = Craft.RefIn(kv.Value, out string head, out string tail);
                        if (Craft.IsNullRef(r) || refs.Contains(r)) vals.Add(kv);
                        else vals.Add(new KeyValuePair<string, string>("attN", head + ",Null_0" + tail));
                        break;
                    }
                    default:
                        vals.Add(kv);
                        break;
                }
            }
            n.Values.Clear();
            n.Values.AddRange(vals);
            return n;
        }

        /// <summary>Checks that a craft is one well-formed part tree. Returns the problems found, if any.</summary>
        public static List<string> Validate(Node craftNode)
        {
            Craft c = Craft.Read(craftNode);
            var problems = new List<string>(c.Problems);
            if (c.Parts.Count == 0) { problems.Add("no parts"); return problems; }
            // The root is the one part with no parent. Stock craft don't always list it first, though this
            // mod always writes it first.
            List<CraftPart> roots = c.Parts.Where(p => p.Parent == null).ToList();
            if (roots.Count != 1) problems.Add(roots.Count + " parts have no parent: " + string.Join(", ", roots.Take(3).Select(p => p.Ref)));
            CraftPart root = roots.Count > 0 ? roots[0] : c.Parts[0];
            foreach (CraftPart p in c.Parts)
            {
                foreach (string v in p.Node.GetAll("srfN"))
                {
                    string r = Craft.RefIn(v, out _, out _);
                    if (!Craft.IsNullRef(r) && !c.ByRef.ContainsKey(r)) problems.Add(p.Ref + " is surface-attached to missing " + r);
                }
                foreach (string v in p.Node.GetAll("attN"))
                {
                    string r = Craft.RefIn(v, out _, out _);
                    if (!Craft.IsNullRef(r) && !c.ByRef.ContainsKey(r)) problems.Add(p.Ref + " node attached to missing " + r);
                }
                foreach (string v in p.Node.GetAll("sym"))
                    if (!c.ByRef.ContainsKey(v)) problems.Add(p.Ref + " has missing symmetry partner " + v);
                if (p.IsCompound && p.TargetId != null && !c.ById.ContainsKey(p.TargetId))
                    problems.Add(p.Ref + " (strut or fuel line) targets missing part " + p.TargetId);
            }
            var seen = new HashSet<CraftPart>();
            var stack = new Stack<CraftPart>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                CraftPart p = stack.Pop();
                if (!seen.Add(p)) { problems.Add("loop at " + p.Ref); continue; }
                foreach (CraftPart ch in p.Children) stack.Push(ch);
            }
            if (seen.Count != c.Parts.Count) problems.Add((c.Parts.Count - seen.Count) + " parts can't be reached from the root");
            return problems;
        }
    }
}
