using System.Collections.Generic;
using System.Linq;
using PhilsReusabilitySuite.Core;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// What a build asks when the parts bin holds parts that would fit but are older Phil's Iterative Parts Marks
    /// (Phil's design, play-test 2026-11-08 #5): update them and use them, or build those parts new. "No" has a
    /// confirm step that says so, and the old parts stay in the bin. Old-Mk parts never fly unless updated. Used by
    /// the VAB's Build panel, Space Center builds and Restore, so all three ask the same way.
    /// </summary>
    sealed class OldPartsQuestion
    {
        /// <summary>The build's bin parts to leave out (answered "No"): its quote is made without them.</summary>
        public readonly HashSet<string> Skip;
        bool confirming;
        string error;

        public OldPartsQuestion(HashSet<string> skip) { Skip = skip; }

        public void Reset() { confirming = false; error = null; }

        /// <summary>"Augur I Mk. 1 → Mk. 3, 2 × Whizzbang I Mk. 1 → Mk. 2".</summary>
        public static string List(IEnumerable<Owner> old) =>
            string.Join(", ", old.GroupBy(o => Pip.Change(o.Now, o.Current)).Select(g => (g.Count() > 1 ? g.Count() + " × " : "") + g.Key).ToArray());

        /// <summary>
        /// Draws the question when the quote found old-Mk bin parts, and returns true while it waits for an answer
        /// (the caller shows no Build button until then). Answering calls requote, then ends this GUI event.
        /// </summary>
        public bool Draw(Quote q, System.Action requote, float width)
        {
            if (q == null || q.OldBin.Count == 0) { confirming = false; return false; }
            int n = q.OldBin.Count;
            string fee = q.UpdatePrice < 0.5 ? "free" : Pricing.Funds(q.UpdatePrice);
            GUILayout.BeginVertical(Look.Notice);
            if (!confirming)
            {
                GUILayout.Label("<color=#ffcf5a>" + (n == 1 ? "A part in the parts bin fits" : n + " parts in the parts bin fit") + " once updated:</color> "
                                + List(q.OldBin) + ". " + (n == 1 ? "Its program has" : "Their programs have") + " moved on since " + (n == 1 ? "it was" : "they were")
                                + " built. Update " + (n == 1 ? "it" : "them") + " to the current Mk (" + fee + ") and use " + (n == 1 ? "it" : "them") + "?",
                                Look.Meta, GUILayout.Width(width));
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("No", Look.Ghost, GUILayout.Height(30), GUILayout.Width(80))) { confirming = true; error = null; GUIUtility.ExitGUI(); }
                GUILayout.Space(6);
                if (GUILayout.Button("Update and use · " + fee, Look.Go, GUILayout.Height(30), GUILayout.ExpandWidth(false)))
                {
                    PRSScenario sc = PRSScenario.Instance;
                    string err = "The suite's save data isn't loaded.";
                    if (sc != null && sc.UpdatePip(sc.BinPartsOf(q.OldBin), out err)) { error = null; requote(); }
                    else error = err;
                    GUIUtility.ExitGUI();
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label((n == 1 ? "This part will be built new; the old one stays in the bin." : "These " + n + " parts will be built new; the old ones stay in the bin.") + " Continue?",
                                Look.Meta, GUILayout.Width(width));
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Back", Look.Ghost, GUILayout.Height(30), GUILayout.Width(80))) { confirming = false; GUIUtility.ExitGUI(); }
                GUILayout.Space(6);
                if (GUILayout.Button(n == 1 ? "Build it new" : "Build them new", Look.Go, GUILayout.Height(30), GUILayout.ExpandWidth(false)))
                {
                    Skip.Add(PRSScenario.SkipOldParts);
                    Log.Info("Building " + Suite.Parts(n) + " new instead of updating old-Mk bin parts (" + List(q.OldBin) + ")");
                    confirming = false;
                    requote();
                    GUIUtility.ExitGUI();
                }
                GUILayout.EndHorizontal();
            }
            if (error != null) GUILayout.Label(error, Look.MetaBad);
            GUILayout.EndVertical();
            GUILayout.Space(6);
            return true;
        }
    }
}
