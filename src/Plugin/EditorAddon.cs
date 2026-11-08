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
    /// In the VAB and SPH: keeps part serials honest, turns the green Launch button into Build (it opens the
    /// Build panel), and shows the suite window from the toolbar.
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
            PRSPart.EditorTrusted.Clear();
            // Coming back to the editor restores the ship that was there: its hangar parts stay hangar parts.
            if (EditorDriver.StartupBehaviour == EditorDriver.StartupBehaviours.LOAD_FROM_CACHE) PRSPart.EditorTrusted.UnionWith(PRSPart.EditorCarry);
            GameEvents.onEditorShipModified.Add(OnShipModified);
            GameEvents.onEditorLoad.Add(OnEditorLoad);
            window = gameObject.AddComponent<SuiteWindow>();
            StartCoroutine(HookLaunchButton());
        }

        void OnDestroy()
        {
            GameEvents.onEditorShipModified.Remove(OnShipModified);
            GameEvents.onEditorLoad.Remove(OnEditorLoad);
            ShipConstruct ship = EditorLogic.fetch != null ? EditorLogic.fetch.ship : null;
            PRSPart.EditorCarry.Clear();
            if (ship != null)
                foreach (Part p in ship.parts)
                {
                    string s = PRSPart.SerialOf(p);
                    if (s != null && PRSPart.EditorTrusted.Contains(s)) PRSPart.EditorCarry.Add(s);
                }
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

        /// <summary>Runs the stock launch (for a ready hangar ship, which launches free).</summary>
        public void StockLaunch()
        {
            if (stockLaunch != null) stockLaunch.Invoke();
            else EditorLogic.fetch.launchVessel();
        }

        // ---- Serials ----

        void OnEditorLoad(ShipConstruct ship, CraftBrowserDialog.LoadType type) { Check(ship); }
        void OnShipModified(ShipConstruct ship) { Check(ship); }

        /// <summary>
        /// Designs never carry specific hardware: any serial the editor didn't give out or load from the hangar
        /// (a saved craft or subassembly) gets replaced, and so does a duplicate.
        /// </summary>
        static void Check(ShipConstruct ship)
        {
            if (ship == null) return;
            var seen = new HashSet<string>();
            int renewed = 0;
            foreach (Part p in ship.parts)
            {
                PRSPart m = PRSPart.Of(p);
                if (m == null) continue;
                bool trusted = !string.IsNullOrEmpty(m.serial) && PRSPart.EditorTrusted.Contains(m.serial);
                if (!trusted || !seen.Add(m.serial))
                {
                    m.MakeNew();
                    seen.Add(m.serial);
                    renewed++;
                }
            }
            if (renewed > 0) Log.Info("Gave " + renewed + " parts new serial numbers (new parts, not hangar hardware)");
        }

        /// <summary>Loads a craft into the editor, keeping its parts' serials (a hangar ship or a queued build).</summary>
        public static void LoadKeepingSerials(Node craft, string shipName, IEnumerable<string> serials, string fileTag)
        {
            ConfigNode c = KspNodes.ToKsp(craft, "");
            c.SetValue("ship", shipName, true);
            string path = System.IO.Path.Combine(SuiteFiles.SaveDir, fileTag + ".craft");
            c.Save(path);
            // Trusted before loading, so it doesn't matter when the editor's load events fire.
            foreach (string s in serials) PRSPart.EditorTrusted.Add(s);
            EditorLogic.LoadShipFromFile(path);
        }

        public static void LoadHangarShip(HangarShip h)
        {
            LoadKeepingSerials(h.Craft, h.DisplayName, h.Placed, "hangar-" + h.Id);
            Log.Info("Loaded " + h.DisplayName + " from the hangar (" + h.Placed.Count + " parts)");
        }

        // ---- Build panel ----

        public BuildPanel Panel { get; private set; }

        public void OpenBuildPanel()
        {
            if (Panel == null) Panel = gameObject.AddComponent<BuildPanel>();
            Panel.Open();
        }
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
        public Pace Chosen = Pace.Normal;
        public string Error;

        Rect rect = new Rect(0, 0, 600, 100);
        Vector2 scroll;

        static Core.Settings S => Suite.Settings;

        /// <summary>Works out the quote for the ship in the editor and shows the panel.</summary>
        public void Open()
        {
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
            Craft = KspNodes.FromKsp(ship.SaveShip());
            ShipName = KSP.Localization.Localizer.Format(ship.shipName);
            Facility = ship.shipFacility == EditorFacility.SPH ? "SPH" : "VAB";
            var serials = ship.parts.Select(PRSPart.SerialOf).Where(s => s != null).ToList();
            ReadyShip = sc?.ReadyShipOf(serials);
            Editing = ReadyShip == null ? sc?.JobOf(serials) : null;
            Quote = sc?.QuoteFor(Craft, ShipName, Editing);
            try { Landings = StageReturns.CheckEditorShip(ship); }
            catch (System.Exception ex) { Landings = new List<StageReturns.EditorPiece>(); Log.Warn("Landing check failed: " + ex.Message); }
            FindReadyCopy(ship);
            try
            {
                bool anyUnstamped = ship.parts.Any(p => PRSPart.Of(p) != null && string.IsNullOrEmpty(PRSPart.Of(p).design));
                Unfiled = PhilsBuilds.BuildsApi.Current == null && anyUnstamped;
            }
            catch (System.Exception) { Unfiled = false; }
        }

        /// <summary>The ship (or part of it) isn't from a design filed in Phil's Builds.</summary>
        public bool Unfiled;
        /// <summary>A ready, never-changed hangar copy of a design that's in the editor as new parts.</summary>
        public HangarShip ReadyCopy;
        string readyCopyDesign;

        void FindReadyCopy(ShipConstruct ship)
        {
            ReadyCopy = null;
            readyCopyDesign = null;
            PRSScenario sc = PRSScenario.Instance;
            if (sc == null || ReadyShip != null) return;
            // Designs in the editor whose parts are all new (not already hangar hardware).
            var groups = ship.parts.Select(PRSPart.Of).Where(m => m != null && !string.IsNullOrEmpty(m.design))
                .GroupBy(m => m.design).ToList();
            foreach (var g in groups)
            {
                if (g.Any(m => sc.OwnerOf(m.serial, Editing) != null)) continue;
                var slots = new HashSet<int>(g.Select(m => m.slot));
                foreach (HangarShip h in sc.Hangar.Where(x => x.Ready && x.ReservedBy == null && BuildsLink.DesignOf(x) == g.Key))
                {
                    var hSlots = new HashSet<int>(h.Craft.Children("PART").Select(SlotIn).Where(s => s >= 0));
                    if (hSlots.SetEquals(slots) && hSlots.Count == g.Count())
                    {
                        ReadyCopy = h;
                        readyCopyDesign = g.Key;
                        return;
                    }
                }
            }
        }

        static int SlotIn(Node part)
        {
            Node m = part.Children("MODULE").FirstOrDefault(x => x.Get("name") == Core.Craft.ModuleName);
            return int.TryParse(m?.Get("slot"), out int s) ? s : -1;
        }

        /// <summary>
        /// "Use it": the parts in the editor built from that design become the ready hangar copy's own parts,
        /// slot by slot (same design, same slots), so the build takes the finished ship instead of new parts.
        /// </summary>
        public void UseReadyCopy()
        {
            if (ReadyCopy == null || readyCopyDesign == null) return;
            var bySlot = new Dictionary<int, Node>();
            foreach (Node p in ReadyCopy.Craft.Children("PART"))
            {
                Node m = p.Children("MODULE").FirstOrDefault(x => x.Get("name") == Core.Craft.ModuleName);
                if (m != null && m.Get("design") == readyCopyDesign) bySlot[SlotIn(p)] = m;
            }
            foreach (Part part in EditorLogic.fetch.ship.parts)
            {
                PRSPart m = PRSPart.Of(part);
                if (m == null || m.design != readyCopyDesign || !bySlot.TryGetValue(m.slot, out Node hm)) continue;
                m.serial = hm.Get("serial");
                m.flights = int.TryParse(hm.Get("flights"), out int f) ? f : 0;
                m.hull = hm.Get("hull") ?? "";
                PRSPart.EditorTrusted.Add(m.serial);
            }
            Log.Info("Using the ready " + ReadyCopy.DisplayName + " from the hangar in this build");
            Refresh();
        }

        public List<StageReturns.EditorPiece> Landings = new List<StageReturns.EditorPiece>();

        void DrawLandings()
        {
            if (Landings == null || Landings.Count == 0) return;
            GUILayout.Space(8);
            GUILayout.Label("LANDING CHECK · pieces that drop off, empty, with every chute open", Look.Section);
            foreach (StageReturns.EditorPiece p in Landings)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label((p.Count > 1 ? p.Count + " × " : "") + p.Label + " <color=#7d88a3>(" + p.Parts + " parts)</color>", Look.Meta, GUILayout.Width(330));
                string how = p.Chutes > 0 ? " on " + p.Chutes + (p.Chutes == 1 ? " chute" : " chutes") : " with no chutes";
                if (p.Speed <= Core.Landing.SafeSpeed) GUILayout.Label("comes home: " + p.Speed.ToString("0.0") + " m/s" + how, Look.MetaGood);
                else if (p.Speed <= Core.Landing.LostSpeed) GUILayout.Label("comes home hard: " + p.Speed.ToString("0.0") + " m/s" + how, Look.MetaWarn);
                else GUILayout.Label(p.CanBurn ? "needs a landing burn: " + p.Speed.ToString("0") + " m/s" + how : "lost: " + p.Speed.ToString("0") + " m/s" + how, p.CanBurn ? Look.MetaWarn : Look.MetaBad);
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>Presses Build (or Update build) with the chosen pace. Returns false with Error set if it couldn't.</summary>
        public bool Confirm()
        {
            PRSScenario sc = PRSScenario.Instance;
            if (sc == null || Quote == null) { Error = "The suite's save data isn't loaded."; return false; }
            if (Quote.PartCount == 0) { Error = "There's nothing to build."; return false; }
            string error;
            if (Editing != null)
            {
                if (!sc.UpdateBuild(Editing, Craft, ShipName, Quote, out error)) { Error = error; return false; }
                if (Chosen == Pace.Overtime && !Editing.Overtime && sc.Jobs.Contains(Editing)) sc.SetOvertime(Editing, true, out error);
                if (Chosen == Pace.Outsource && sc.Jobs.Contains(Editing)) sc.Outsource(Editing, out error);
                Suite.Message(Editing.Name + ": build updated");
            }
            else
            {
                BuildJob job = sc.StartBuild(Craft, ShipName, Facility, Quote, Chosen == Pace.Overtime, Chosen == Pace.Outsource, null, out error);
                if (job == null) { Error = error; return false; }
                if (Chosen != Pace.Outsource) Suite.Message(ShipName + " is being built: " + Pricing.Duration(job.SecondsLeft(S), S));
            }
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
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Space(10);
            GUILayout.BeginHorizontal(); GUILayout.Space(16); GUILayout.BeginVertical();

            if (ReadyShip != null) { DrawReady(); EndBody(); return; }

            GUILayout.Label(Editing != null ? "Update build" : "Ready to build", Look.Eyebrow);
            GUILayout.Label(ShipName, Look.H2);
            GUILayout.Space(6);
            if (ReadyCopy != null && Quote != null)
            {
                GUILayout.BeginHorizontal(Look.NoticeGood);
                GUILayout.Label("A ready <b>" + ReadyCopy.DisplayName + "</b> is in the hangar, built and never changed. Use it instead of building new parts?", Look.Meta, GUILayout.Width(430));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Use it", Look.Go, GUILayout.Height(30), GUILayout.Width(90))) UseReadyCopy();
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
            }
            if (Unfiled)
            {
                GUILayout.BeginHorizontal(Look.Notice);
                GUILayout.Label("<color=#ffcf5a>Not filed in Phil's Builds.</color> Pieces of this ship that aren't from a filed design go to the parts bin when they come home.", Look.Meta, GUILayout.Width(430));
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

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(330), GUILayout.MinHeight(Mathf.Min(330, 70 * Quote.Lines.Count)));
            foreach (QuoteLine line in Quote.Lines) DrawLine(line);
            GUILayout.EndScrollView();
            GUILayout.Label("Reused parts cost only their refit. New parts cost their full price.", Look.Hint);
            DrawLandings();

            GUILayout.Space(8);
            GUILayout.BeginVertical(Look.Notice);
            Row("New parts", Quote.NewPrice, Look.RowVal);
            Row("Refit of reused parts", Quote.RefitPrice, Look.RowVal);
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
            GUILayout.Label(ReadyShip.DisplayName + " is built and ready, so launching it costs nothing. Change anything and it becomes a build again.", Look.NoticeGood);
        }

        void DrawLine(QuoteLine line)
        {
            bool reused = line.Kind != OwnerKind.New;
            GUILayout.BeginHorizontal(reused ? Look.CardReuse : Look.Card);
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label(line.Label, Look.Name, GUILayout.ExpandWidth(false));
            GUILayout.Space(6);
            if (line.Kind == OwnerKind.New) Look.Chip("New", Look.ChipNew);
            else if (line.Kind == OwnerKind.Bin) Look.Chip("From the bin", Look.ChipReuse);
            else if (line.RefitDone) Look.Chip("In the hangar", Look.ChipReuse);
            else Look.Chip("Reused · " + line.MaxFlights + (line.MaxFlights == 1 ? " flight" : " flights"), Look.ChipReuse);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            string meta = line.Parts + (line.Parts == 1 ? " part" : " parts");
            if (line.Kind == OwnerKind.Hangar && line.RefitDone) meta += " · ready, nothing to build";
            else if (line.Kind != OwnerKind.New) meta += " · refit " + Mathf.RoundToInt((float)(Pricing.RefitShare(line.MaxFlights, S) * 100)) + "%: " + Mathf.RoundToInt((float)(S.RefitBase * 100)) + "% plus " + Mathf.RoundToInt((float)(S.RefitPerFlight * 100)) + "% for each flight";
            else meta += " · built new";
            GUILayout.Label(meta, Look.Meta);
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUILayout.Width(120));
            if (reused && line.Price < 0.5) GUILayout.Label("No cost", Look.PriceFree);
            else GUILayout.Label(Pricing.Funds(line.Price), Look.Price);
            if (line.Kind != OwnerKind.New && !line.RefitDone) GUILayout.Label("refit", Look.PriceSub);
            if (line.Fuel >= 0.5) GUILayout.Label("+ fuel " + Pricing.Funds(line.Fuel), Look.PriceSub);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
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
                if (GUILayout.Button("Launch", Look.Go, GUILayout.Height(38), GUILayout.MinWidth(140)))
                {
                    Showing = false;
                    EditorAddon.Instance.StockLaunch();
                }
            }
            else
            {
                string verb = Editing != null ? "Update build" : Chosen == Pace.Outsource ? "Outsource" : Chosen == Pace.Overtime ? "Build with overtime" : "Build";
                string label = verb + " · " + (DueNow < 0 ? "refund " + Pricing.Funds(-DueNow) : Pricing.Funds(DueNow));
                if (GUILayout.Button(label, Look.Go, GUILayout.Height(38), GUILayout.MinWidth(180)))
                {
                    // KSP's own checks first; its dialogs explain any problem. Then build.
                    Showing = false;
                    BuildChecks.Run(() => { if (!Confirm()) Showing = true; }, () => Showing = true);
                }
            }
            GUILayout.EndHorizontal();
        }
    }
}
