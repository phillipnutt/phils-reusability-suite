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
        public static CompletionResult Complete(Node design, string designId, IEnumerable<Node> hangarParts)
        {
            var result = new CompletionResult();
            var bySlot = new Dictionary<int, Node>();
            var nameBySlot = new Dictionary<int, string>();
            foreach (Node p in hangarParts)
            {
                Node m = Module(p);
                if (m == null || m.Get("design") != designId) continue;
                if (int.TryParse(m.Get("slot"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slot) && slot >= 0 && !bySlot.ContainsKey(slot))
                {
                    bySlot[slot] = m;
                    nameBySlot[slot] = PartName(p);
                }
            }

            Node craft = design.Clone();
            int i = 0;
            foreach (Node part in craft.Children("PART"))
            {
                // If the design was saved over since the stage flew, a slot may now hold another part: then the
                // flown part doesn't fit it, and the slot is built new.
                if (bySlot.ContainsKey(i) && nameBySlot[i] != PartName(part)) bySlot.Remove(i);
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
                    result.Used.Add(flown.Get("serial"));
                }
                else
                {
                    // A new part: no serial yet (the editor gives one), never flown, no ship.
                    m.Set("serial", "");
                    m.Set("flights", "0");
                    m.Set("hull", "");
                    result.NewSlots.Add(i);
                }
                i++;
            }
            result.Craft = craft;
            return result;
        }

        static Node Module(Node part) => part.Children("MODULE").FirstOrDefault(m => m.Get("name") == Craft.ModuleName);

        static string PartName(Node part)
        {
            string r = part.Get("part") ?? "";
            return r.Contains("_") ? r.Substring(0, r.LastIndexOf('_')) : r;
        }
    }
}
