using System;
using System.Linq;
using System.Reflection;
using PhilsReusabilitySuite.Core;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// Phil's Iterative Parts, when it's installed: each of its parts is a program's part at some Mk. One rule for
    /// that Mk, wherever a build starts (Phil, 2026-11-08: "totally current with whatever the VAB can know and
    /// apply"): a new part is the program's current Mk, as the VAB makes it; flown hardware keeps the Mk it was
    /// built at until it's updated ("Update PIP parts"). PIP owns what a Mk is and the step that brings a part to
    /// the current one; the suite only calls it. Found by reflection: without PIP (or with an older one) every
    /// part counts as current and nothing changes.
    /// </summary>
    static class Pip
    {
        public delegate int BehindFn(Node part, out string now, out string current);

        /// <summary>How many Marks a part is behind its program, with labels ("Augur I Mk. 1", "Augur I Mk. 3"); 0 when current or not PIP.</summary>
        public static BehindFn BehindOf;
        /// <summary>Brings a part to its program's current Mk, in place; true if anything changed.</summary>
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
                MethodInfo current = t.GetMethod("MakeCurrent", Static, null, new[] { typeof(ConfigNode) }, null);
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
                if (current != null && current.ReturnType == typeof(bool))
                    MakeCurrentOf = part =>
                    {
                        ConfigNode c = KspNodes.ToKsp(part, "PART");
                        if (!(bool)current.Invoke(null, new object[] { c })) return false;
                        Node fresh = KspNodes.FromKsp(c);
                        part.Values.Clear(); part.Values.AddRange(fresh.Values);
                        part.Nodes.Clear(); part.Nodes.AddRange(fresh.Nodes);
                        return true;
                    };
                if (title != null && title.ReturnType == typeof(string))
                    TitleOf = part => title.Invoke(null, new object[] { KspNodes.ToKsp(part, "PART") }) as string;
                FieldInfo hook = t.GetField("IsHardware", Static);
                if (hook != null && hook.FieldType == typeof(Func<Part, bool>)) hook.SetValue(null, (Func<Part, bool>)EditorAddon.IsHardware);
                Log.Info("Linked with Phil's Iterative Parts: " + (BehindOf != null ? "Mk check, " : "") + (MakeCurrentOf != null ? "update, " : "")
                         + (TitleOf != null ? "titles, " : "") + (hook != null ? "hardware keeps its Mk in the editor" : "no editor hook"));
            }
            catch (Exception ex) { Log.Warn("Couldn't link with Phil's Iterative Parts: " + ex.Message); }
        }

        /// <summary>What PIP says about one part, kept for a couple of seconds: windows ask every frame.</summary>
        sealed class MkInfo
        {
            public int Behind;
            public string Now, Current, Title;
            public float At;
        }

        static readonly System.Collections.Generic.Dictionary<Node, MkInfo> cache = new System.Collections.Generic.Dictionary<Node, MkInfo>();
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

        /// <summary>Brings the part to its program's current Mk (what the VAB does to a new part). True if it changed.</summary>
        public static bool MakeCurrent(Node part)
        {
            if (MakeCurrentOf == null || part == null) return false;
            cache.Remove(part);
            try { return MakeCurrentOf(part); }
            catch (Exception ex) { Log.Warn("Phil's Iterative Parts couldn't update a part: " + (ex.InnerException ?? ex).Message); return false; }
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

        /// <summary>Every new part of a craft at its program's current Mk: a Space Center build makes what the VAB would.</summary>
        public static int MakeCurrent(Node craft, Func<Node, bool> isNew)
        {
            int n = 0;
            foreach (Node part in craft.Children("PART").ToList()) if (isNew(part) && MakeCurrent(part)) n++;
            return n;
        }

        /// <summary>The part's own program and Mk ("Augur I Mk. 1"), or null when it isn't PIP.</summary>
        public static string Title(Node part) => TitleOf == null || part == null ? null : Info(part).Title;

        /// <summary>A part's name for the suite's lists: its own PIP program and Mk, or KSP's title.</summary>
        public static string NameOf(Node part)
        {
            string t = Title(part);
            if (!string.IsNullOrEmpty(t)) return t;
            string r = part?.Get("part") ?? part?.Get("name") ?? "";
            string name = r.Contains("_") ? r.Substring(0, r.LastIndexOf('_')) : r;
            AvailablePart ap = PartLoader.getPartInfoByName(name);
            return ap != null ? ap.title : name;
        }
    }
}
