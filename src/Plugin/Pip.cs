using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PhilsReusabilitySuite.Core;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// Phil's Iterative Parts, when it's installed: each of its parts is a program's part at some version and Mk. One
    /// rule for that, wherever a build starts (Phil, 2026-11-08: "totally current with whatever the VAB can know and
    /// apply"): a new part is what the VAB would make now (the family's newest version at its current Mk); flown
    /// hardware keeps what it was built as until it's updated ("Update PIP parts"). PIP owns what a Mk is and the one
    /// step that brings a part up to date (PIP.PipApi.ToCurrent); the suite only calls it. Found by reflection:
    /// without PIP (or with an older one) every part counts as current and nothing changes.
    /// </summary>
    static class Pip
    {
        public delegate int BehindFn(Node part, out string now, out string current);

        /// <summary>How many Marks a part is behind its program's newest, with labels ("Augur I Mk. 1", "Augur I Mk. 3"); 0 when current or not PIP.</summary>
        public static BehindFn BehindOf;
        /// <summary>Brings a part up to date, in place (an old version's part is renamed to the newest's); true if it changed.</summary>
        public static Func<Node, bool> MakeCurrentOf;
        /// <summary>"Augur I Mk. 1" from the part's own data, or null when it isn't PIP.</summary>
        public static Func<Node, string> TitleOf;

        static bool looked;

        /// <summary>Looks for PIP's calls once, and tells it which editor parts are real hardware.</summary>
        public static void Find()
        {
            if (looked) return;
            looked = true;
            try
            {
                Type t = AssemblyLoader.loadedAssemblies.Select(a => a.assembly.GetType("PIP.PipApi", false)).FirstOrDefault(x => x != null);
                if (t == null) { Log.Info("Phil's Iterative Parts' API isn't installed: PIP parts are taken as they are"); return; }
                const BindingFlags Static = BindingFlags.Public | BindingFlags.Static;
                MethodInfo behind = t.GetMethod("Behind", Static, null, new[] { typeof(ConfigNode), typeof(string).MakeByRefType(), typeof(string).MakeByRefType() }, null);
                MethodInfo current = t.GetMethod("ToCurrent", Static, null, new[] { typeof(ConfigNode) }, null);
                MethodInfo title = t.GetMethod("Title", Static, null, new[] { typeof(ConfigNode) }, null);
                if (behind != null && behind.ReturnType == typeof(int))
                    BehindOf = (Node part, out string now, out string cur) =>
                    {
                        var args = new object[] { KspNodes.ToKsp(part, "PART"), null, null };
                        int n = (int)behind.Invoke(null, args);
                        now = args[1] as string;
                        cur = args[2] as string;
                        return n;
                    };
                if (current != null && current.ReturnType == typeof(int))
                    MakeCurrentOf = part =>
                    {
                        ConfigNode c = KspNodes.ToKsp(part, "PART");
                        if ((int)current.Invoke(null, new object[] { c }) <= 0) return false;
                        Node fresh = KspNodes.FromKsp(c);
                        part.Values.Clear(); part.Values.AddRange(fresh.Values);
                        part.Nodes.Clear(); part.Nodes.AddRange(fresh.Nodes);
                        return true;
                    };
                if (title != null && title.ReturnType == typeof(string))
                    TitleOf = part => title.Invoke(null, new object[] { KspNodes.ToKsp(part, "PART") }) as string;
                // PIP asks as the editor builds each part from its saved data (before the part exists, since an old
                // version's part would be renamed): hardware keeps what it is, a new part is made current.
                FieldInfo hook = t.GetField("IsHardware", Static);
                bool hooked = hook != null && hook.FieldType == typeof(Func<ConfigNode, bool>);
                if (hooked) hook.SetValue(null, (Func<ConfigNode, bool>)EditorAddon.IsHardwareNode);
                Log.Info("Linked with Phil's Iterative Parts: " + (BehindOf != null ? "Mk check, " : "") + (MakeCurrentOf != null ? "update, " : "")
                         + (TitleOf != null ? "titles, " : "") + (hooked ? "hardware keeps its Mk in the editor" : "no editor hook"));
            }
            catch (Exception ex) { Log.Warn("Couldn't link with Phil's Iterative Parts: " + ex.Message); }
        }

        /// <summary>What PIP says about one part, kept for a couple of seconds: windows ask every frame.</summary>
        sealed class MkInfo
        {
            public int Behind;
            public string Now, Current, Title;
            /// <summary>For a part that's behind: a copy of it brought up to date (what an update would make it).</summary>
            public Node Preview;
            public bool Previewed;
            public float At;
        }

        static readonly Dictionary<Node, MkInfo> cache = new Dictionary<Node, MkInfo>();
        static bool warned;

        static MkInfo Info(Node part)
        {
            float t = UnityEngine.Time.realtimeSinceStartup;
            if (cache.TryGetValue(part, out MkInfo had) && t - had.At < 2f) return had;
            if (cache.Count > 4000) cache.Clear();
            var info = new MkInfo { At = t };
            try
            {
                if (BehindOf != null) info.Behind = Math.Max(0, BehindOf(part, out info.Now, out info.Current));
                if (TitleOf != null) info.Title = TitleOf(part);
            }
            catch (Exception ex)
            {
                if (!warned) Log.Warn("Couldn't read a part's Mk from Phil's Iterative Parts: " + (ex.InnerException ?? ex).Message);
                warned = true;
                info.Behind = 0;
            }
            return cache[part] = info;
        }

        /// <summary>How many Marks the part is behind (0 when it's current, isn't PIP, or PIP isn't installed).</summary>
        public static int Behind(Node part, out string now, out string current)
        {
            now = current = null;
            if (BehindOf == null || part == null) return 0;
            MkInfo i = Info(part);
            now = i.Now;
            current = i.Current;
            return i.Behind;
        }

        public static int Behind(Node part) => Behind(part, out _, out _);

        /// <summary>
        /// What updating a part would make it (a copy; the part itself is unchanged), or the part itself when it isn't
        /// behind. An old version becomes the newest version's part, so matching and prices use this.
        /// </summary>
        public static Node Preview(Node part)
        {
            if (part == null || Behind(part) == 0 || MakeCurrentOf == null) return part;
            MkInfo i = Info(part);
            if (!i.Previewed)
            {
                i.Previewed = true;
                Node copy = part.Clone();
                try { i.Preview = MakeCurrentOf(copy) ? copy : null; }
                catch (Exception) { i.Preview = null; }
            }
            return i.Preview ?? part;
        }

        /// <summary>
        /// Brings the part up to date (what the VAB does to a new part). True if it changed. When it's in a craft, an old
        /// version's part is renamed, so the other parts' links to it are renamed too.
        /// </summary>
        public static bool MakeCurrent(Node part, Node craft = null)
        {
            if (MakeCurrentOf == null || part == null) return false;
            cache.Remove(part);
            string before = part.Get("part");
            bool changed;
            try { changed = MakeCurrentOf(part); }
            catch (Exception ex) { Log.Warn("Phil's Iterative Parts couldn't update a part: " + (ex.InnerException ?? ex).Message); return false; }
            string after = part.Get("part");
            if (changed && craft != null && !string.IsNullOrEmpty(before) && !string.IsNullOrEmpty(after) && before != after)
                foreach (Node other in craft.Children("PART"))
                    if (!ReferenceEquals(other, part)) Completion.RenameRef(other, before, after);
            return changed;
        }

        /// <summary>Every new part of a craft brought up to date: a Space Center build makes what the VAB would.</summary>
        public static int MakeCurrent(Node craft, Func<Node, bool> isNew)
        {
            int n = 0;
            foreach (Node part in craft.Children("PART").ToList()) if (isNew(part) && MakeCurrent(part, craft)) n++;
            return n;
        }

        /// <summary>"Augur I Mk. 1 → Mk. 3": what an old part is and what an update makes it.</summary>
        public static string Change(string now, string current)
        {
            if (string.IsNullOrEmpty(now)) return current ?? "";
            if (string.IsNullOrEmpty(current)) return now;
            int a = now.LastIndexOf(" Mk", StringComparison.Ordinal), b = current.LastIndexOf(" Mk", StringComparison.Ordinal);
            if (a > 0 && b > 0 && now.Substring(0, a) == current.Substring(0, b)) return now + " → " + current.Substring(b + 1);
            return now + " → " + current;
        }

        /// <summary>
        /// Gives a live editor part a stored part's hardware modules (its own program and Mk): "Use it" turns new parts
        /// into a hangar copy's hardware. PIP leaves an assigned part's data as it's loaded.
        /// </summary>
        public static void LoadHardware(Part part, Node stored)
        {
            if (part == null || stored == null) return;
            foreach (string name in Completion.HardwareModules)
            {
                var from = stored.Children("MODULE").Where(m => m.Get("name") == name).ToList();
                var live = part.Modules.Cast<PartModule>().Where(m => m.moduleName == name).ToList();
                for (int i = 0; i < Math.Min(from.Count, live.Count); i++)
                {
                    try { live[i].Load(KspNodes.ToKsp(from[i], "MODULE")); }
                    catch (Exception ex) { Log.Warn("Couldn't give " + part.partInfo?.title + " its own " + name + " data: " + ex.Message); }
                }
            }
        }

        /// <summary>The part's own program and Mk ("Augur I Mk. 1"), or null when it isn't PIP.</summary>
        public static string Title(Node part) => TitleOf == null || part == null ? null : Info(part).Title;

        /// <summary>A part node's part name, without the craft's id ("PIP.x" from "PIP.x_4294").</summary>
        public static string PartName(Node part)
        {
            string r = part?.Get("part") ?? part?.Get("name") ?? "";
            return r.Contains("_") ? r.Substring(0, r.LastIndexOf('_')) : r;
        }

        /// <summary>A part's name for the suite's lists: its own PIP program and Mk, or KSP's title.</summary>
        public static string NameOf(Node part)
        {
            string t = Title(part);
            if (!string.IsNullOrEmpty(t)) return t;
            string name = PartName(part);
            AvailablePart ap = PartLoader.getPartInfoByName(name);
            return ap != null ? ap.title : name;
        }
    }
}
