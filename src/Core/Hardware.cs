using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PhilsReusabilitySuite.Core
{
    /// <summary>
    /// The one note a part carries about being real, built hardware (Phil, round 4: "the VAB knows that all of those
    /// parts are real, have been built, and that ship is 'The Peleus', and it knows that the new part I just added is
    /// not a built part"). Only the suite's own loads put it there: which stored craft the part comes from, and its
    /// place in that craft ("craftId:index"). Every stored craft gets a new id whenever its parts change, so a note
    /// from an older copy points at nothing. Nothing else about a ship is kept on its parts. Also the helpers for a
    /// part's own hardware (a Phil's Iterative Parts program and Mk) and for restoring a ship from its blueprint.
    /// </summary>
    public static class Hardware
    {
        public const string Field = "hw";

        /// <summary>The suite's own module on a part node, or null.</summary>
        public static Node Module(Node part) => part?.Children("MODULE").FirstOrDefault(m => m.Get("name") == Craft.ModuleName);

        public static string Make(string craftId, int index) => craftId + ":" + index.ToString(CultureInfo.InvariantCulture);

        public static bool Parse(string token, out string craftId, out int index)
        {
            craftId = null; index = -1;
            if (string.IsNullOrEmpty(token)) return false;
            int c = token.LastIndexOf(':');
            if (c <= 0 || !int.TryParse(token.Substring(c + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out index) || index < 0) return false;
            craftId = token.Substring(0, c);
            return true;
        }

        /// <summary>A part node's note, or null.</summary>
        public static string Of(Node part)
        {
            string t = Module(part)?.Get(Field);
            return string.IsNullOrEmpty(t) ? null : t;
        }

        /// <summary>Sets (or clears, with null) a part node's note, adding the suite's module if it has none.</summary>
        public static void Set(Node part, string token)
        {
            Node m = Module(part);
            if (m == null)
            {
                if (string.IsNullOrEmpty(token)) return;
                m = part.AddNode("MODULE");
                m.Add("name", Craft.ModuleName);
                m.Add("isEnabled", "True");
            }
            m.Set(Field, token ?? "");
        }

        /// <summary>A copy of a craft with every part noted as that craft's own ("craftId:0", "craftId:1"...).</summary>
        public static Node Noted(Node craft, string craftId)
        {
            Node c = craft.Clone();
            int i = 0;
            foreach (Node p in c.Children("PART")) Set(p, Make(craftId, i++));
            return c;
        }

        /// <summary>The craft with no notes on it (as stored: a stored craft is its own parts).</summary>
        public static Node Plain(Node craft)
        {
            foreach (Node p in craft.Children("PART")) { Node m = Module(p); if (m != null && m.Get(Field) != null) m.Set(Field, ""); }
            return craft;
        }

        /// <summary>A Phil's Builds design file as a new build: each part stamped with the design and its slot, no notes.</summary>
        public static Node Stamped(Node design, string designId)
        {
            Node craft = design.Clone();
            int i = 0;
            foreach (Node part in craft.Children("PART"))
            {
                Node m = Module(part);
                if (m == null)
                {
                    m = part.AddNode("MODULE");
                    m.Add("name", Craft.ModuleName);
                    m.Add("isEnabled", "True");
                }
                m.Set("design", designId);
                m.Set("slot", (i++).ToString(CultureInfo.InvariantCulture));
                m.Set(Field, "");
            }
            return craft;
        }

        /// <summary>A part node's part name, without the craft's id ("fuelTank" from "fuelTank_4294").</summary>
        public static string PartName(Node part)
        {
            string r = part?.Get("part") ?? "";
            int u = r.LastIndexOf('_');
            return u > 0 ? r.Substring(0, u) : r;
        }

        /// <summary>
        /// "Restore" (Phil, round 4: "couldn't it have a blueprint of the 'Peleus III #2' and restore uses that?"): the
        /// ship's own blueprint, the whole ship as it was last built (or as it last launched, less what it dropped on
        /// purpose), with each part the ship still has in its place as that hardware (its note, its own Mk) and every part
        /// it lost new. <paramref name="slots"/> gives each of the ship's parts its place in the blueprint.
        /// </summary>
        public static Node Restore(Node blueprint, IList<int> slots, IList<Node> current, string craftId, out List<int> newSlots)
        {
            var present = new Dictionary<int, int>();
            for (int k = 0; k < slots.Count && k < current.Count; k++)
                if (slots[k] >= 0 && !present.ContainsKey(slots[k])) present[slots[k]] = k;
            Node craft = blueprint.Clone();
            newSlots = new List<int>();
            int j = -1;
            foreach (Node part in craft.Children("PART").ToList())
            {
                j++;
                if (present.TryGetValue(j, out int k))
                {
                    Set(part, Make(craftId, k));
                    // An updated PIP part is another part now: the slot takes the part the ship actually has.
                    if (PartName(current[k]) != PartName(part)) TakePartName(craft, part, PartName(current[k]));
                    CarryHardware(current[k], part);
                    // Its cargo as it is now: Restore puts back lost parts, not what was used up.
                    CarryModules(current[k], part, "ModuleInventoryPart");
                }
                else
                {
                    Set(part, null);
                    newSlots.Add(j);
                }
            }
            return craft;
        }

        /// <summary>
        /// Modules that describe the hardware itself, not how it's used: Phil's Iterative Parts keeps a part's program,
        /// version and Mk there. Hardware keeps them. Everything else (staging, whether a decoupler has fired, a packed
        /// chute) comes from the blueprint, as a restored ship should.
        /// </summary>
        public static readonly string[] HardwareModules = { "ModulePipPart", "ModulePipEngine" };

        /// <summary>Puts the stored part's hardware modules in place of another node's (the same module, by name and order).</summary>
        public static void CarryHardware(Node from, Node to) => CarryModules(from, to, HardwareModules);

        /// <summary>Puts one part's modules of these names in place of another's (the same module, by name and order).</summary>
        public static void CarryModules(Node from, Node to, params string[] names)
        {
            foreach (string name in names)
            {
                List<Node> own = from.Children("MODULE").Where(x => x.Get("name") == name).ToList();
                int k = 0;
                for (int j = 0; j < to.Nodes.Count && k < own.Count; j++)
                {
                    Node d = to.Nodes[j];
                    if (d.Name != "MODULE" || d.Get("name") != name) continue;
                    to.Nodes[j] = own[k++].Clone();
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
    }
}
