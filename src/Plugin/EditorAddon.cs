using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KSP.UI.Screens;
using PhilsReusabilitySuite.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// In the VAB and SPH: knows which parts are a real, built ship's, turns the green Launch button into Build (it
    /// opens the Build panel), and shows the suite window from the toolbar.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class EditorAddon : MonoBehaviour
    {
        public static EditorAddon Instance { get; private set; }

        Button.ButtonClickedEvent stockLaunch;
        SuiteWindow window;

        void Start()
        {
            Instance = this;
            Look.Init();
            // KSP brings back the ship that was here (its cache) as this editor starts; any later load replaces it.
            restoringCache = EditorDriver.StartupBehaviour == EditorDriver.StartupBehaviours.LOAD_FROM_CACHE;
            if (!restoringCache) { live.Clear(); if (!Expected(OpenedCraft)) OpenedCraft = null; }
            GameEvents.onEditorShipModified.Add(OnShipModified);
            GameEvents.onEditorLoad.Add(OnEditorLoad);
            GameEvents.onEditorRestart.Add(OnEditorRestart);
            window = gameObject.AddComponent<SuiteWindow>();
            StartCoroutine(HookLaunchButton());
            StartCoroutine(CheckShipAlreadyThere());
        }

        /// <summary>
        /// KSP brings the ship back from its cache without a load event, sometimes before this addon is listening,
        /// so the ship already in the editor is checked once it's there. Checking twice changes nothing.
        /// </summary>
        IEnumerator CheckShipAlreadyThere()
        {
            // Counted in frames, and the ship looked at before the limit, so one slow frame can't skip the check.
            for (int frame = 0; frame < 300; frame++)
            {
                ShipConstruct ship = EditorLogic.fetch != null ? EditorLogic.fetch.ship : null;
                if (ship != null && ship.parts.Count > 0)
                {
                    yield return null;
                    Check(ship);
                    expect = null;   // a ship opened from the Space Center is in: its notes are the ship's own now
                    restoringCache = false;
                    yield break;
                }
                yield return null;
            }
            restoringCache = false;
        }

        void OnDestroy()
        {
            GameEvents.onEditorShipModified.Remove(OnShipModified);
            GameEvents.onEditorLoad.Remove(OnEditorLoad);
            GameEvents.onEditorRestart.Remove(OnEditorRestart);
            if (stockLaunch != null && EditorLogic.fetch != null && EditorLogic.fetch.launchBtn != null) EditorLogic.fetch.launchBtn.onClick = stockLaunch;
            if (Instance == this) Instance = null;
        }

        /// <summary>Waits for the editor to wire its own Launch button, then takes the click over.</summary>
        IEnumerator HookLaunchButton()
        {
            while (EditorLogic.fetch == null || EditorLogic.fetch.launchBtn == null) yield return null;
            yield return null;
            yield return null;
            Button b = EditorLogic.fetch.launchBtn;
            stockLaunch = b.onClick;
            b.onClick = new Button.ButtonClickedEvent();
            b.onClick.AddListener(OnLaunchClicked);
            Log.Info("The green Launch button now opens the Build panel");
        }

        void OnLaunchClicked() { OpenBuildPanel(); }

        /// <summary>The editor's ship, as a craft node (KSP writes its parts in the ship's own order).</summary>
        public static Node EditorCraft() => EditorLogic.fetch?.ship != null ? KspNodes.FromKsp(EditorLogic.fetch.ship.SaveShip()) : null;

        /// <summary>The ready hangar ship the editor's ship is exactly (all its parts, nothing more), or null.</summary>
        public static HangarShip ReadyShipInEditor()
        {
            if (!HighLogic.LoadedSceneIsEditor || EditorLogic.fetch == null || EditorLogic.fetch.ship == null || PRSScenario.Instance == null) return null;
            HangarShip h = PRSScenario.Instance.ExactShipOf(EditorCraft());
            return h != null && h.Ready ? h : null;
        }

        public static bool EditorShipIsReady() => ReadyShipInEditor() != null;

        /// <summary>Runs the stock launch of a hangar ship (it launches free): the suite starts it, so it knows which ship it is.</summary>
        public void StockLaunch(HangarShip h)
        {
            PRSScenario.Instance?.Launching(h);
            if (stockLaunch != null) stockLaunch.Invoke();
            else EditorLogic.fetch.launchVessel();
        }

        // ---- Which parts are a real, built ship's (one rule) ----
        //
        // Phil, round 4: "the VAB to understand if I've got an actual ship that is loaded in there. A ship that exists and
        // has been built. I slap a part on there and the VAB knows that all of those parts are real, have been built, and
        // that ship is 'The Peleus', and it knows that the new part I just added is not a built part."
        //
        // A part is real hardware only if it carries a note the suite put there itself when it brought a stored ship in
        // (Open in the VAB, a queued build to edit, a hangar copy picked up in Phil's Builds), and that note still points
        // at the ship's current parts in the hangar. Parts that arrive any other way (a saved craft, a merge, a
        // subassembly, a design) come in as new parts, however they were saved. A copy is a new part. Launching ends it.
        //
        // The build is the ship that was opened (Phil, round 4: "once I build the Peleus III #2, that ship should always
        // be the Peleus III #2"): the hangar ship loaded into the editor, or brought into an empty one. Another hangar
        // ship added to it is owned parts, and stops being a ship of its own ("Sure i guess that works").

        /// <summary>The stored craft the suite is bringing into the editor right now, and when. It ends as those parts arrive
        /// (or after two minutes, for a ship opened from the Space Center whose scene is still loading).</summary>
        static string expect;
        static float expectAt;
        /// <summary>The notes on the editor's own ship (so KSP's undo and its cache keep the hardware as it was).</summary>
        static readonly HashSet<string> live = new HashSet<string>();
        /// <summary>The editor is bringing back its cached ship as it opens (KSP's startup flag stays set after that).</summary>
        static bool restoringCache;
        /// <summary>The stored craft the editor's ship was opened from (a hangar ship's, or a queued build's), or null for
        /// a new ship. New, or any other load, ends it.</summary>
        public static string OpenedCraft;

        /// <summary>The suite is about to bring this stored craft in: its notes are real.</summary>
        public static void Expect(string craftId) { expect = craftId; expectAt = Time.realtimeSinceStartup; }

        /// <summary>A note that counts: it points at a stored craft, and either the suite is bringing that craft in right
        /// now, or the part is already the editor ship's hardware (KSP's undo and its cache rebuild it).</summary>
        static bool Kept(string note) => Hardware.Parse(note, out string craftId, out _) && (Expected(craftId) || live.Contains(note)) && Known(craftId);

        static bool Expected(string craftId) => craftId == expect && Time.realtimeSinceStartup - expectAt < 120f;

        static bool Known(string craftId) => PRSScenario.Instance != null && PRSScenario.Instance.Knows(craftId);

        /// <summary>
        /// Is this editor part real hardware (by the rule above), not a new part? Phil's Iterative Parts asks as the editor
        /// builds each part from its saved data: a new part is made current, hardware keeps the version and Mk it was
        /// built at (play-test #6: opening Gallant I #1 quietly made its flown Mk 1 pod a Mk 2).
        /// </summary>
        public static bool IsHardwareNode(ConfigNode part)
        {
            if (part == null) return false;
            foreach (ConfigNode m in part.GetNodes("MODULE"))
                if (m.GetValue("name") == Core.Craft.ModuleName) return Kept(m.GetValue(Hardware.Field));
            return false;
        }

        public static bool IsHardware(Part p) => Kept(PRSPart.NoteOf(p));

        void OnShipModified(ShipConstruct ship) { Check(ship); }

        /// <summary>A new ship (New, or a load): only what the suite is bringing in is hardware now, and only a ship the
        /// suite is opening is opened. Not when KSP brings the ship back from its cache.</summary>
        void OnEditorRestart()
        {
            if (restoringCache) return;
            live.Clear();
            if (!Expected(OpenedCraft)) OpenedCraft = null;
        }

        /// <summary>KSP's craft browser (and the suite's own loads) fire this with the loaded ship, or with a merge's parts.</summary>
        void OnEditorLoad(ShipConstruct ship, CraftBrowserDialog.LoadType type)
        {
            Arrive(ship?.parts);
            Check(EditorLogic.fetch != null ? EditorLogic.fetch.ship : null);
        }

        /// <summary>Parts arriving in the editor: unless the suite is bringing them in, they're new parts.</summary>
        internal static void Arrive(IEnumerable<Part> parts)
        {
            if (parts == null) return;
            List<Part> arriving = parts.ToList();
            var those = new HashSet<Part>(arriving);
            // A part that carries a note already on another part of the editor's ship is a copy of it (a merge of a file
            // saved from it): a new part.
            ShipConstruct ship = EditorLogic.fetch != null ? EditorLogic.fetch.ship : null;
            var onShip = new HashSet<string>(ship != null ? ship.parts.Where(p => !those.Contains(p)).Select(PRSPart.NoteOf).Where(n => n != null) : new string[0]);
            int cleared = 0;
            foreach (PRSPart m in arriving.Select(PRSPart.Of).Where(x => x != null && !string.IsNullOrEmpty(x.hw)).ToList())
            {
                bool ours = Hardware.Parse(m.hw, out string craftId, out _) && Expected(craftId) && Known(craftId) && !onShip.Contains(m.hw);
                if (!ours) { m.hw = ""; cleared++; }
            }
            if (cleared > 0) Log.Info(Suite.Parts(cleared) + " came in as new parts (notes saved with them don't count)");
        }

        /// <summary>The editor's ship: each note counts once (a copy that kept one is a new part), and these are its hardware.</summary>
        internal static void Check(ShipConstruct ship)
        {
            if (ship == null) return;
            live.Clear();
            foreach (PRSPart m in ship.parts.Select(PRSPart.Of).Where(x => x != null && !string.IsNullOrEmpty(x.hw)))
                if (!live.Add(m.hw)) m.hw = "";
        }

        /// <summary>After Build: the editor's ship is the build's parts now (and the hangar ship's, once it's done).</summary>
        internal static void NoteAs(ShipConstruct ship, string craftId)
        {
            if (ship == null) return;
            OpenedCraft = craftId;
            for (int i = 0; i < ship.parts.Count; i++)
            {
                PRSPart m = PRSPart.Of(ship.parts[i]);
                if (m != null) m.hw = Hardware.Make(craftId, i);
            }
            Check(ship);
        }

        /// <summary>Loads a stored craft into the editor as that hardware (a hangar ship or a queued build).</summary>
        public static void LoadNoted(Node noted, string shipName, string craftId, string fileTag)
        {
            ConfigNode c = KspNodes.ToKsp(noted, "");
            c.SetValue("ship", shipName, true);
            string path = System.IO.Path.Combine(SuiteFiles.SaveDir, fileTag + ".craft");
            c.Save(path);
            Expect(craftId);
            OpenedCraft = craftId;
            EditorLogic.LoadShipFromFile(path);
            expect = null;
        }

        public static void LoadHangarShip(HangarShip h)
        {
            LoadNoted(h.Noted(), h.Name, h.CraftId, "hangar-" + h.Id);
            Log.Info("Loaded " + h.DisplayName + " from the hangar (" + h.PartCount + " parts)");
        }

        public static void LoadBuild(BuildJob j) => LoadNoted(j.Noted(), j.Name, j.CraftId, "build-" + j.Id);

        /// <summary>"Open in the VAB": the hangar ship exactly as it's stored, in the editor. From the Space Center it goes
        /// to its own building (VAB or SPH) and opens it there.</summary>
        public static void OpenHangarShip(HangarShip h)
        {
            if (HighLogic.LoadedSceneIsEditor) { LoadHangarShip(h); return; }
            // Entering a building from the Space Center saves the game first, as KSP's own buttons do: the hangar
            // as it is now (refuelled, say) is what the editor scene loads.
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            string facility = Suite.FacilityOf(h.Craft);
            ConfigNode c = KspNodes.ToKsp(h.Noted(), "");
            c.SetValue("ship", h.Name, true);
            string path = System.IO.Path.Combine(SuiteFiles.SaveDir, "hangar-" + h.Id + ".craft");
            c.Save(path);
            Expect(h.CraftId);
            OpenedCraft = h.CraftId;
            Log.Info("Opening " + h.DisplayName + " from the hangar in the " + facility + " (" + h.PartCount + " parts)");
            EditorDriver.StartAndLoadVessel(path, facility == "SPH" ? EditorFacility.SPH : EditorFacility.VAB);
        }

        // ---- Build panel ----

        public BuildPanel Panel { get; private set; }

        public void OpenBuildPanel()
        {
            if (Panel == null) Panel = gameObject.AddComponent<BuildPanel>();
            Panel.Open();
        }
    }

    /// <summary>Everything put on the editor's cursor (a merge, a subassembly, a Phil's Builds design) arrives through here.</summary>
    [HarmonyLib.HarmonyPatch(typeof(EditorLogic), "SpawnConstruct")]
    static class ArrivingParts
    {
        static void Prefix(ShipConstruct __0) { if (__0 != null) EditorAddon.Arrive(__0.parts); }
    }

    /// <summary>
    /// KSP's own pre-launch checks, run at Build instead: part count, size and mass limits for the building and
    /// the pad, experimental parts, a working facility and a control source. Taken from the editor's stock
    /// check list, leaving out its price check (the suite prices builds itself) and "launch site clear" (that
    /// matters at launch, not at build).
    /// </summary>
    static class BuildChecks
    {
        static readonly System.Reflection.MethodInfo stockCheck = typeof(EditorLogic).GetMethod("GetStockPreFlightCheck", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        static readonly System.Reflection.FieldInfo testsField = typeof(PreFlightCheck).GetField("tests", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        public static void Run(Callback onPass, Callback onFail)
        {
            try
            {
                if (KSP.UI.CrewAssignmentDialog.Instance != null) ShipConstruction.ShipManifest = KSP.UI.CrewAssignmentDialog.Instance.GetManifest();
                var stock = (PreFlightCheck)stockCheck.Invoke(EditorLogic.fetch, new object[] { EditorLogic.fetch.launchSiteName });
                var tests = (System.Collections.IList)testsField.GetValue(stock);
                var ours = new PreFlightCheck(onPass, onFail);
                int n = 0;
                foreach (object t in tests)
                {
                    string name = t.GetType().Name;
                    if (name == "CanAffordLaunchTest" || name == "LaunchSiteClear") continue;
                    var test = (PreFlightTests.IPreFlightTest)t;
                    // KSP's dialog special-cases the wrong-vessel-type check by its type, so that one stays as it is.
                    ours.AddTest(name.Contains("VesselType") ? test : new BuildWording(test));
                    n++;
                }
                Log.Info("Running " + n + " of KSP's pre-launch checks before building");
                ours.RunTests();
            }
            catch (System.Exception ex)
            {
                Log.Warn("KSP's pre-launch checks couldn't be run (" + ex.Message + "); building without them");
                onPass();
            }
        }

        /// <summary>
        /// One of KSP's own pre-launch checks, unchanged, except that its warning's go-ahead button says
        /// "Build anyway" instead of "Launch anyway": at this point the ship is being built, not launched.
        /// </summary>
        sealed class BuildWording : PreFlightTests.IPreFlightTest
        {
            readonly PreFlightTests.IPreFlightTest inner;
            public BuildWording(PreFlightTests.IPreFlightTest inner) { this.inner = inner; }
            public bool Test() => inner.Test();
            public string GetWarningTitle() => inner.GetWarningTitle();
            public string GetWarningDescription() => inner.GetWarningDescription();
            public string GetProceedOption() => string.IsNullOrEmpty(inner.GetProceedOption()) ? inner.GetProceedOption() : "Build anyway";
            public string GetAbortOption() => inner.GetAbortOption();
        }
    }


    /// <summary>The Build panel: every piece on its own line, the totals, and Normal / Overtime / Outsource.</summary>
    public class BuildPanel : MonoBehaviour
    {
        public enum Pace { Normal, Overtime, Outsource }

        public bool Showing;
        public Node Craft;
        public string ShipName;
        public string Facility;
        public Quote Quote;
        public BuildJob Editing;
        public HangarShip ReadyShip;
        /// <summary>The editor's ship is exactly a hangar ship that isn't fuelled yet: it can launch as it is.</summary>
        public HangarShip AsIs;
        /// <summary>
        /// The ship this build is: the queued build's own ship, or the hangar ship that was opened (Phil, round 4: "once I
        /// build the Peleus III #2, that ship should always be the Peleus III #2"), whatever it's saved as, while its parts
        /// are in it. Null: a new ship.
        /// </summary>
        public Ship MainShip;
        /// <summary>It's all launcher parts: a launcher on its own isn't a ship, so it can't be built.</summary>
        public bool OnlyLauncher;
        public Pace Chosen = Pace.Normal;
        public string Error;
        /// <summary>Old-Mk bin parts this build leaves out (answered "No": built new instead).</summary>
        public readonly HashSet<string> SkipOld = new HashSet<string>();
        OldPartsQuestion oldQ;
        OldPartsQuestion OldQ => oldQ ?? (oldQ = new OldPartsQuestion(SkipOld));
        /// <summary>The panel is asking about old-Mk bin parts: no Build until it's answered.</summary>
        bool asking;

        Rect rect = new Rect(0, 0, 600, 100);
        Vector2 scroll;

        static Core.Settings S => Suite.Settings;

        /// <summary>What the build is called: the ship's own name, or the design's for a new ship.</summary>
        public string BuildName => MainShip != null ? MainShip.Name : ShipName;
        public string KeepShipId => MainShip?.Id;

        /// <summary>Works out the quote for the ship in the editor and shows the panel.</summary>
        public void Open()
        {
            SkipOld.Clear();
            OldQ.Reset();
            Refresh();
            Showing = true;
            rect.x = (Screen.width - rect.width) / 2;
            rect.y = Mathf.Max(60, (Screen.height - 760) / 2);
        }

        public void Refresh()
        {
            Error = null;
            PRSScenario sc = PRSScenario.Instance;
            ShipConstruct ship = EditorLogic.fetch.ship;
            EditorAddon.Check(ship);
            Craft = KspNodes.FromKsp(ship.SaveShip());
            ShipName = KSP.Localization.Localizer.Format(ship.shipName);
            // A ship picked up into an empty VAB keeps KSP's "Untitled Space Craft": it's built under its design's name
            // instead (the launcher under it isn't the ship), so it comes home as "Kopje 005 #1".
            if (ShipName == KSP.Localization.Localizer.Format("#autoLOC_900530"))
            {
                List<string> own = ship.parts.Select(p => PRSPart.Of(p)?.design).Where(d => !BuildsLink.IsLauncher(d)).ToList();
                string designName = BuildsLink.NameFor(own, own.FirstOrDefault(d => !string.IsNullOrEmpty(d)));
                if (!string.IsNullOrEmpty(designName)) ShipName = designName;
            }
            Facility = ship.shipFacility == EditorFacility.SPH ? "SPH" : "VAB";
            HangarShip exact = sc?.ExactShipOf(Craft);
            ReadyShip = exact != null && exact.Ready ? exact : null;
            AsIs = exact != null && !exact.Ready ? exact : null;
            Editing = ReadyShip == null ? sc?.JobOf(Craft) : null;
            Quote = sc?.QuoteFor(Craft, ShipName, Editing, SkipOld);
            string keep = Editing != null ? Editing.ShipId : sc?.ShipOfOpened(EditorAddon.OpenedCraft);
            MainShip = Quote != null && keep != null && Quote.HangarShips.Contains(keep) ? sc.ShipOf(keep) : null;
            OnlyLauncher = BuildsLink.OnlyLauncher(Craft);
            Craft.Set("ship", BuildName);
            try { Landings = StageReturns.CheckEditorShip(ship); }
            catch (System.Exception ex) { Landings = new List<StageReturns.EditorPiece>(); Log.Warn("Landing check failed: " + ex.Message); }
            try
            {
                bool anyUnstamped = ship.parts.Any(p => PRSPart.Of(p) != null && string.IsNullOrEmpty(PRSPart.Of(p).design));
                Unfiled = PhilsBuilds.BuildsApi.Current == null && anyUnstamped;
            }
            catch (System.Exception) { Unfiled = false; }
        }

        /// <summary>What the panel calls the build: the ship's own name ("Peleus III #2"), or the design's for a new ship.</summary>
        public string Title => MainShip?.DisplayName ?? ShipName;

        /// <summary>The ship (or part of it) isn't from a design filed in Phil's Builds.</summary>
        public bool Unfiled;
        public List<StageReturns.EditorPiece> Landings = new List<StageReturns.EditorPiece>();

        void DrawLandings()
        {
            if (Landings == null || Landings.Count == 0) return;
            GUILayout.Space(8);
            GUILayout.Label("LANDING CHECK · pieces that drop off, empty, with every chute open", Look.Section);
            // Each piece on two full-width lines, its name and then how it lands: side by side, wrapped text in one
            // column was cut off where it ran longer than the other.
            foreach (StageReturns.EditorPiece p in Landings)
            {
                GUILayout.Label((p.Count > 1 ? p.Count + " × " : "") + p.Label + " <color=#7d88a3>(" + Suite.Parts(p.Parts) + ")</color>", Look.Meta);
                string how = p.Chutes > 0 ? " on " + p.Chutes + (p.Chutes == 1 ? " chute" : " chutes") : " with no chutes";
                if (p.Speed <= Core.Landing.SafeSpeed) GUILayout.Label("    comes home: " + p.Speed.ToString("0.0") + " m/s" + how, Look.MetaGood);
                else if (p.Speed <= Core.Landing.LostSpeed) GUILayout.Label("    comes home hard: " + p.Speed.ToString("0.0") + " m/s" + how, Look.MetaWarn);
                else if (p.CanBurn && !p.TooWeakToBurn)
                    GUILayout.Label("    lands with a burn if it keeps " + p.BurnPropellant.ToString("0.00") + " t of propellant (" + p.Speed.ToString("0") + " m/s without)", Look.MetaWarn);
                else if (p.CanBurn)
                    GUILayout.Label("    lost: " + p.Speed.ToString("0") + " m/s" + how + "; its engines are too weak for a landing burn", Look.MetaBad);
                else GUILayout.Label("    lost: " + p.Speed.ToString("0") + " m/s" + how, Look.MetaBad);
                GUILayout.Space(2);
            }
        }

        /// <summary>Presses Build (or Update build) with the chosen pace. Returns false with Error set if it couldn't.</summary>
        public bool Confirm()
        {
            PRSScenario sc = PRSScenario.Instance;
            if (sc == null || Quote == null) { Error = "The suite's save data isn't loaded."; return false; }
            if (Quote.PartCount == 0) { Error = "There's nothing to build."; return false; }
            if (OnlyLauncher) { Error = PRSScenario.OnlyLauncherText; return false; }
            if (Quote.OldBin.Count > 0) { Error = "Answer the question about the parts bin's older parts first."; return false; }
            string error;
            // Bin parts or hangar ships priced into this build may have been sold or put in another build since
            // the panel opened: price it again and let the player look before paying.
            string stale = sc.StaleQuote(Quote, Editing, KeepShipId);
            if (stale != null)
            {
                Refresh();
                Error = "Something changed since this was priced (" + stale + "). Check the new price and press Build again.";
                return false;
            }
            string title = Title;
            string craftId;
            if (Editing != null)
            {
                if (!sc.UpdateBuild(Editing, Craft, BuildName, Quote, KeepShipId, out error)) { Error = error; return false; }
                craftId = Editing.CraftId;
                if (Chosen == Pace.Overtime && !Editing.Overtime && sc.Jobs.Contains(Editing)) sc.SetOvertime(Editing, true, out error);
                if (Chosen == Pace.Outsource && sc.Jobs.Contains(Editing)) sc.Outsource(Editing, out error);
                Suite.Message(title + ": build updated");
            }
            else
            {
                BuildJob job = sc.StartBuild(Craft, BuildName, Facility, Quote, Chosen == Pace.Overtime, Chosen == Pace.Outsource, KeepShipId, out error);
                if (job == null) { Error = error; return false; }
                craftId = job.CraftId;
                if (Chosen != Pace.Outsource) Suite.Message(title + " is being built: " + Pricing.Duration(job.SecondsLeft(S), S));
            }
            // The ship in the editor is now the build's parts (and the hangar ship's, once it's finished).
            if (EditorLogic.fetch.ship.parts.Count == Quote.PartCount) EditorAddon.NoteAs(EditorLogic.fetch.ship, craftId);
            Showing = false;
            return true;
        }

        double Extra(Pace p)
        {
            if (Quote == null) return 0;
            double remaining = Editing != null ? 1 - Mathf.Min(1, (float)(Editing.Progress * Editing.BuildPrice / System.Math.Max(1, Quote.BuildPrice))) : 1;
            return p == Pace.Overtime ? (Editing != null && Editing.Overtime ? 0 : Pricing.OvertimeCharge(Quote.BuildPrice, remaining, S))
                 : p == Pace.Outsource ? Pricing.OutsourceCharge(Quote.BuildPrice, remaining, S) : 0;
        }

        double Seconds(Pace p)
        {
            if (Quote == null || p == Pace.Outsource) return 0;
            double remaining = Editing != null ? 1 - System.Math.Min(1, Editing.Progress * Editing.BuildPrice / System.Math.Max(1, Quote.BuildPrice)) : 1;
            double speed = p == Pace.Overtime || (Editing != null && Editing.Overtime) ? S.OvertimeSpeed : 1;
            return Quote.Seconds * remaining / speed;
        }

        double DueNow => Quote == null ? 0 : (Editing != null ? Quote.Total - Editing.Paid : Quote.Total) + Extra(Chosen);

        void OnGUI()
        {
            if (!Showing) return;
            GUI.skin = Look.Skin;
            rect = GUILayout.Window(GetInstanceID(), rect, Draw, GUIContent.none, Look.Window, GUILayout.Width(600));
            // Keep clicks on the panel from reaching the editor underneath.
            if (rect.Contains(Event.current.mousePosition)) InputLockManager.SetControlLock(ControlTypes.EDITOR_LOCK, "PRSBuildPanel");
            else InputLockManager.RemoveControlLock("PRSBuildPanel");
        }

        void OnDisable() { InputLockManager.RemoveControlLock("PRSBuildPanel"); }

        void Update()
        {
            if (!Showing) InputLockManager.RemoveControlLock("PRSBuildPanel");
        }

        void Draw(int id)
        {
            SuiteWindow.HeaderBar("Build", Facility, () => Showing = false);
            asking = false;
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Space(10);
            GUILayout.BeginHorizontal(); GUILayout.Space(16); GUILayout.BeginVertical();

            if (ReadyShip != null) { DrawReady(); EndBody(); return; }

            GUILayout.Label(Editing != null ? "Update build" : AsIs != null ? "In the hangar as it is" : "Ready to build", Look.Eyebrow);
            GUILayout.Label(Title, Look.H2);
            GUILayout.Space(6);
            if (AsIs != null)
            {
                bool worn = Core.Wear.WornOut(AsIs.Wear);
                GUILayout.Label(worn ? "<b>" + AsIs.DisplayName + "</b> is worn out (100% wear): it must be refurbished before it flies again."
                                     : "<b>" + AsIs.DisplayName + "</b> is exactly as it came home, at " + Core.Wear.Percent(AsIs.Wear) + " wear. Launch it as it is (it's refuelled first), or refurbish it back to 0% wear.", worn ? Look.Notice : Look.NoticeGood);
                GUILayout.Space(6);
            }
            if (Unfiled)
            {
                GUILayout.BeginHorizontal(Look.Notice);
                GUILayout.Label("<color=#ffcf5a>Not filed in Phil's Builds.</color> A ship that isn't from a filed design goes to the parts bin when it comes home.", Look.Meta, GUILayout.Width(430));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("File it", Look.Ghost, GUILayout.Height(30), GUILayout.Width(80)))
                {
                    try { PhilsBuilds.BuildsApi.OpenSave(); Showing = false; }
                    catch (System.Exception ex) { Error = "Couldn't open Phil's Builds' Save panel: " + ex.Message; }
                }
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
            }
            if (Quote == null) { GUILayout.Label("The suite's save data isn't loaded.", Look.MetaBad); EndBody(); return; }
            if (OnlyLauncher) { GUILayout.Label(PRSScenario.OnlyLauncherText, Look.Notice); EndBody(); return; }
            asking = OldQ.Draw(Quote, Refresh, 540);

            // What the build is made of: one build, broken down (Phil read these as cards to pick from).
            GUILayout.Label("WHAT GOES INTO IT", Look.Section);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(300), GUILayout.MinHeight(Mathf.Min(300, 48 * Quote.Lines.Count)));
            foreach (QuoteLine line in Quote.Lines) DrawLine(line);
            GUILayout.EndScrollView();
            GUILayout.Label("Parts already built cost only their refurbish, which follows their ship's wear. Parts from the bin are already paid for. New parts cost their full price.", Look.Hint);
            DrawLandings();

            GUILayout.Space(8);
            GUILayout.BeginVertical(Look.Notice);
            Row("New parts", Quote.NewPrice, Look.RowVal);
            Row("Refurbishing", Quote.RefurbishPrice, Look.RowVal);
            Row("Fuel", Quote.FuelPrice, Look.RowVal);
            if (Chosen != Pace.Normal) Row(Chosen == Pace.Overtime ? "Overtime" : "Outsourcing", Extra(Chosen), Look.RowValAmber, "+");
            if (Editing != null) Row("Already paid", -Editing.Paid, Look.RowValGood);
            Look.Fill(GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true)), Look.Line);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Editing != null ? "To pay now" : "Total", Look.RowTotalKey);
            GUILayout.Label(Pricing.Funds(DueNow), Look.RowTotal);
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(10);
            GUILayout.Label("BUILD TIME", Look.Section);
            GUILayout.BeginHorizontal();
            PaceButton(Pace.Normal, "Normal", Pricing.Duration(Seconds(Pace.Normal), S));
            PaceButton(Pace.Overtime, "Overtime", Editing != null && Editing.Overtime ? "already on" : Pricing.Duration(Seconds(Pace.Overtime), S) + " · +" + Pricing.Funds(Extra(Pace.Overtime)));
            PaceButton(Pace.Outsource, "Outsource", "ready now · +" + Pricing.Funds(Extra(Pace.Outsource)));
            GUILayout.EndHorizontal();
            GUILayout.Label(PaceNote(), Look.Hint);
            EndBody();
        }

        void EndBody()
        {
            GUILayout.EndVertical(); GUILayout.Space(16); GUILayout.EndHorizontal();
            GUILayout.Space(10);
            DrawFooter();
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 50));
        }

        void DrawReady()
        {
            GUILayout.Label("Ready in the hangar", Look.Eyebrow);
            GUILayout.Label(ReadyShip.DisplayName, Look.H2);
            GUILayout.Space(8);
            GUILayout.Label(ReadyShip.DisplayName + " is fuelled and ready, so launching it costs nothing" + (ReadyShip.Wear >= 0.005 ? " (wear " + Core.Wear.Percent(ReadyShip.Wear) + "; you can refurbish it first)" : "") + ". Change anything and it becomes a build again.", Look.NoticeGood);
        }

        /// <summary>One line of the build's breakdown: where these parts come from, what's done to them, and the price.</summary>
        void DrawLine(QuoteLine line)
        {
            string parts = Suite.Parts(line.Parts);
            string title, note;
            List<string> items = null;
            if (line.Kind == OwnerKind.New) { title = NewTitle(line); note = "built new"; }
            else if (line.Kind == OwnerKind.Bin)
            {
                // Which parts the bin gives (Phil didn't know it had used them).
                title = parts + " from the parts bin, already paid for";
                note = null;
                items = BinItems(line);
            }
            else
            {
                title = parts + " from " + line.Label;
                bool nothing = line.Price < 0.5 && line.Fuel < 0.5;
                bool refuel = line.Fuel >= 0.5;
                note = "already paid for · " + (nothing ? (line.Ready ? "fuelled and like new: nothing to do" : "nothing to do")
                     : line.Wear < 0.005 ? "like new" + (refuel ? " · refuelled" : "")
                     : "wear " + Core.Wear.Percent(line.Wear) + " · refurbished to 0%" + (refuel ? " and refuelled" : ""));
                // Parts of that ship the build leaves out: refurbished into the parts bin with the rest (Phil, round 4).
                if (line.LeftOut > 0) note += "\n" + Suite.Parts(line.LeftOut) + " left out " + (line.LeftOut == 1 ? "goes" : "go") + " to the parts bin" + (line.LeftOutPrice >= 0.5 ? ", refurbished for " + Pricing.Funds(line.LeftOutPrice) : "");
            }
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label(title, Look.Name);
            if (note != null) GUILayout.Label(note, Look.Meta);
            if (items != null) foreach (string it in items) GUILayout.Label(it, Look.Meta);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical(GUILayout.Width(120));
            if (line.Kind != OwnerKind.New && line.Price < 0.5) GUILayout.Label("No cost", Look.PriceFree);
            else GUILayout.Label(Pricing.Funds(line.Price), Look.Price);
            if (line.Fuel >= 0.5) GUILayout.Label("+ fuel " + Pricing.Funds(line.Fuel), Look.PriceSub);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            Look.Fill(GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true)), Look.Line);
            GUILayout.Space(4);
        }

        /// <summary>New parts, split by design as the reused pieces are (Phil, play-test #15): "Cabral I · 1 new part",
        /// "Shkhara 022 · 31 new parts". Parts from no filed design: "3 new parts".</summary>
        public static string NewTitle(QuoteLine line)
        {
            string design = string.IsNullOrEmpty(line.Design) ? null : BuildsLink.NameOf(line.Design);
            return (design != null ? design + " · " : "") + Suite.Parts(line.Parts).Replace(" part", " new part");
        }

        /// <summary>The bin line's parts: "2 × Mystery Goo Unit", "Augur I Mk. 3".</summary>
        public static List<string> BinItems(QuoteLine line)
        {
            return line.Items.GroupBy(i => Pip.NameOf(i.Part)).Select(g => (g.Count() > 1 ? g.Count() + " × " : "") + g.Key).ToList();
        }

        static void Row(string key, double value, GUIStyle style, string sign = "")
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(key, Look.RowKey);
            GUILayout.Label(sign + Pricing.Funds(value), value == 0 && style == Look.RowVal ? Look.RowVal : style);
            GUILayout.EndHorizontal();
        }

        void PaceButton(Pace p, string title, string sub)
        {
            bool on = Chosen == p;
            if (GUILayout.Button(title + "\n<size=11>" + sub + "</size>", on ? Look.SegOn : Look.Seg, GUILayout.Height(50), GUILayout.ExpandWidth(true))) { Chosen = p; Error = null; }
        }

        string PaceNote()
        {
            if (Chosen == Pace.Overtime) return "Overtime: crews work double shifts. Twice as fast, for " + Mathf.RoundToInt((float)(S.OvertimeCost * 100)) + "% of the build price.";
            if (Chosen == Pace.Outsource) return "Outsourcing: another agency delivers it today, for the whole build price again.";
            PRSScenario sc = PRSScenario.Instance;
            if (sc == null) return "";
            int lines = Suite.Lines(Facility);
            int queued = sc.Jobs.Count(j => j.Facility == Facility && j != Editing);
            if (Editing != null) return sc.ActiveJobs().Contains(Editing) ? "Being built on a " + Facility + " line. The work done so far is kept." : "Waiting for a free " + Facility + " line. The work done so far is kept.";
            return queued < lines ? "Starts now on " + Facility + " line " + (queued + 1) + " of " + lines + "."
                                  : "Waits for a free " + Facility + " line: " + (queued - lines + 1) + " build" + (queued - lines + 1 == 1 ? "" : "s") + " ahead.";
        }

        void DrawFooter()
        {
            GUILayout.BeginHorizontal(Look.Footer);
            GUILayout.BeginVertical();
            if (Error != null) GUILayout.Label(Error, Look.MetaBad);
            else if (Suite.Career && ReadyShip == null)
                GUILayout.Label("Funds " + Pricing.Funds(Suite.Funds) + " → <b>" + Pricing.Funds(Suite.Funds - DueNow) + "</b>", Look.FooterText);
            else if (!Suite.Career) GUILayout.Label("Sandbox: no funds, build time only.", Look.FooterText);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", Look.Ghost, GUILayout.Height(38))) Showing = false;
            GUILayout.Space(8);
            if (ReadyShip != null)
            {
                // Refurbishing is optional (Phil): a fuelled ship with wear can still be refurbished first.
                if (ReadyShip.Wear >= 0.005 && Quote != null && GUILayout.Button("Refurbish · " + Pricing.Funds(DueNow), Look.Ghost, GUILayout.Height(38))) Build();
                GUILayout.Space(8);
                if (GUILayout.Button("Launch", Look.Go, GUILayout.Height(38), GUILayout.MinWidth(140)))
                {
                    Showing = false;
                    EditorAddon.Instance.StockLaunch(ReadyShip);
                }
            }
            else
            {
                if (AsIs != null && !Core.Wear.WornOut(AsIs.Wear))
                {
                    double fuel = PRSScenario.Instance != null ? PRSScenario.Instance.RefuelPrice(AsIs) : 0;
                    if (GUILayout.Button("Launch as it is · " + (fuel < 0.5 ? "free" : "fuel " + Pricing.Funds(fuel)), Look.Ghost, GUILayout.Height(38))) LaunchAsIs();
                    GUILayout.Space(8);
                }
                string verb = Editing != null ? "Update build" : Chosen == Pace.Outsource ? "Outsource" : Chosen == Pace.Overtime ? (AsIs != null ? "Refurbish with overtime" : "Build with overtime") : AsIs != null ? "Refurbish" : "Build";
                string label = verb + " · " + (DueNow < 0 ? "refund " + Pricing.Funds(-DueNow) : Pricing.Funds(DueNow));
                if (OnlyLauncher) GUILayout.Button("Put a ship on it first", Look.GoOff, GUILayout.Height(38), GUILayout.MinWidth(180));
                else if (asking) GUILayout.Button("Answer the parts bin first", Look.GoOff, GUILayout.Height(38), GUILayout.MinWidth(180));
                else if (GUILayout.Button(label, Look.Go, GUILayout.Height(38), GUILayout.MinWidth(180))) Build();
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>KSP's own checks first (its dialogs explain any problem), then the build.</summary>
        void Build()
        {
            Showing = false;
            BuildChecks.Run(() => { if (!Confirm()) Showing = true; }, () => Showing = true);
        }

        /// <summary>A hangar ship exactly as it came home: refuelled (all it pays), then launched with its wear.</summary>
        void LaunchAsIs()
        {
            PRSScenario sc = PRSScenario.Instance;
            if (sc == null || AsIs == null) return;
            if (!sc.Refuel(AsIs, out string err)) { Error = err; return; }
            Showing = false;
            EditorAddon.Instance.StockLaunch(AsIs);
        }
    }
}
