using System;
using System.Collections.Generic;
using System.Linq;
using KSP.UI.Screens;
using PhilsReusabilitySuite.Core;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>Opens the suite window at the Space Center (the editors open it themselves).</summary>
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public class SpaceCenterAddon : MonoBehaviour
    {
        static bool warned;

        void Start()
        {
            Look.Init();
            gameObject.AddComponent<SuiteWindow>();
            if (warned) return;
            warned = true;
            if (AssemblyLoader.loadedAssemblies.Any(a => a.name == "ScrapYard"))
            {
                Log.Warn("ScrapYard is installed alongside the suite: both act on the same recoveries, launches and funds");
                Suite.Message("ScrapYard is installed: it handles the same recoveries and launches as Phil's Reusability Suite. Remove one of them.", 12f);
            }
            if (StageReturns.StageRecoveryInstalled)
                Suite.Message("StageRecovery is installed, so Phil's Reusability Suite leaves stage recovery to it. Remove StageRecovery to have stages come home to the hangar.", 12f);
        }
    }

    /// <summary>The suite window: Build queue, Hangar and Parts bin tabs, from a toolbar button.</summary>
    public partial class SuiteWindow : MonoBehaviour
    {
        public static SuiteWindow Instance { get; private set; }

        ApplicationLauncherButton button;
        public bool Show;
        public int Tab;
        Rect rect = new Rect(160, 90, 960, 620);
        Vector2 scrollMain, scrollSide;
        string error;
        string selectedId;

        static Core.Settings S => Suite.Settings;
        static bool InEditor => HighLogic.LoadedSceneIsEditor;

        void Start()
        {
            Instance = this;
            Look.Init();
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            if (ApplicationLauncher.Ready) AddButton();
        }

        void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            if (button != null && ApplicationLauncher.Instance != null) ApplicationLauncher.Instance.RemoveModApplication(button);
            InputLockManager.RemoveControlLock("PRSSuiteWindow");
            if (Instance == this) Instance = null;
        }

        void AddButton()
        {
            if (button != null || ApplicationLauncher.Instance == null) return;
            button = ApplicationLauncher.Instance.AddModApplication(() => Show = true, () => Show = false, null, null, null, null,
                ApplicationLauncher.AppScenes.SPACECENTER | ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH, Look.Icon());
        }

        void OnGUI()
        {
            if (!Show) { InputLockManager.RemoveControlLock("PRSSuiteWindow"); return; }
            GUI.skin = Look.Skin;
            if (TestOffset.HasValue) rect.position = Event.current.mousePosition + TestOffset.Value;
            rect = GUI.Window(GetInstanceID(), rect, Draw, GUIContent.none, Look.Window);
            // Phil: scrolling over a suite window scrolls only the window. While the mouse is over it, the scene under it and
            // its camera (the scroll-wheel zoom) are held.
            if (rect.Contains(Event.current.mousePosition)) InputLockManager.SetControlLock((InEditor ? ControlTypes.EDITOR_LOCK : ControlTypes.KSC_ALL) | ControlTypes.CAMERACONTROLS, "PRSSuiteWindow");
            else InputLockManager.RemoveControlLock("PRSSuiteWindow");
        }

        /// <summary>Self-test only: keeps the window's corner this far from the mouse (negative: under it, positive: off it).</summary>
        internal Vector2? TestOffset;

        /// <summary>The window header: the suite's name or a title, something on the right, and a close button.</summary>
        public static void HeaderBar(string title, string right, Action close, Action tabs = null)
        {
            GUILayout.BeginHorizontal(Look.Header, GUILayout.Height(50));
            if (title == null)
            {
                GUILayout.Label("Phil's", Look.TitleMuted, GUILayout.ExpandWidth(false), GUILayout.Height(50));
                GUILayout.Space(6);
                GUILayout.Label("Reusability Suite", Look.Title, GUILayout.ExpandWidth(false), GUILayout.Height(50));
            }
            else GUILayout.Label(title, Look.TitleMuted, GUILayout.ExpandWidth(false), GUILayout.Height(50));
            GUILayout.Space(14);
            tabs?.Invoke();
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(right)) GUILayout.Label(right, Look.Eyebrow, GUILayout.ExpandWidth(false), GUILayout.Height(50));
            GUILayout.Space(8);
            GUILayout.BeginVertical(); GUILayout.Space(10);
            if (GUILayout.Button("×", Look.Close, GUILayout.Width(30), GUILayout.Height(30))) close();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        void Draw(int id)
        {
            PRSScenario sc = PRSScenario.Instance;
            GUILayout.BeginVertical();
            HeaderBar(null, Planetarium.fetch != null ? KSPUtil.PrintDateCompact(Planetarium.GetUniversalTime(), true) : "", () => { Show = false; if (button != null) button.SetFalse(false); }, () =>
            {
                if (sc == null) return;
                TabButton(0, "Build queue", sc.Jobs.Count);
                TabButton(1, "Hangar", sc.Hangar.Count);
                TabButton(2, "Parts bin", sc.Bin.Sum(k => k.Count));
            });
            if (sc == null) { GUILayout.Label("The suite's save data isn't loaded in this scene.", Look.Empty, GUILayout.ExpandHeight(true)); GUILayout.EndVertical(); GUI.DragWindow(); return; }
            if (error != null) { GUILayout.Space(6); GUILayout.BeginHorizontal(); GUILayout.Space(16); GUILayout.Label(error, Look.MetaBad); GUILayout.EndHorizontal(); }
            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            if (crewShip != null) CrewStep(sc);
            else if (Tab == 0) QueueTab(sc);
            else if (Tab == 1) HangarTab(sc);
            else BinTab(sc);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 50));
        }

        void TabButton(int i, string label, int count)
        {
            GUILayout.BeginVertical(); GUILayout.Space(10);
            if (GUILayout.Button(label + "   <color=#ffa62b><b>" + count + "</b></color>", Tab == i ? Look.TabOn : Look.Tab, GUILayout.Height(30), GUILayout.ExpandWidth(false)))
            {
                Tab = i;
                error = null;
                picking = false;
                library = null;
                crewShip = null;
                GUIUtility.ExitGUI();   // what the window holds changed: end this event, draw afresh next frame
            }
            GUILayout.EndVertical();
            GUILayout.Space(4);
        }

        // ---- Build queue ----

        void QueueTab(PRSScenario sc)
        {
            scrollMain = GUILayout.BeginScrollView(scrollMain, GUILayout.ExpandWidth(true));
            GUILayout.BeginVertical(); GUILayout.Space(10);
            List<BuildJob> active = sc.ActiveJobs();
            if (picking)
            {
                // Priced once, at the start of a frame (Layout), so every event in the frame draws the same list.
                if (library == null && Event.current.type == EventType.Layout) LoadLibrary();
                if (library != null) LibraryList(sc);
            }
            else
            {
                foreach (string f in new[] { "VAB", "SPH" })
                {
                    int lines = Suite.Lines(f);
                    GUILayout.BeginHorizontal(); GUILayout.Space(16);
                    GUILayout.Label("<b>" + f + "</b>   <color=#9ba7c0>" + lines + (lines == 1 ? " build line" : " build lines") + "</color>", Look.H2Muted);
                    GUILayout.EndHorizontal();
                    List<BuildJob> jobs = sc.Jobs.Where(j => j.Facility == f).ToList();
                    if (jobs.Count == 0) Indent(() => GUILayout.Label("Nothing being built.", Look.Hint));
                    for (int i = 0; i < jobs.Count; i++) Indent(() => JobCard(sc, jobs[i], active.Contains(jobs[i]) ? "Line " + (active.Where(j => j.Facility == f).ToList().IndexOf(jobs[i]) + 1) : "Waiting", i > 0));
                    GUILayout.Space(8);
                }
                Indent(() =>
                {
                    if (GUILayout.Button("Build from Phil's Builds…", Look.Ghost, GUILayout.Height(32), GUILayout.ExpandWidth(false))) { OpenLibrary(); GUIUtility.ExitGUI(); }
                    GUILayout.Label("Queue a filed launcher or craft without opening the VAB or SPH.", Look.Hint);
                });
            }
            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            GUILayout.BeginVertical(Look.Notice, GUILayout.Width(300), GUILayout.ExpandHeight(true));
            GUILayout.Label("READY TO LAUNCH", Look.Section);
            List<HangarShip> ready = sc.Hangar.Where(h => h.Ready && h.ReservedBy == null).ToList();
            if (ready.Count == 0) GUILayout.Label("Nothing yet. Finished builds wait here and in the hangar.", Look.Hint);
            scrollSide = GUILayout.BeginScrollView(scrollSide);
            foreach (HangarShip h in ready)
            {
                GUILayout.BeginVertical(Look.CardReuse);
                GUILayout.Label(h.DisplayName, Look.Name);
                string under = BuildsLink.Under(h);
                if (under != null) GUILayout.Label("on " + under, Look.Meta);
                GUILayout.Label(Suite.FacilityOf(h.Craft) + " · " + Suite.Parts(h.PartCount) + (h.Flights > 0 ? " · " + h.Flights + (h.Flights == 1 ? " flight" : " flights") : " · never flown") + (h.Wear >= 0.005 ? " · wear " + Core.Wear.Percent(h.Wear) : ""), Look.Meta);
                if (GUILayout.Button("Launch", Look.Go, GUILayout.Height(32))) Launch(sc, h);
                GUILayout.EndVertical();
                GUILayout.Space(6);
            }
            GUILayout.EndScrollView();
            GUILayout.FlexibleSpace();
            if (!InEditor && active.Count > 0 && GUILayout.Button("Warp to next finish", Look.Ghost, GUILayout.Height(32))) sc.WarpToNextFinish();
            GUILayout.Label("Upgrade the VAB or SPH for more build lines. No points, no upgrade times.", Look.Hint);
            GUILayout.EndVertical();
        }

        // ---- Building from Phil's Builds' library, without the editor ----

        bool picking;
        BuildPanel.Pace pickPace = BuildPanel.Pace.Normal;
        List<BuildsLink.LibraryBuild> library;

        /// <summary>Each build's question about old-Mk bin parts ("lib:" + design, "restore:" + ship), with its answers.</summary>
        readonly Dictionary<string, OldPartsQuestion> questions = new Dictionary<string, OldPartsQuestion>();

        OldPartsQuestion QuestionFor(string key)
        {
            if (!questions.TryGetValue(key, out OldPartsQuestion q)) questions[key] = q = new OldPartsQuestion(new HashSet<string>());
            return q;
        }

        /// <summary>The library, priced at the start of a frame, with any "build them new" answers already given.</summary>
        void LoadLibrary()
        {
            library = BuildsLink.Library();
            foreach (BuildsLink.LibraryBuild item in library)
            {
                OldPartsQuestion q = QuestionFor("lib:" + item.Design.Id);
                item.Skip = q.Skip;
                if (q.Skip.Count > 0) item.Requote();
            }
        }

        /// <summary>Opens the window on the Hangar tab with this ship picked.</summary>
        public void ShowHangarShip(string id)
        {
            Tab = 1;
            picking = false;
            selectedId = id;
            restoreFor = null;
            error = null;
            Show = true;
        }

        public void OpenLibrary()
        {
            Tab = 0;
            picking = true;
            library = null;
            error = null;
            scrollMain = Vector2.zero;
            questions.Clear();
        }

        void LibraryList(PRSScenario sc)
        {
            Indent(() =>
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("<b>Build from Phil's Builds</b>", Look.H2Muted);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Back to the queue", Look.Ghost, GUILayout.Height(28), GUILayout.ExpandWidth(false))) { picking = false; library = null; GUIUtility.ExitGUI(); }
                GUILayout.EndHorizontal();
                GUILayout.Label("Each is built new, from its design file. Matching parts in the parts bin are used automatically: they're already paid for.", Look.Hint);
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                PaceButton(BuildPanel.Pace.Normal, "Normal");
                PaceButton(BuildPanel.Pace.Overtime, "Overtime");
                PaceButton(BuildPanel.Pace.Outsource, "Outsource");
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
            });
            if (library.Count == 0) Indent(() => GUILayout.Label("Nothing is filed yet. File craft with Phil's Builds' Save panel in the VAB or SPH.", Look.Hint));
            foreach (BuildsLink.LibraryBuild item in library.ToList()) Indent(() => LibraryCard(item));
        }

        void PaceButton(BuildPanel.Pace p, string label)
        {
            if (GUILayout.Button(label, pickPace == p ? Look.SegOn : Look.Seg, GUILayout.Height(30), GUILayout.Width(120))) pickPace = p;
        }

        void LibraryCard(BuildsLink.LibraryBuild item)
        {
            Quote q = item.Quote;
            bool overtime = pickPace == BuildPanel.Pace.Overtime, outsource = pickPace == BuildPanel.Pace.Outsource;
            double extra = outsource ? Pricing.OutsourceCharge(q.BuildPrice, 1, S) : overtime ? Pricing.OvertimeCharge(q.BuildPrice, 1, S) : 0;
            double seconds = outsource ? 0 : overtime ? q.Seconds / S.OvertimeSpeed : q.Seconds;
            GUILayout.BeginHorizontal(Look.Card);
            if (BuildsLink.HasPictures) { Look.Picture(BuildsLink.Picture(item.Design.Id), 52, 46); GUILayout.Space(10); }
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label(item.Design.Name, Look.Name, GUILayout.ExpandWidth(false));
            GUILayout.Space(6);
            if (q.BinParts > 0) { Look.Chip("Parts from the bin", Look.ChipReuse); GUILayout.Space(4); }
            if (item.Ready > 0) Look.Chip(item.Ready + " ready in the hangar", Look.ChipGrey);
            GUILayout.FlexibleSpace();
            GUILayout.Label(Pricing.Funds(q.Total + extra) + " · " + (seconds <= 0 ? "ready now" : Pricing.Duration(seconds, S)), Look.Name, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
            // Which parts the bin gives, and what a worn one costs to refurbish (Phil didn't know it had used them).
            QuoteLine bin = q.Lines.FirstOrDefault(l => l.Kind == OwnerKind.Bin);
            if (bin != null) GUILayout.Label("From the parts bin: " + string.Join(", ", BuildPanel.BinItems(bin).ToArray()), Look.Meta);
            bool asking = QuestionFor("lib:" + item.Design.Id).Draw(q, item.Requote, 560);
            GUILayout.BeginHorizontal();
            GUILayout.Label(item.KindLabel + " · " + Suite.Parts(item.Parts) + (item.Blocked != null ? "\n<color=#ff8a7a>" + item.Blocked + "</color>" : ""), Look.Meta);
            GUILayout.FlexibleSpace();
            bool can = item.Blocked == null && !asking;
            if (GUILayout.Button(asking ? "Answer above" : can ? "Build" : "Can't build", can ? Look.Go : Look.GoOff, GUILayout.Height(30), GUILayout.Width(120)) && can)
            {
                BuildJob job = BuildsLink.BuildFromLibrary(item, overtime, outsource, out string err);
                if (job == null) error = err;
                else { error = null; picking = false; }
                library = null;   // fresh serials and prices next time
                GUIUtility.ExitGUI();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

        static void Indent(Action body)
        {
            GUILayout.BeginHorizontal(); GUILayout.Space(16); GUILayout.BeginVertical(); body(); GUILayout.EndVertical(); GUILayout.Space(10); GUILayout.EndHorizontal();
        }

        void JobCard(PRSScenario sc, BuildJob j, string lineLabel, bool canMoveUp)
        {
            bool waiting = lineLabel == "Waiting";
            GUILayout.BeginHorizontal(Look.Card);
            if (BuildsLink.HasPictures) { Look.Picture(BuildsLink.Picture(BuildsLink.TopDesign(j.Craft)), 64, 64); GUILayout.Space(10); }
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            Look.Chip(lineLabel, Look.ChipGrey);
            GUILayout.Space(6);
            GUILayout.Label(sc.TitleOf(j), Look.Name, GUILayout.ExpandWidth(false));
            GUILayout.Space(6);
            Ship kept = sc.KeptShipOf(j);
            if (kept != null) Look.Chip(j.NewPrice >= 0.5 || j.BinTaken.Count > 0 ? "Refit" : "Refurbish", Look.ChipReuse);
            else if (j.RefurbishPrice > 0 || j.BinTaken.Count > 0) Look.Chip("Reused parts", Look.ChipReuse);
            else Look.Chip("New", Look.ChipNew);
            if (j.Overtime) { GUILayout.Space(4); Look.Chip("Overtime on", Look.ChipWarn); }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (!waiting)
            {
                Look.ProgressBar((float)j.Progress, 260);
                GUILayout.Space(10);
                double left = j.SecondsLeft(S);
                GUILayout.Label(Mathf.RoundToInt((float)(j.Progress * 100)) + "% · " + Pricing.Duration(left, S) + " left · ready " + KSPUtil.PrintDateCompact(Planetarium.GetUniversalTime() + left, true), Look.Meta);
            }
            else GUILayout.Label(Pricing.Duration(j.SecondsLeft(S), S) + " of work · starts when a " + j.Facility + " line is free", Look.Meta);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            string err;
            if (j.Overtime)
            {
                if (GUILayout.Button("Overtime on", Look.GhostOn, GUILayout.Height(28))) { if (!sc.SetOvertime(j, false, out err)) error = err; else error = null; }
            }
            else if (GUILayout.Button("Overtime · +" + Pricing.Funds(Pricing.OvertimeCharge(j.BuildPrice, 1 - j.Progress, S)), Look.Ghost, GUILayout.Height(28)))
            { if (!sc.SetOvertime(j, true, out err)) error = err; else error = null; }
            if (GUILayout.Button("Outsource · +" + Pricing.Funds(Pricing.OutsourceCharge(j.BuildPrice, 1 - j.Progress, S)), Look.Ghost, GUILayout.Height(28)))
            { if (!sc.Outsource(j, out err)) error = err; else error = null; }
            GUILayout.FlexibleSpace();
            if (canMoveUp && GUILayout.Button("Move up", Look.Ghost, GUILayout.Height(28))) sc.MoveUp(j);
            if (InEditor && GUILayout.Button("Edit", Look.Ghost, GUILayout.Height(28)))
            {
                EditorAddon.LoadBuild(j);
                Show = false;
                if (button != null) button.SetFalse(false);
            }
            if (GUILayout.Button("Cancel", Look.Ghost, GUILayout.Height(28))) { sc.Cancel(j); error = null; }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
        }

        /// <summary>Launch: a ship with seats goes through the crew step first (Phil: pick the crew before launch).</summary>
        void Launch(PRSScenario sc, HangarShip h)
        {
            if (!StartLaunch(h)) return;
            if (crewShip == null) Show = false;
            GUIUtility.ExitGUI();
        }

        // ---- Hangar ----

        void HangarTab(PRSScenario sc)
        {
            scrollMain = GUILayout.BeginScrollView(scrollMain, GUILayout.ExpandWidth(true));
            GUILayout.BeginVertical(); GUILayout.Space(10);
            if (sc.Hangar.Count == 0) Indent(() => GUILayout.Label("The hangar is empty. Recovered ships and finished builds come here.", Look.Hint));
            foreach (string group in new[] { "Ready to fly", "Stored", "In a build" })
            {
                List<HangarShip> ships = sc.Hangar.Where(h => GroupOf(h) == group).ToList();
                if (ships.Count == 0) continue;
                Indent(() => GUILayout.Label(group.ToUpperInvariant(), Look.Section));
                foreach (HangarShip h in ships) Indent(() => ShipRow(sc, h));
            }
            GUILayout.EndVertical();
            GUILayout.EndScrollView();

            GUILayout.BeginVertical(Look.Notice, GUILayout.Width(316), GUILayout.ExpandHeight(true));
            HangarShip sel = sc.Hangar.FirstOrDefault(h => h.Id == selectedId);
            if (sel == null) GUILayout.Label("Pick a ship to see what its next flight costs.", Look.Hint);
            else ShipDetail(sc, sel);
            GUILayout.EndVertical();
        }

        // Restore (from its own blueprint), worked out for the picked ship at most every two seconds (not every frame).
        BuildsLink.Restore restore;
        string restoreFor;
        float restoreAt;

        BuildsLink.Restore RestoreFor(HangarShip h)
        {
            // A fuelled ship can still be short of parts (refuelled to fly as it is), so only a build stops a restore.
            if (h.ReservedBy != null) return null;
            // Worked out only at the start of a frame (Layout), so the panel's shape can't change between its events.
            if (Event.current.type == EventType.Layout && (restoreFor != h.Id || Time.realtimeSinceStartup - restoreAt > 2f))
            {
                restoreFor = h.Id;
                restoreAt = Time.realtimeSinceStartup;
                try { restore = BuildsLink.RestoreOf(h, QuestionFor("restore:" + h.Id).Skip); }
                catch (Exception ex) { restore = null; Log.Warn("Couldn't work out restoring " + h.DisplayName + ": " + ex.Message); }
            }
            return restoreFor == h.Id ? restore : null;
        }

        bool restoreAsking;

        static string GroupOf(HangarShip h) => h.ReservedBy != null ? "In a build" : h.Ready ? "Ready to fly" : "Stored";

        /// <summary>
        /// A hangar ship's Phil's Iterative Parts parts that are behind their program's current Mk, and "Update PIP
        /// parts" (Phil's design, play-test 2026-11-08 #5). Flown hardware keeps the Mk it was built at until then.
        /// </summary>
        void PipSection(PRSScenario sc, HangarShip h)
        {
            string list = BehindList(h);
            if (list == null) return;
            double price = sc.PipUpdatePrice(h.Craft.Children("PART"), out int n);
            GUILayout.Space(8);
            GUILayout.Label("PIP PARTS · " + n + " BEHIND", Look.Section);
            // Phil: a ship's own old parts can fly as they are.
            GUILayout.Label(list + ". " + (n == 1 ? "Its program has" : "Their programs have") + " moved on since " + (n == 1 ? "it was" : "they were")
                            + " built. It can fly with " + (n == 1 ? "it" : "them") + " as " + (n == 1 ? "it is" : "they are") + ".", Look.Meta);
            if (GUILayout.Button("Update PIP parts · " + (price < 0.5 ? "free" : Pricing.Funds(price)), Look.Ghost, GUILayout.Height(30))) UpdateShip(sc, h, n);
        }

        /// <summary>"Augur I Mk. 1 → Mk. 3, 2 × Hertz I Mk. 1 → Mk. 3" for a hangar ship's parts that are behind, or null.</summary>
        static string BehindList(HangarShip h)
        {
            var behind = new List<string>();
            foreach (Node p in h.Craft.Children("PART"))
                if (Pip.Behind(p, out string now, out string current) > 0) behind.Add(Pip.Change(now, current));
            if (behind.Count == 0) return null;
            return string.Join(", ", behind.GroupBy(x => x).Select(g => (g.Count() > 1 ? g.Count() + " × " : "") + g.Key).ToArray());
        }

        void UpdateShip(PRSScenario sc, HangarShip h, int n)
        {
            if (!sc.UpdatePip(h, out string err)) error = err;
            else { error = null; Suite.Message(h.DisplayName + ": " + Suite.Parts(n) + " updated to the current Mk"); }
            restoreFor = null;
            GUIUtility.ExitGUI();
        }

        /// <summary>Hangar ships whose own old-Mk parts the player chose to keep when restoring ("No").</summary>
        readonly HashSet<string> keepOldParts = new HashSet<string>();

        /// <summary>
        /// Restore asks about the ship's own old-Mk parts first (Phil: "update its old parts too?"; No keeps them, and it
        /// flies with them). True while it waits for an answer.
        /// </summary>
        bool AskOwnOldParts(PRSScenario sc, HangarShip h)
        {
            if (keepOldParts.Contains(h.Id)) return false;
            string list = BehindList(h);
            if (list == null) return false;
            double price = sc.PipUpdatePrice(h.Craft.Children("PART"), out int n);
            string fee = price < 0.5 ? "free" : Pricing.Funds(price);
            GUILayout.BeginVertical(Look.Notice);
            GUILayout.Label("<color=#ffcf5a>" + h.DisplayName + " has " + (n == 1 ? "a PIP part" : n + " PIP parts") + " behind:</color> " + list
                            + ". Update " + (n == 1 ? "it" : "them") + " too (" + fee + "), or restore it with " + (n == 1 ? "it" : "them") + " as " + (n == 1 ? "it is" : "they are") + "?",
                            Look.Meta, GUILayout.Width(270));
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("No, keep", Look.Ghost, GUILayout.Height(30), GUILayout.ExpandWidth(false))) { keepOldParts.Add(h.Id); GUIUtility.ExitGUI(); }
            GUILayout.Space(6);
            if (GUILayout.Button("Update too · " + fee, Look.Go, GUILayout.Height(30), GUILayout.ExpandWidth(false))) UpdateShip(sc, h, n);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(6);
            return true;
        }

        void ShipRow(PRSScenario sc, HangarShip h)
        {
            bool sel = h.Id == selectedId;
            GUILayout.BeginHorizontal(sel ? Look.CardSel : Look.Card);
            if (BuildsLink.HasPictures) { Look.Picture(BuildsLink.Picture(h.Ship.DesignId ?? BuildsLink.TopDesign(h.Craft)), 52, 46, h.Missing > 0); GUILayout.Space(10); }
            GUILayout.BeginVertical();
            GUILayout.Label(h.DisplayName, Look.Name);
            string under = BuildsLink.Under(h);
            if (under != null) GUILayout.Label("on " + under, Look.Meta);
            string meta = Suite.Parts(h.PartCount) + " · " + (h.Flights == 0 ? "never flown" : h.Flights + (h.Flights == 1 ? " flight" : " flights")) + (h.Flights > 0 || h.Wear >= 0.005 ? " · wear " + Core.Wear.Percent(h.Wear) : "");
            // Against what it launched with: a booster loaded to 30% on purpose that brings all of it back is full.
            if (!h.Ready) meta += " · fuel " + Mathf.RoundToInt((float)(h.Fuel * 100)) + "% of launch load";
            GUILayout.Label(meta, Look.Meta);
            if (h.Missing > 0) GUILayout.Label("Missing " + Suite.Parts(h.Missing), Look.MetaWarn);
            if (Core.Wear.WornOut(h.Wear)) GUILayout.Label("Worn out: refurbish it before it flies again", Look.MetaBad);
            int behind = h.Craft.Children("PART").Count(p => Pip.Behind(p) > 0);
            if (behind > 0) GUILayout.Label((behind == 1 ? "1 PIP part" : behind + " PIP parts") + " behind the current Mk", Look.MetaWarn);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (h.ReservedBy != null) GUILayout.Label("In the build of " + (sc.Jobs.FirstOrDefault(j => j.Id == h.ReservedBy) is BuildJob job ? sc.TitleOf(job) : "a ship"), Look.Meta, GUILayout.Width(200));
            else if (h.Ready) GUILayout.Label("Ready", Look.MetaGood, GUILayout.Width(60));
            GUILayout.EndHorizontal();
            if (Event.current.type == EventType.MouseUp && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) { selectedId = h.Id; error = null; scrollDetail = Vector2.zero; Event.current.Use(); }
            GUILayout.Space(5);
        }

        Vector2 scrollDetail;

        void ShipDetail(PRSScenario sc, HangarShip h)
        {
            GUILayout.Label(GroupOf(h).ToUpperInvariant(), Look.Section);
            GUILayout.Label(h.DisplayName, Look.H2);
            string under = BuildsLink.Under(h);
            if (under != null) GUILayout.Label("on " + under, Look.Meta);
            GUILayout.Label(Suite.Parts(h.PartCount) + " · " + (h.Flights == 0 ? "never flown" : h.Flights + (h.Flights == 1 ? " flight" : " flights")) + (h.Missing > 0 ? " · missing " + h.Missing : ""), Look.Meta);
            string top = BuildsLink.HasPictures ? h.Ship.DesignId ?? BuildsLink.TopDesign(h.Craft) : null, below = BuildsLink.HasPictures ? BuildsLink.LauncherDesign(h.Craft) : null;
            Texture2D topPic = BuildsLink.Picture(top), belowPic = BuildsLink.Picture(below);
            if (topPic != null || belowPic != null)
            {
                // Its design's picture; for a stack, the launcher it's on beside it. No picture yet: no empty box.
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                Look.Picture(topPic, below != null ? 140 : 284, 110, h.Missing > 0);
                if (below != null) { GUILayout.Space(4); Look.Picture(belowPic, 140, 110); }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(8);
            if (h.ReservedBy == null)
            {
                bool worn = Core.Wear.WornOut(h.Wear);
                Quote q = sc.QuoteFor(h);
                bool canRefurbish = q.RefurbishPrice >= 0.5 || q.FuelPrice >= 0.5;
                BuildsLink.Restore r = RestoreFor(h);
                // Prices and the flight log scroll; the buttons stay at the bottom, however long the log gets.
                scrollDetail = GUILayout.BeginScrollView(scrollDetail, GUILayout.ExpandHeight(true));
                GUILayout.Label("WEAR · " + Core.Wear.Percent(h.Wear), Look.Section);
                GUILayout.Label(worn ? "<color=#ff8a7a>Worn out: it must be refurbished before it flies again.</color>"
                                : h.Wear < 0.005 ? "Like new." : "From how hard its flights were: speed, heat and G. Refurbishing takes it back to 0%; it's optional until 100%.", Look.Meta);
                PipSection(sc, h);
                GUILayout.Space(8);
                GUILayout.Label("LAUNCH AS IT IS", Look.Section);
                if (worn) Line("Launch", "refurbish first");
                else Line(h.Ready ? "Fuelled · launch" : "Fuel back to launch levels", h.Ready ? "free" : Pricing.Funds(q.FuelPrice));
                if (canRefurbish)
                {
                    GUILayout.Space(8);
                    GUILayout.Label("REFURBISH TO 0% WEAR", Look.Section);
                    Line("Refurbish", Pricing.Funds(q.RefurbishPrice));
                    Line("Fuel", Pricing.Funds(q.FuelPrice));
                    Line("Total · time", Pricing.Funds(q.Total) + " · " + Pricing.Duration(q.Seconds, S));
                }
                if (r != null)
                {
                    GUILayout.Space(8);
                    GUILayout.Label("RESTORE · WHAT IT LOST", Look.Section);
                    Line(r.NewParts + (r.NewParts == 1 ? " new part" : " new parts") + (r.Quote.BinParts > 0 ? " (" + r.Quote.BinParts + " from the bin)" : ""), Pricing.Funds(r.Quote.NewPrice));
                    Line("Refurbish the rest", Pricing.Funds(r.Quote.RefurbishPrice));
                    Line("Fuel", Pricing.Funds(r.Quote.FuelPrice));
                    Line("Total · build time", Pricing.Funds(r.Quote.Total) + " · " + Pricing.Duration(r.Quote.Seconds, S));
                    QuoteLine bin = r.Quote.Lines.FirstOrDefault(l => l.Kind == OwnerKind.Bin);
                    if (bin != null) GUILayout.Label("From the parts bin: " + string.Join(", ", BuildPanel.BinItems(bin).ToArray()), Look.Meta);
                    if (r.Blocked != null) GUILayout.Label(r.Blocked, Look.MetaBad);
                    // Its own old parts first (No keeps them), then any old bin part that would fill a gap.
                    restoreAsking = AskOwnOldParts(sc, h) || QuestionFor("restore:" + h.Id).Draw(r.Quote, () => restoreFor = null, 270);
                }
                else restoreAsking = false;
                List<string> log = sc.LogOf(h);
                if (log.Count > 0)
                {
                    GUILayout.Space(8);
                    GUILayout.Label("FLIGHTS", Look.Section);
                    foreach (string e in log) GUILayout.Label(e, Look.Meta);
                }
                GUILayout.EndScrollView();
                GUILayout.Space(6);

                // The first button is the likely next step: restore a ship that's missing parts, refurbish a worn-out
                // one, otherwise launch it.
                string err;
                bool restoreFirst = r != null && r.Blocked == null && !restoreAsking;
                if (r != null && GUILayout.Button((restoreAsking ? "Restore: answer the parts bin above" : "Restore · " + Pricing.Funds(r.Quote.Total)), restoreFirst ? Look.Go : Look.GoOff, GUILayout.Height(38)) && restoreFirst)
                {
                    if (BuildsLink.RestoreBuild(h, r, false, false, out err) == null) error = err;
                    else { error = null; Tab = 0; picking = false; library = null; }
                    restoreFor = null;
                    GUIUtility.ExitGUI();
                }
                if (!worn && GUILayout.Button(h.Ready ? "Launch" : "Refuel and launch · " + Pricing.Funds(q.FuelPrice), restoreFirst ? Look.Ghost : Look.Go, GUILayout.Height(restoreFirst ? 32 : 38)))
                    Launch(sc, h);
                if (canRefurbish && GUILayout.Button("Refurbish · " + Pricing.Funds(q.Total), worn && !restoreFirst ? Look.Go : Look.Ghost, GUILayout.Height(worn && !restoreFirst ? 38 : 32)))
                {
                    if (sc.RefurbishBuild(h, out err) == null) error = err;
                    else { error = null; Tab = 0; }
                }
                string facility = InEditor ? (EditorDriver.editorFacility == EditorFacility.SPH ? "SPH" : "VAB") : Suite.FacilityOf(h.Craft);
                if (GUILayout.Button("Open in the " + facility, Look.Ghost, GUILayout.Height(32)))
                {
                    EditorAddon.OpenHangarShip(h);
                    Show = false;
                    if (button != null) button.SetFalse(false);
                }
                GUILayout.Label(h.Ready ? "Fuelled: launching costs nothing. Open it in the " + facility + " to build on it."
                                : "Refurbishing is queued like a build; the ship is then ready to launch. Open it in the " + facility + " to build on it.", Look.Hint);
                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                // Its parts are refurbished as they go into the bin (Phil, round 4), so taking it apart costs that.
                double apart = sc.TakeApartPrice(h);
                if (GUILayout.Button("Take apart" + (apart >= 0.5 ? " · refurbish " + Pricing.Funds(apart) : ""), Look.Link, GUILayout.ExpandWidth(false)))
                {
                    if (sc.TakeApart(h, out string apartErr)) { selectedId = null; error = null; }
                    else error = apartErr;
                }
                GUILayout.Space(16);
                if (Suite.Career && GUILayout.Button("Sell for scrap · +" + Pricing.Funds(sc.ScrapValue(h)), Look.Link, GUILayout.ExpandWidth(false))) { sc.Sell(h); selectedId = null; error = null; }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label("This ship is part of a build in the queue. Cancel that build to free it.", Look.Hint);
            }
        }

        static void Line(string key, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(key, Look.RowKey);
            GUILayout.Label(value, Look.RowVal);
            GUILayout.EndHorizontal();
        }

        // ---- Parts bin ----

        void BinTab(PRSScenario sc)
        {
            List<BinKind> kinds = sc.Bin.Where(k => k.Count > 0).OrderBy(k => Pip.NameOf(k.Sample)).ToList();
            scrollMain = GUILayout.BeginScrollView(scrollMain, GUILayout.ExpandWidth(true));
            GUILayout.BeginVertical(); GUILayout.Space(10);
            Indent(() => GUILayout.Label("Loose parts from ships taken apart, pieces with no engine or probe core, and dangling struts and fuel lines. Each was refurbished as it came in, so it's like a new part that's already paid for: builds use them in place of new parts.", Look.Hint));
            if (kinds.Count == 0) Indent(() => GUILayout.Label("The parts bin is empty.", Look.Hint));
            // One row per kind of part: a Phil's Iterative Parts part shows its own program and Mk ("Augur I Mk. 1").
            foreach (BinKind k in kinds)
            {
                Indent(() =>
                {
                    GUILayout.BeginHorizontal(Look.Card);
                    GUILayout.BeginVertical();
                    GUILayout.Label(Pip.NameOf(k.Sample), Look.Name);
                    if (Pip.Behind(k.Sample, out string now, out string current) > 0)
                        GUILayout.Label("Behind its program (" + Pip.Change(now, current) + "): update it before a build can use it", Look.MetaWarn);
                    GUILayout.EndVertical();
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("× " + k.Count, Look.RowVal, GUILayout.Width(50));
                    if (Suite.Career && GUILayout.Button("Sell · +" + Pricing.Funds(sc.ScrapValue(k)), Look.Ghost, GUILayout.Height(26), GUILayout.Width(120))) sc.Sell(new[] { k });
                    GUILayout.EndHorizontal();
                });
            }
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            List<KeyValuePair<BinKind, int>> old = kinds.Where(k => Pip.Behind(k.Sample) > 0).Select(k => new KeyValuePair<BinKind, int>(k, k.Count)).ToList();
            bool sell = Suite.Career && kinds.Count > 0;
            if (sell || old.Count > 0)
            {
                GUILayout.BeginVertical(GUILayout.Width(220));
                GUILayout.Space(10);
                if (sell)
                {
                    double all = kinds.Sum(sc.ScrapValue);
                    GUILayout.Label(Suite.Parts(kinds.Sum(k => k.Count)) + " · scrap value " + Pricing.Funds(all), Look.Meta);
                    if (GUILayout.Button("Sell all for scrap", Look.Ghost, GUILayout.Height(32))) sc.Sell(kinds);
                }
                if (old.Count > 0)
                {
                    // Phil's design (play-test 2026-11-08 #5): old-Mk parts are updated here, or when a build asks.
                    GUILayout.Space(10);
                    double price = sc.PipUpdatePrice(old, out int n);
                    GUILayout.Label((n == 1 ? "1 PIP part is" : n + " PIP parts are") + " behind the current Mk. Builds use them only once they're updated.", Look.Meta);
                    if (GUILayout.Button("Update PIP parts · " + (price < 0.5 ? "free" : Pricing.Funds(price)), Look.Ghost, GUILayout.Height(32)))
                    {
                        if (!sc.UpdatePip(old, out string err)) error = err;
                        else { error = null; Suite.Message(Suite.Parts(n) + " in the parts bin updated to the current Mk"); }
                        GUIUtility.ExitGUI();
                    }
                }
                GUILayout.EndVertical();
                GUILayout.Space(12);
            }
        }
    }
}
