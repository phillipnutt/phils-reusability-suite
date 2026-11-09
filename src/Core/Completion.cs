using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PhilsReusabilitySuite.Core
{
    /// <summary>What completing a hangar ship to its design gives.</summary>
    public sealed class CompletionResult
    {
        /// <summary>The whole design, with the hangar ship's parts in their own slots.</summary>
        public Node Craft;
        /// <summary>Hangar serials placed into the design.</summary>
        public readonly List<string> Used = new List<string>();
        /// <summary>Design slots the hangar ship doesn't have: built new (an upper stage, a lost fin).</summary>
        public readonly List<int> NewSlots = new List<int>();
    }

    /// <summary>
    /// "Restore to design": a flown first stage (or any hangar ship) completed from the design it was built
    /// from. Every part placed from a Phil's Builds design carries the design's id and its slot (its position
    /// in the design file). The completed craft is the design file itself, with each slot the hangar ship
    /// has filled by that exact part (its serial, flights and ship identity), and every other slot a new part.
    /// </summary>
    public static class Completion
    {
        /// <param name="family">What a part becomes once brought up to date (Phil's Iterative Parts: an old version's part
        /// becomes the newest version's), or null. A flown part fits its slot if it's the same part, or the same part
        /// once updated; either way it stays the part it is there (flown hardware keeps what it was built as).</param>
        public static CompletionResult Complete(Node design, string designId, IEnumerable<Node> hangarParts, Func<Node, string> family = null)
        {
            var result = new CompletionResult();
            var bySlot = new Dictionary<int, Node>();
            var nameBySlot = new Dictionary<int, string>();
            var partBySlot = new Dictionary<int, Node>();
            foreach (Node p in hangarParts)
            {
                Node m = Module(p);
                if (m == null || m.Get("design") != designId) continue;
                if (int.TryParse(m.Get("slot"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot) && slot >= 0 && !bySlot.ContainsKey(slot))
                {
                    bySlot[slot] = m;
                    nameBySlot[slot] = PartName(p);
                    partBySlot[slot] = p;
                }
            }

            Node craft = design.Clone();
            int i = 0;
            foreach (Node part in craft.Children("PART"))
            {
                // If the design was saved over since the stage flew, a slot may now hold another part: then the
                // flown part doesn't fit it, and the slot is built new.
                if (bySlot.ContainsKey(i) && nameBySlot[i] != PartName(part) && (family == null || family(partBySlot[i]) != family(part))) bySlot.Remove(i);
                Node m = Module(part);
                if (m == null)
                {
                    m = part.AddNode("MODULE");
                    m.Add("name", Craft.ModuleName);
                    m.Add("isEnabled", "True");
                }
                m.Set("design", designId);
                m.Set("slot", i.ToString(CultureInfo.InvariantCulture));
                if (bySlot.TryGetValue(i, out Node flown) && !string.IsNullOrEmpty(flown.Get("serial")))
                {
                    m.Set("serial", flown.Get("serial"));
                    m.Set("flights", flown.Get("flights") ?? "0");
                    m.Set("hull", flown.Get("hull") ?? "");
                    m.Set("wear", flown.Get("wear") ?? "0");
                    // Another version of the slot's part (updated, or not yet): the slot takes the flown part's own.
                    if (nameBySlot[i] != PartName(part)) TakePartName(craft, part, nameBySlot[i]);
                    CarryHardware(partBySlot[i], part);
                    result.Used.Add(flown.Get("serial"));
                }
                else
                {
                    // A new part: no serial yet (the editor gives one), never flown, no ship.
                    m.Set("serial", "");
                    m.Set("flights", "0");
                    m.Set("hull", "");
                    m.Set("wear", "0");
                    result.NewSlots.Add(i);
                }
                i++;
            }
            result.Craft = craft;
            return result;
        }

        /// <summary>
        /// Modules that describe the hardware itself, not how the design uses it: Phil's Iterative Parts keeps a
        /// part's program, version and Mk there. A flown part keeps them. Everything else (staging, whether a
        /// decoupler has fired, a packed chute) comes from the design, as a restored ship should.
        /// </summary>
        public static readonly string[] HardwareModules = { "ModulePipPart", "ModulePipEngine" };

        /// <summary>Puts the flown part's hardware modules in place of the design's (the same module, by name and order).</summary>
        public static void CarryHardware(Node flownPart, Node designPart)
        {
            foreach (string name in HardwareModules)
            {
                List<Node> from = flownPart.Children("MODULE").Where(x => x.Get("name") == name).ToList();
                int k = 0;
                for (int j = 0; j < designPart.Nodes.Count && k < from.Count; j++)
                {
                    Node d = designPart.Nodes[j];
                    if (d.Name != "MODULE" || d.Get("name") != name) continue;
                    designPart.Nodes[j] = from[k++].Clone();
                }
            }
        }

        /// <summary>Makes a craft's part another part ("PIP.x_42" becomes "PIP.y_42"), with every link to it renamed.</summary>
        public static void TakePartName(Node craft, Node part, string name)
        {
            string from = part.Get("part") ?? "";
            int cut = from.LastIndexOf('_');
            string to = cut >= 0 ? name + from.Substring(cut) : name;
            part.Set("part", to);
            foreach (Node other in craft.Children("PART")) if (!ReferenceEquals(other, part)) RenameRef(other, from, to);
        }

        /// <summary>Renames a part reference ("PIP.x_4294") in a part's links and attachments, whole references only.</summary>
        public static void RenameRef(Node part, string from, string to)
        {
            if (string.IsNullOrEmpty(from) || from == to) return;
            for (int k = 0; k < part.Values.Count; k++)
            {
                string v = part.Values[k].Value;
                if (string.IsNullOrEmpty(v) || !v.Contains(from)) continue;
                var sb = new System.Text.StringBuilder();
                int i = 0;
                while (i < v.Length)
                {
                    int at = v.IndexOf(from, i, StringComparison.Ordinal);
                    if (at < 0) { sb.Append(v, i, v.Length - i); break; }
                    int end = at + from.Length;
                    bool startOk = at == 0 || v[at - 1] == ',' || char.IsWhiteSpace(v[at - 1]);
                    bool endOk = end == v.Length || !char.IsDigit(v[end]);
                    sb.Append(v, i, at - i).Append(startOk && endOk ? to : from);
                    i = end;
                }
                part.Values[k] = new KeyValuePair<string, string>(part.Values[k].Key, sb.ToString());
            }
        }

        static Node Module(Node part) => part.Children("MODULE").FirstOrDefault(m => m.Get("name") == Craft.ModuleName);

        static string PartName(Node part)
        {
            string r = part.Get("part") ?? "";
            return r.Contains("_") ? r.Substring(0, r.LastIndexOf('_')) : r;
        }
    }
}
