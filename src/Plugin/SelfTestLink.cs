using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using PhilsBuilds;
using PhilsReusabilitySuite.Core;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// An unattended in-game check of build step 5, the link with Phil's Builds, started by the test harness
    /// through the hidden PRSSelfTestLink event. It makes a throwaway career save (zz_PRSSelfTestPB) with one
    /// launcher filed in its Phil's Builds library, then: places it (parts stamped with design slots), builds it,
    /// sees the ready copy in the library rows and the Build panel's "Use it", flies it and loses its chute, picks
    /// the flown copy back up as the whole design (flown parts plus a new chute), and checks an unfiled ship's
    /// "Not filed" warning and that its parts go to the parts bin when it comes home.
    /// </summary>
    class SelfTestLink : MonoBehaviour
    {
        public const string SaveName = "zz_PRSSelfTestPB";
        const string DesignName = "Hopper 001 I";
        static bool running;
        int pass, fail;

        public static void Begin()
        {
            if (running) return;
            running = true;
            var go = new GameObject("PRSSelfTestLink");
            DontDestroyOnLoad(go);
            go.AddComponent<SelfTestLink>();
        }

        void Check(bool ok, string what)
        {
            if (ok) { pass++; Debug.Log("[PRS-TEST] PASS " + what); }
            else { fail++; Debug.LogError("[PRS-TEST] FAIL " + what); }
        }

        static void Note(string s) { Debug.Log("[PRS-TEST] " + s); }
        static bool Near(double a, double b, double tol = 1.5) => Math.Abs(a - b) <= tol;
        static double Funds => Funding.Instance != null ? Funding.Instance.Funds : double.NaN;
        static BuildsLink Link => BuildsLink.Instance;

        void Start() { StartCoroutine(Run()); }

        IEnumerator Run()
        {
            // This test is about the design rule, so it runs with the rule on whatever Settings.cfg says.
            bool designRule = Suite.Settings.RequireDesign;
            Suite.Settings.RequireDesign = true;
            IEnumerator inner = Body();
            while (true)
            {
                object cur;
                try { if (!inner.MoveNext()) break; cur = inner.Current; }
                catch (Exception ex) { Check(false, "the test threw " + ex); break; }
                yield return cur;
            }
            Note("done: " + pass + " passed, " + fail + " failed");
            Suite.Settings.RequireDesign = designRule;
            Debug.Log("[PRS-TEST-DONE]");
            Destroy(gameObject);
        }

        IEnumerator WaitFor(Func<bool> cond, float timeout, string what)
        {
            float t0 = Time.realtimeSinceStartup;
            while (!cond())
            {
                if (Time.realtimeSinceStartup - t0 > timeout) { Check(false, what + " (timed out)"); yield break; }
                yield return null;
            }
        }

        static void Shot(string name)
        {
            string dir = Path.Combine(KSPUtil.ApplicationRootPath, "KSPTestHarness", "out");
            Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "prs_" + name + ".png"));
        }

        IEnumerator ToEditor()
        {
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            EditorDriver.StartupBehaviour = EditorDriver.StartupBehaviours.START_CLEAN;
            EditorDriver.StartEditor(EditorFacility.VAB);
            yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.EDITOR && EditorLogic.fetch != null && EditorAddon.Instance != null, 90, "the VAB opened");
            yield return new WaitForSeconds(3f);
        }

        IEnumerator ToSpaceCenter()
        {
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            HighLogic.LoadScene(GameScenes.SPACECENTER);
            yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.SPACECENTER && PRSScenario.Instance != null, 90, "the Space Center opened");
            yield return new WaitForSeconds(3f);
        }

        static string StockFlea => Path.Combine(KSPUtil.ApplicationRootPath, "Ships", "VAB", "Jumping Flea.craft");

        /// <summary>Files the stock Jumping Flea as a launcher, the way Phil's Builds files one.</summary>
        static void WriteLibrary(string saveDir)
        {
            string subs = Path.Combine(saveDir, "Subassemblies");
            Directory.CreateDirectory(subs);
            string text = File.ReadAllText(StockFlea);
            text = new Regex(@"^ship = .*$", RegexOptions.Multiline).Replace(text, "ship = " + DesignName, 1);
            File.WriteAllText(Path.Combine(subs, DesignName + ".craft"), text);
            File.WriteAllText(Path.Combine(saveDir, "PhilsBuilds.cfg"),
                "PHILS_BUILDS\n{\n\tSUBASSEMBLY\n\t{\n\t\tfile = " + DesignName + "\n\t\tkind = Launcher\n\t\tfamily = Hopper\n\t\tstyle = Roman\n\t\tmark = 1\n\t\trating = 0.01\n\t\twidth = 1.25\n\t}\n}\n");
        }

        /// <summary>What Phil's Builds does after it puts a design's parts in the editor.</summary>
        static void RaisePlaced(Design d, PlaceHow how, IEnumerable<Part> parts, string copyId)
        {
            MethodInfo m = typeof(BuildsApi).GetMethod("RaisePlaced", BindingFlags.Static | BindingFlags.NonPublic);
            m.Invoke(null, new object[] { d, how, parts, copyId });
        }

        IEnumerator Place(Design d)
        {
            EditorLogic.LoadShipFromFile(d.Path);
            yield return new WaitForSeconds(2f);
            RaisePlaced(d, PlaceHow.Load, EditorLogic.fetch.ship.parts, null);
            yield return new WaitForSeconds(0.5f);
        }

        static void StampedPart(Node craft, string designId)
        {
            Node m = craft.AddNode("PART").AddNode("MODULE");
            m.Set("name", Core.Craft.ModuleName);
            m.Set("design", designId);
        }

        static bool Stamped(IList<Part> parts, Design d) =>
            parts.Select((p, i) => PRSPart.Of(p) != null && PRSPart.Of(p).design == d.Id && PRSPart.Of(p).slot == i).All(x => x);

        IEnumerator Recover(RecoverChoice choice)
        {
            var altimeter = FindObjectOfType<KSP.UI.Screens.AltimeterSliderButtons>();
            var recoverMethod = typeof(KSP.UI.Screens.AltimeterSliderButtons).GetMethod("recoverVessel", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(altimeter != null && recoverMethod != null, "found the flight Recover button");
            if (altimeter == null || recoverMethod == null) yield break;
            recoverMethod.Invoke(altimeter, null);
            yield return new WaitForSeconds(1f);
            RecoverPrompt.Current?.TestChoose(choice);
            yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.SPACECENTER && PRSScenario.Instance != null, 120, "recovery returned to the Space Center");
            yield return new WaitForSeconds(4f);
        }

        IEnumerator ToPad(HangarShip h)
        {
            Check(PRSScenario.Instance.LaunchReady(h, out string err), "launching " + h.DisplayName + " " + err);
            yield return WaitFor(() => HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.loaded && !FlightGlobals.ActiveVessel.packed, 120, "it reached the pad");
            yield return new WaitForSeconds(3f);
        }

        IEnumerator Body()
        {
            // 0. KSP's own part renames: an old design file comes out with today's part names.
            // The stock Kerbal 2 was saved before KSP 1.12 renamed its RCS blocks.
            ConfigNode old = ConfigNode.Load(Path.Combine(KSPUtil.ApplicationRootPath, "Ships", "VAB", "Kerbal 2.craft"));
            List<string> before = old?.GetNodes("PART").Select(p => p.GetValue("part")).ToList() ?? new List<string>();
            ConfigNode up = old != null ? BuildsLink.Upgraded(old, "Kerbal 2") : null;
            List<string> after = up?.GetNodes("PART").Select(p => p.GetValue("part")).ToList() ?? new List<string>();
            Check(before.Any(n => n.StartsWith("RCSBlock_")) && after.Any(n => n.StartsWith("RCSBlock.v2_")) && !after.Any(n => n.StartsWith("RCSBlock_")),
                "an old design file's renamed parts read as today's parts (" + before.Count(n => n.StartsWith("RCSBlock_")) + " RCSBlock to " + after.Count(n => n.StartsWith("RCSBlock.v2_")) + " RCSBlock.v2)");

            // 1. A career game with a launcher filed in its Phil's Builds library.
            GameParameters gp = GameParameters.GetDefaultParameters(Game.Modes.CAREER, GameParameters.Preset.Normal);
            Game g = GamePersistence.CreateNewGame(SaveName, Game.Modes.CAREER, gp, "Squad/Flags/default", GameScenes.SPACECENTER, EditorFacility.VAB);
            HighLogic.CurrentGame = g;
            HighLogic.SaveFolder = SaveName;
            WriteLibrary(Path.Combine(KSPUtil.ApplicationRootPath, "saves", SaveName));
            g.startScene = GameScenes.SPACECENTER;
            g.Start();
            yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.SPACECENTER && PRSScenario.Instance != null && Funding.Instance != null, 120, "the career game reached the Space Center");
            yield return new WaitForSeconds(4f);
            Funding.Instance.SetFunds(1000000, TransactionReasons.Cheating);
            Library.Scan();
            Design d = BuildsApi.Designs().FirstOrDefault(x => x.Name == DesignName);
            Check(d != null && d.Kind == DesignKind.Launcher && !string.IsNullOrEmpty(d.Id), "Phil's Builds lists the filed launcher (" + d?.Name + ", " + d?.Kind + ")");
            if (d == null) yield break;
            object provider = typeof(BuildsApi).GetProperty("Copies", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null, null);
            Check(Link != null && ReferenceEquals(provider, Link), "the suite is Phil's Builds' hangar-copies provider");
            IList<CopyRow> rows = Link.CopiesOf(d);
            Check(rows.Count == 1 && rows[0].IsNew && rows[0].Price.Length > 0, "with nothing built, the launcher has just its New row (" + rows.FirstOrDefault()?.Price + ")");
            Check(Link.Note(d) == "", "and no hangar note");

            // 2. Placed in the VAB, every part is stamped with the design and its slot.
            yield return ToEditor();
            yield return Place(d);
            ShipConstruct ship = EditorLogic.fetch.ship;
            Check(ship.parts.Count == 8 && Stamped(ship.parts, d), "all " + ship.parts.Count + " placed parts carry the design and their slot");
            // Picked up into an empty VAB, a launcher keeps KSP's default name; the build takes the design's name.
            ship.shipName = KSP.Localization.Localizer.Format("#autoLOC_900530");
            EditorAddon.Instance.OpenBuildPanel();
            Check(EditorAddon.Instance.Panel.ShipName == DesignName, "a ship called '" + KSP.Localization.Localizer.Format("#autoLOC_900530") + "' that is all one design is built as " + EditorAddon.Instance.Panel.ShipName);
            // Two designs (a payload on a launcher): the name comes from the design at the top, the root part.
            PRSPart lastPart = PRSPart.Of(ship.parts.Last());
            lastPart.design = "another-design";
            EditorAddon.Instance.OpenBuildPanel();
            Check(EditorAddon.Instance.Panel.ShipName == DesignName, "a ship of two designs takes the name of its root part's design (" + EditorAddon.Instance.Panel.ShipName + ")");
            // Its new parts are split by design, as reused pieces are (Phil: "a Cabral on top of a Shkhara").
            var newLines = EditorAddon.Instance.Panel.Quote.Lines.Where(l => l.Kind == OwnerKind.New).ToList();
            Check(newLines.Count == 2 && BuildPanel.NewTitle(newLines[0]) == DesignName + " · 7 new parts" && BuildPanel.NewTitle(newLines[1]) == "1 new part",
                  "new parts split by design: " + string.Join(" / ", newLines.Select(BuildPanel.NewTitle).ToArray()));
            lastPart.design = d.Id;
            EditorAddon.Instance.OpenBuildPanel();
            BuildPanel panel = EditorAddon.Instance.Panel;
            Check(!panel.Unfiled, "a filed design shows no 'Not filed' warning");
            Check(panel.HangarCopy == null, "nothing ready in the hangar to offer yet");
            PRSScenario sc = PRSScenario.Instance;
            Check(panel.Confirm(), "building the Hopper " + panel.Error);
            string jobId = sc.Jobs.LastOrDefault()?.Id;
            double vabPaid = sc.Jobs.LastOrDefault()?.Paid ?? 0;

            // 3. Built, it's a ready copy of the design in the library.
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            BuildJob job = sc.Jobs.FirstOrDefault(j => j.Id == jobId);
            string err = "";
            Check(job != null && sc.Outsource(job, out err), "outsourcing the build " + err);
            HangarShip ready = sc.Hangar.FirstOrDefault(h => h.Ready);
            Check(ready != null && BuildsLink.DesignOf(ready) == d.Id, "the ready ship is a copy of the design (" + ready?.DisplayName + ")");
            if (ready == null) yield break;
            Check(ready.Name == DesignName, "it's named after the design, not 'Untitled Space Craft' (" + ready.Name + ")");
            // A payload stacked on a launcher is a copy of neither design; parts added by hand don't count.
            var mixed = new HangarShip { Craft = new Node("SHIP") };
            var handAdded = new HangarShip { Craft = new Node("SHIP") };
            foreach (string id in new[] { d.Id, d.Id, "another-design" }) StampedPart(mixed.Craft, id);
            foreach (string id in new[] { d.Id, d.Id, "" }) StampedPart(handAdded.Craft, id);
            Check(BuildsLink.DesignOf(mixed) == null, "a ship made of two designs (a payload on a launcher) is a copy of neither");
            Check(BuildsLink.DesignOf(handAdded) == d.Id, "a copy with a part added by hand is still a copy of its design");
            // What a stacked ship is on, for its hangar card: a design built new with it, or another ship's hardware.
            var onDesign = new HangarShip { Id = "under-test-1", Name = "Top", Number = 1, Craft = new Node("SHIP") };
            foreach (string id in new[] { "another-design", d.Id }) StampedPart(onDesign.Craft, id);
            Check(BuildsLink.Under(onDesign) == DesignName, "a payload built new on a launcher is 'on' the launcher's design (" + BuildsLink.Under(onDesign) + ")");
            var onShip = new HangarShip { Id = "under-test-2", Name = "Top", Number = 1, Craft = new Node("SHIP") };
            foreach (string id in new[] { "another-design", d.Id }) StampedPart(onShip.Craft, id);
            onShip.Craft.Children("PART").Last().Children("MODULE").First().Set("hull", "Rockall 002|1");
            Check(BuildsLink.Under(onShip) == "Rockall 002 #1", "stacked on a hangar ship's own hardware, it names that ship (" + BuildsLink.Under(onShip) + ")");
            Check(BuildsLink.Under(ready) == null, "a ship of one piece isn't 'on' anything");
            string readyId = ready.Id;
            rows = Link.CopiesOf(d);
            CopyRow readyRow = rows.FirstOrDefault(r => r.Id == readyId);
            Check(rows.Count == 2 && rows[0].IsNew && readyRow != null && readyRow.Text == "ready · never flown", "the library shows New and the ready copy (" + readyRow?.Label + " " + readyRow?.Text + ")");
            Check(Link.Note(d) == "1 ready in the hangar", "the design's note: " + Link.Note(d));
            CopyDetail det = readyRow != null ? Link.Detail(d, readyRow) : null;
            Check(det != null && det.Title == ready.DisplayName && det.Chips.Any(c => c.Text == "Ready"), "the ready copy's detail panel (" + det?.Title + ")");

            // 3b. Building from Phil's Builds' library at the Space Center, without opening the VAB.
            if (SuiteWindow.Instance != null) { SuiteWindow.Instance.OpenLibrary(); SuiteWindow.Instance.Show = true; }
            yield return new WaitForSeconds(1.5f);
            Shot("suite_library");
            yield return new WaitForSeconds(1f);
            if (SuiteWindow.Instance != null) SuiteWindow.Instance.Show = false;
            // A Space Center build makes what the VAB would: every new part goes through Phil's Iterative Parts' "current
            // Mk" step (play-test #7). Counted with a stand-in for PIP's step; the Hopper's parts are stock.
            Func<Node, bool> realCurrent = Pip.MakeCurrentOf;
            int madeCurrent = 0;
            Pip.MakeCurrentOf = p => { madeCurrent++; return false; };
            List<BuildsLink.LibraryBuild> lib = BuildsLink.Library();
            Pip.MakeCurrentOf = realCurrent;
            BuildsLink.LibraryBuild hop = lib.FirstOrDefault(i => i.Design.Id == d.Id);
            Check(hop != null && madeCurrent >= 8, "every new part of a library build went through PIP's current-Mk step (" + madeCurrent + " parts)");
            Check(hop != null && hop.Blocked == null && hop.Parts == 8 && hop.Ready == 1, "the library lists the Hopper as buildable, with its ready copy (" + hop?.Blocked + ")");
            if (hop == null) yield break;
            Check(Near(hop.Quote.Total, vabPaid, 1), "it costs what the VAB build cost (" + Pricing.Funds(hop.Quote.Total) + " vs " + Pricing.Funds(vabPaid) + ")");
            double fLib = Funds;
            BuildJob libJob = BuildsLink.BuildFromLibrary(hop, false, true, out err);
            Check(libJob != null && libJob.Name == DesignName && libJob.Facility == "VAB", "Build from the library queued " + libJob?.Name + " " + err);
            HangarShip libShip = sc.Hangar.FirstOrDefault(h => h.Ready && h.Id != readyId && h.Name == DesignName);
            Check(libShip != null && BuildsLink.DesignOf(libShip) == d.Id && libShip.Placed.Count == 8 && libShip.Placed.Distinct().Count() == 8, "outsourced, it's a ready copy of the design with 8 parts of its own (" + libShip?.DisplayName + ")");
            Check(Near(fLib - Funds, hop.Quote.Total + Pricing.OutsourceCharge(hop.Quote.BuildPrice, 1, Suite.Settings), 1), "it was charged its price plus outsourcing (" + Pricing.Funds(fLib - Funds) + ")");
            if (libShip != null) sc.Sell(libShip);   // keep the hangar as the next steps expect
            // A design too big for a brand-new career is refused, with the reason.
            Node kerbalX = KspNodes.FromKsp(ConfigNode.Load(Path.Combine(KSPUtil.ApplicationRootPath, "Ships", "VAB", "Kerbal X.craft")));
            string why = BuildsLink.WhyNot(kerbalX, "VAB");
            Check(why != null && (why.StartsWith("It uses the") || why.StartsWith("It has")), "the Kerbal X can't be built in a new career: " + why);

            // 4. The design placed again: the Build panel offers the ready one, and Use it makes the VAB ship that ship.
            yield return ToEditor();
            yield return Place(d);
            EditorAddon.Instance.OpenBuildPanel();
            panel = EditorAddon.Instance.Panel;
            Check(panel.HangarCopy != null && panel.HangarCopy.Id == readyId, "the Build panel offers the ready Hopper");
            yield return new WaitForSeconds(1f);
            Shot("build_panel_use_it");
            yield return new WaitForSeconds(1f);
            panel.UseHangarCopy();
            Check(panel.ReadyShip != null && panel.ReadyShip.Id == readyId, "after Use it, the VAB ship is the ready hangar ship");
            var inVab = EditorLogic.fetch.ship.parts.Select(PRSPart.SerialOf).ToList();
            Check(inVab.Count == ready.Placed.Count && inVab.All(ready.Placed.Contains), "its parts carry the ready ship's serials");
            panel.Showing = false;

            // Leaving with KSP's own exit button and clicking the VAB again: KSP brings the ship back from its
            // cache, and it must still be the ready hangar ship, not new parts.
            typeof(EditorLogic).GetMethod("onExitConfirm", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(EditorLogic.fetch, null);
            yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.SPACECENTER && PRSScenario.Instance != null, 90, "KSP's exit button went to the Space Center");
            yield return new WaitForSeconds(3f);
            var vabBuilding = FindObjectOfType<VehicleAssemblyBuilding>();
            Check(vabBuilding != null, "found the VAB building at the Space Center");
            if (vabBuilding == null) yield break;
            typeof(VehicleAssemblyBuilding).GetMethod("OnClicked", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(vabBuilding, null);
            yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.EDITOR && EditorLogic.fetch != null && EditorAddon.Instance != null && EditorLogic.fetch.ship != null && EditorLogic.fetch.ship.parts.Count > 0, 90, "clicking the VAB brought the ship back");
            yield return new WaitForSeconds(3f);
            var back = EditorLogic.fetch.ship.parts.Select(PRSPart.SerialOf).ToList();
            Check(back.Count == ready.Placed.Count && back.All(ready.Placed.Contains), "back in the VAB, it's still the ready ship's own parts (" + back.Count(ready.Placed.Contains) + " of " + ready.Placed.Count + ")");
            EditorAddon.Instance.OpenBuildPanel();
            Check(EditorAddon.Instance.Panel.ReadyShip != null && EditorAddon.Instance.Panel.ReadyShip.Id == readyId, "and the Build panel still knows it as the ready " + ready.DisplayName);
            EditorAddon.Instance.Panel.Showing = false;

            // 4b. A design saved from parts that are now a hangar ship (Phil's Harris Hawk II was saved from the I's
            // parts): loading it gives new parts. It must never quietly take the hangar ship's hardware.
            string copyPath = Path.Combine(SuiteFiles.SaveDir, "test-saved-from-hangar-parts.craft");
            EditorLogic.fetch.ship.SaveShip().Save(copyPath);
            Check(File.ReadAllText(copyPath).Contains(ready.Placed[0]), "a craft saved in the VAB keeps its parts' serials (this one carries the ready Hopper's)");
            EditorLogic.LoadShipFromFile(copyPath);
            yield return new WaitForSeconds(2f);
            var loaded = EditorLogic.fetch.ship.parts.Select(PRSPart.SerialOf).ToList();
            Check(loaded.Count == ready.Placed.Count && !loaded.Any(ready.Placed.Contains), "loading that craft gives new parts: " + loaded.Count(ready.Placed.Contains) + " of " + loaded.Count + " carry the ready ship's serials");
            RaisePlaced(d, PlaceHow.Load, EditorLogic.fetch.ship.parts, null);
            EditorAddon.Instance.OpenBuildPanel();
            panel = EditorAddon.Instance.Panel;
            Check(panel.ReadyShip == null && panel.Editing == null && panel.Quote != null && panel.Quote.Lines.All(l => l.Kind == OwnerKind.New), "the Build panel prices it as all new parts, not the hangar ship");
            Check(panel.HangarCopy != null && panel.HangarCopy.Id == readyId, "and offers the ready Hopper with Use it instead");
            panel.Showing = false;
            HangarShip readyNow = PRSScenario.Instance.Hangar.FirstOrDefault(h => h.Id == readyId);
            Check(readyNow != null && readyNow.ReservedBy == null, "the ready Hopper is still in the hangar, free");
            if (readyNow == null) yield break;
            // The hangar's own Load keeps a hangar ship's parts; a design merged into it comes in as new parts.
            EditorAddon.LoadHangarShip(readyNow);
            yield return new WaitForSeconds(2f);
            var ownParts = new HashSet<Part>(EditorLogic.fetch.ship.parts);
            Check(ownParts.Count == ready.Placed.Count && ownParts.All(p => ready.Placed.Contains(PRSPart.SerialOf(p))), "the hangar's own Load keeps the ready ship's serials");
            MethodInfo toLoad = typeof(EditorLogic).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "ShipToLoadSelected" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(string));
            Check(toLoad != null, "found KSP's Merge");
            if (toLoad != null)
            {
                toLoad.Invoke(EditorLogic.fetch, new object[] { copyPath, KSP.UI.Screens.CraftBrowserDialog.LoadType.Merge });
                yield return new WaitForSeconds(2f);
                var all = new HashSet<Part>(EditorLogic.fetch.ship.parts);
                Part held = EditorLogic.SelectedPart;
                if (held != null) { all.Add(held); foreach (Part c in held.FindChildParts<Part>(true)) all.Add(c); }
                var merged = all.Where(p => !ownParts.Contains(p)).Select(PRSPart.SerialOf).ToList();
                Check(merged.Count == ready.Placed.Count && !merged.Any(ready.Placed.Contains), "the merged design came in as new parts (" + merged.Count + " parts, " + merged.Count(ready.Placed.Contains) + " with the ready ship's serials)");
                Check(ownParts.All(p => p != null && ready.Placed.Contains(PRSPart.SerialOf(p))), "and the hangar ship already in the VAB kept its own");
                // KSP's New clears the hangar ship away: a design merged into the empty VAB after it is new parts too.
                MethodInfo newShip = typeof(EditorLogic).GetMethod("OnNewShipConfirm", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Check(newShip != null, "found KSP's New");
                if (newShip != null)
                {
                    newShip.Invoke(EditorLogic.fetch, null);
                    yield return new WaitForSeconds(2f);
                    toLoad.Invoke(EditorLogic.fetch, new object[] { copyPath, KSP.UI.Screens.CraftBrowserDialog.LoadType.Merge });
                    yield return new WaitForSeconds(2f);
                    var afterNew = new HashSet<Part>(EditorLogic.fetch.ship.parts);
                    if (EditorLogic.SelectedPart != null) { afterNew.Add(EditorLogic.SelectedPart); foreach (Part c in EditorLogic.SelectedPart.FindChildParts<Part>(true)) afterNew.Add(c); }
                    var newSerials = afterNew.Select(PRSPart.SerialOf).ToList();
                    Check(newSerials.Count == ready.Placed.Count && !newSerials.Any(ready.Placed.Contains), "after New, the same design merged in is new parts (" + newSerials.Count(ready.Placed.Contains) + " of " + newSerials.Count + " with the ready ship's serials)");
                }
                EditorAddon.LoadHangarShip(readyNow);    // drops the merged copy
                yield return new WaitForSeconds(2f);
            }

            // 5. Fly it, lose the chute, bring the rest home: a flown copy that's short one part.
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            ready = sc.Hangar.FirstOrDefault(h => h.Id == readyId);
            if (ready == null) { Check(false, "the ready Hopper is still in the hangar"); yield break; }
            yield return ToPad(ready);
            Vessel v = FlightGlobals.ActiveVessel;
            Part chute = v.parts.FirstOrDefault(p => p.partInfo.name == "parachuteSingle");
            Check(chute != null, "the Hopper has its chute");
            if (chute == null) yield break;
            string chuteSerial = PRSPart.SerialOf(chute);
            int chuteSlot = PRSPart.Of(chute).slot;
            chute.Die();
            yield return new WaitForSeconds(2f);
            var home = v.parts.Select(PRSPart.SerialOf).ToList();
            yield return Recover(RecoverChoice.Hangar);
            sc = PRSScenario.Instance;
            HangarShip flown = sc.Hangar.FirstOrDefault(h => h.Placed.Count == home.Count && h.Placed.All(home.Contains));
            Check(flown != null && BuildsLink.DesignOf(flown) == d.Id && flown.Flights == 1, "a filed design's piece comes home to the hangar (" + flown?.DisplayName + ", " + home.Count + " parts)");
            if (flown == null) yield break;
            // A hop from the pad adds next to no wear: give it 30% so the refurbish has a price to check.
            Check(flown.Wear < 0.02, "a hop from the pad adds next to no wear (" + Core.Wear.Percent(flown.Wear) + ")");
            flown.SetWear(0.3);
            CompletionResult c5 = BuildsLink.Complete(d, flown);
            Check(c5 != null && c5.Used.Count == home.Count && c5.NewSlots.Count == 1 && c5.NewSlots[0] == chuteSlot, "completing it to the design: its own " + c5?.Used.Count + " parts, and a new part in the chute's slot");
            rows = Link.CopiesOf(d);
            CopyRow flownRow = rows.FirstOrDefault(r => r.Id == flown.Id);
            // The chute was destroyed while attached, so the copy is missing it (a spent upper stage wouldn't be).
            Check(flownRow != null && flownRow.Text == "first stage · 1 flight · wear 30%" && flownRow.Warning == "missing 1 part", "its library row (" + flownRow?.Label + " " + flownRow?.Text + " " + flownRow?.Warning + ")");
            Check(Link.Note(d) == "1 flown first stage in the hangar", "the design's note: " + Link.Note(d));
            CopyDetail fd = flownRow != null ? Link.Detail(d, flownRow) : null;
            Check(fd != null && fd.Lines.Any(l => l.Label.StartsWith("1 new part")) && fd.Lines.Any(l => l.Label == "Refurbish to 0% wear") && fd.Chips.Any(ch => ch.Text == "Wear 30%"), "its detail panel shows its wear and prices the refurbish and the new part");
            var openField = typeof(CopyDetail).GetField("Open");
            if (openField != null) Check(fd != null && openField.GetValue(fd) != null, "its detail panel offers Load as stored beside Load");
            else Note("this Phil's Builds has no Load as stored button yet");
            Check(fd != null && fd.Log.Count > 0, "its detail panel shows its flight log (" + (fd?.Log.FirstOrDefault() ?? "none") + ")");
            Quote cq = sc.QuoteFor(c5.Craft, flown.DisplayName, null);
            float chutePrice = PartLoader.getPartInfoByName("parachuteSingle").cost;
            Check(Near(cq.NewPrice, chutePrice, 1) && cq.RefurbishPrice > 0, "the whole Hopper from it: the chute new (" + Pricing.Funds(cq.NewPrice) + " of " + Pricing.Funds(chutePrice) + "), the rest refurbished (" + Pricing.Funds(cq.RefurbishPrice) + ")");
            string flownId = flown.Id, flownHull = flown.Hull;
            string flownDisplay = flown.DisplayName;

            // 6. Picked up in the VAB: the flown parts in their slots plus a new chute.
            yield return ToEditor();
            sc = PRSScenario.Instance;
            flown = sc.Hangar.FirstOrDefault(h => h.Id == flownId);

            // 5b. The plain design loaded in the VAB (Phil, 2026-11-08: loading Gallant I said nothing about the flown
            // Gallant I #1): the Build panel points out the flown copy, short its chute, and "Use it" builds from it,
            // its own parts refurbished and only the chute new. Built, it would stay that ship.
            yield return Place(d);
            EditorAddon.Instance.OpenBuildPanel();
            BuildPanel bp = EditorAddon.Instance.Panel;
            double allNew = bp.Quote?.Total ?? 0, offered = bp.CopyQuote?.Total ?? 0;
            Check(bp.HangarCopy?.Id == flownId && !bp.CopyWhole && bp.CopyQuote != null && offered < allNew, "the Build panel offers the flown Hopper, short its chute, for " + Pricing.Funds(offered) + " instead of " + Pricing.Funds(allNew));
            yield return new WaitForSeconds(1f);
            Shot("build_panel_flown_copy");
            bp.UseHangarCopy();
            Quote uq = bp.Quote;
            Check(uq != null && uq.HangarShips.Contains(flownId) && uq.RefurbishPrice > 0 && (uq.NewPrice < 1 || Near(uq.NewPrice, chutePrice, 1)) && Near(uq.Total, offered, 1),
                  "Use it: the flown parts refurbished (" + Pricing.Funds(uq?.RefurbishPrice ?? 0) + "), only the chute new (" + Pricing.Funds(uq?.NewPrice ?? 0) + "), the price it offered");
            Check(bp.Kept?.Id == flownId && bp.HangarCopy == null, "built from it, it would stay " + flownDisplay + " (" + bp.Kept?.DisplayName + "), and nothing more is offered");
            MethodInfo clearVab = typeof(EditorLogic).GetMethod("OnNewShipConfirm", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            clearVab?.Invoke(EditorLogic.fetch, null);
            yield return new WaitForSeconds(2f);
            flownRow = Link.CopiesOf(d).FirstOrDefault(r => r.Id == flownId);
            ShipConstruct got = null;
            bool called = false;
            if (flownRow != null) Link.Build(d, flownRow, s => { got = s; called = true; });
            yield return WaitFor(() => called, 30, "the hangar copy was built for the cursor");
            yield return new WaitForSeconds(1f);
            Check(got != null && got.parts.Count == 8, "picking up the flown copy gives the whole design (" + got?.parts.Count + " parts)");
            if (got != null)
            {
                RaisePlaced(d, PlaceHow.Copy, got.parts, flownId);
                var mods = got.parts.Select(PRSPart.Of).ToList();
                Check(mods.Count(m => home.Contains(m.serial) && m.flights == 1 && m.hull == flownHull) == home.Count, "the flown parts keep their serials, flight and hull");
                PRSPart nc = got.parts.Where(p => p.partInfo.name == "parachuteSingle").Select(PRSPart.Of).FirstOrDefault();
                Check(nc != null && !string.IsNullOrEmpty(nc.serial) && nc.serial != chuteSerial && nc.flights == 0 && nc.hull == "", "the chute is a new part (" + nc?.serial + ")");
                Check(Stamped(got.parts, d), "every part is in its design slot");
                foreach (Part p in got.parts) Destroy(p.gameObject);
            }

            // 7. An unfiled ship: the warning in the Build panel, and its parts go to the bin when it comes home.
            EditorLogic.LoadShipFromFile(StockFlea);
            yield return new WaitForSeconds(2f);
            EditorAddon.Instance.OpenBuildPanel();
            panel = EditorAddon.Instance.Panel;
            Check(panel.Unfiled, "the stock Jumping Flea, never filed, shows the 'Not filed' warning");
            yield return new WaitForSeconds(1f);
            Shot("build_panel_unfiled");
            yield return new WaitForSeconds(1f);
            sc = PRSScenario.Instance;
            Check(panel.Confirm(), "building it anyway " + panel.Error);
            jobId = sc.Jobs.LastOrDefault()?.Id;
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            job = sc.Jobs.FirstOrDefault(j => j.Id == jobId);
            Check(job != null && sc.Outsource(job, out err), "outsourcing it " + err);
            HangarShip plain = sc.Hangar.FirstOrDefault(h => h.Ready);
            Check(plain != null && BuildsLink.DesignOf(plain) == null, "it's ready in the hangar, a copy of no design");
            if (plain == null) yield break;
            var plainSerials = plain.Placed.ToList();
            int hangarBefore = sc.Hangar.Count - 1;
            yield return ToPad(plain);
            yield return Recover(RecoverChoice.Hangar);
            sc = PRSScenario.Instance;
            Check(!sc.Hangar.Any(h => h.Placed.Any(plainSerials.Contains)) && sc.Hangar.Count == hangarBefore, "the unfiled ship didn't come home to the hangar");
            Check(sc.Bin.Count(b => plainSerials.Contains(b.Serial)) == plainSerials.Count, "all " + plainSerials.Count + " of its parts went to the parts bin");

            // 7b. A ship that comes down while you watch from the Tracking Station is still judged and recovered.
            yield return ToEditor();
            EditorLogic.LoadShipFromFile(StockFlea);
            yield return new WaitForSeconds(2f);
            EditorAddon.Instance.OpenBuildPanel();
            panel = EditorAddon.Instance.Panel;
            sc = PRSScenario.Instance;
            Check(panel.Confirm(), "building a Flea to drop " + panel.Error);
            jobId = sc.Jobs.LastOrDefault()?.Id;
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            job = sc.Jobs.FirstOrDefault(j => j.Id == jobId);
            if (job != null) sc.Outsource(job, out err);
            HangarShip dropper = sc.Hangar.FirstOrDefault(h => h.Ready);
            Check(dropper != null, "the Flea to drop is ready");
            if (dropper == null) yield break;
            yield return ToPad(dropper);
            Vessel dv = FlightGlobals.ActiveVessel;
            uint dropId = dv.persistentId;
            var dropSerials = dv.parts.Select(PRSPart.SerialOf).Where(s => s != null).ToList();
            // KSP's own "set orbit" cheat: up at 80 km over Kerbin and falling (periapsis deep underground).
            CelestialBody kerbin = FlightGlobals.GetHomeBody();
            double rA = kerbin.Radius + 80000, rP = kerbin.Radius - 300000;
            FlightGlobals.fetch.SetShipOrbit(kerbin.flightGlobalsIndex, (rA - rP) / (rA + rP), (rA + rP) / 2, 0, 0, Math.PI, 0, 0);
            yield return new WaitForSeconds(3f);
            Check(!dv.LandedOrSplashed && dv.altitude > 70000, "the Flea is up at " + Math.Round(dv.altitude / 1000) + " km and falling");
            Check(Math.Abs(StageReturns.SurfaceSpeed(dv) - dv.srfSpeed) < 5, "the suite's ground speed from the orbit matches KSP's own in flight (" + Math.Round(StageReturns.SurfaceSpeed(dv)) + " vs " + Math.Round(dv.srfSpeed) + " m/s)");
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            HighLogic.LoadScene(GameScenes.TRACKSTATION);
            yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.TRACKSTATION && StageReturns.Instance != null && PRSScenario.Instance != null, 90, "the Tracking Station opened");
            yield return new WaitForSeconds(2f);
            Vessel tv = FlightGlobals.Vessels.FirstOrDefault(x => x != null && x.persistentId == dropId);
            if (tv != null)
            {
                // Away from flight there's no KSP number to compare with: its orbital speed (vis-viva) is close,
                // within the ground's turning speed (Kerbin's equator moves at 175 m/s).
                double visViva = Math.Sqrt(tv.mainBody.gravParameter * (2 / tv.orbit.radius - 1 / tv.orbit.semiMajorAxis));
                double ground = StageReturns.SurfaceSpeed(tv);
                Check(Math.Abs(ground - visViva) < 250, "in the Tracking Station its ground speed is about its orbital speed (" + Math.Round(ground) + " vs " + Math.Round(visViva) + " m/s)");
            }
            int outcomesBefore = StageReturns.Instance.Outcomes.Count;
            TimeWarp.SetRate(3, true, false);
            yield return WaitFor(() => !FlightGlobals.Vessels.Any(x => x != null && x.persistentId == dropId), 120, "KSP deleted the falling Flea in the Tracking Station");
            TimeWarp.SetRate(0, true, false);
            yield return new WaitForSeconds(2f);
            string outcome = StageReturns.Instance != null ? StageReturns.Instance.Outcomes.Skip(outcomesBefore).FirstOrDefault() : null;
            Check(outcome != null, "the suite judged its landing in the Tracking Station: " + (outcome ?? "nothing"));
            sc = PRSScenario.Instance;
            bool lostIt = outcome != null && outcome.Contains(" was lost");
            // A hard landing (6 to 12 m/s) can break outer parts off: those are lost, the rest go to the bin.
            int inBin = sc.Bin.Count(b => dropSerials.Contains(b.Serial));
            Check(outcome != null && (lostIt || (inBin > 0 && inBin <= dropSerials.Count)), "it came home to the parts bin (it was never filed): " + inBin + " parts in the bin, " + (dropSerials.Count - inBin) + " lost in the landing");
            Check(outcome == null || lostIt || outcome.Contains("filed in Phil's Builds"), "the message says why it went to the bin");

            // 7c. Restore to design from the hangar: the flown Hopper gets its chute back (new, or one from the bin)
            // and is ready again as itself, with its own flown parts.
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            // A price made before a bin part was sold doesn't hold: the build is refused, not charged the old price.
            BuildsLink.LibraryBuild stale = BuildsLink.Library().FirstOrDefault(i => i.Design.Id == d.Id);
            if (stale != null && stale.Quote.BinSerials.Count > 0)
            {
                string gone = stale.Quote.BinSerials[0];
                sc.Sell(sc.Bin.Where(b => b.Serial == gone).ToList());
                double fStale = Funds;
                BuildJob staleJob = BuildsLink.BuildFromLibrary(stale, false, false, out string staleErr);
                Check(staleJob == null && staleErr != null && staleErr.Contains("changed") && Near(Funds, fStale, 0.5), "a library build priced with a bin part that was then sold is refused, nothing charged (" + staleErr + ")");
            }
            else Note("no bin parts matched the Hopper here, so the stale-price check was skipped");
            HangarShip toRestore = sc.Hangar.FirstOrDefault(h => h.Id == flownId);
            Func<Node, bool> realStep = Pip.MakeCurrentOf;
            int restoredCurrent = 0;
            Pip.MakeCurrentOf = p => { restoredCurrent++; return false; };
            BuildsLink.Restore rs = toRestore != null ? BuildsLink.RestoreOf(toRestore) : null;
            Pip.MakeCurrentOf = realStep;
            Check(rs == null || restoredCurrent == rs.NewParts, "Restore brings only its new parts to the current Mk; its own keep theirs (" + restoredCurrent + " of " + rs?.NewParts + ")");
            if (toRestore != null && SuiteWindow.Instance != null) SuiteWindow.Instance.ShowHangarShip(toRestore.Id);
            yield return new WaitForSeconds(3f);
            Shot("suite_restore");
            yield return new WaitForSeconds(1f);
            if (SuiteWindow.Instance != null) SuiteWindow.Instance.Show = false;
            Check(rs != null && rs.NewParts == 1 && rs.Blocked == null && rs.Design.Id == d.Id, "Restore to design: the flown Hopper needs 1 part (" + rs?.NewParts + ") " + rs?.Blocked);
            if (rs != null)
            {
                bool chuteFromBin = rs.Quote.BinSerials.Count == 1;
                Check(rs.Quote.RefurbishPrice > 0 && (chuteFromBin ? rs.Quote.NewPrice < 1 : Near(rs.Quote.NewPrice, chutePrice, 1)), "it costs a chute (" + (chuteFromBin ? "from the bin" : "new, " + Pricing.Funds(rs.Quote.NewPrice)) + ") and the refurbish of the rest (" + Pricing.Funds(rs.Quote.RefurbishPrice) + ")");
                BuildJob rj = BuildsLink.RestoreBuild(toRestore, rs, false, true, out err);
                Check(rj != null, "restoring it " + err);
                HangarShip restored = sc.Hangar.FirstOrDefault(h => h.Id == flownId);
                Check(restored != null && restored.Ready && restored.Placed.Count == 8 && home.All(restored.Placed.Contains), "it's ready again with its own flown parts and a chute (" + restored?.Placed.Count + " parts)");
                Check(restored != null && restored.DisplayName == flownDisplay, "as the same ship (" + restored?.DisplayName + ")");
                Check(restored != null && restored.Wear == 0 && restored.Craft.Children("PART").All(p => Core.Wear.In(p.Children("MODULE").FirstOrDefault(m => m.Get("name") == Core.Craft.ModuleName)) == 0), "restoring refurbishes the rest too: the whole craft leaves at 0% wear");
            }

            // 8. The VAB landing check on the stock Kerbal X: launch clamps stay on the pad, and the same ship
            // measures the same every time it's checked.
            yield return ToEditor();
            EditorLogic.LoadShipFromFile(Path.Combine(KSPUtil.ApplicationRootPath, "Ships", "VAB", "Kerbal X.craft"));
            yield return new WaitForSeconds(3f);
            ShipConstruct kx = EditorLogic.fetch.ship;
            var clampNames = new HashSet<string>(kx.parts.Where(p => p.FindModuleImplementing<LaunchClamp>() != null).Select(p => p.partInfo.name));
            List<StageReturns.EditorPiece> first = StageReturns.CheckEditorShip(kx);
            foreach (StageReturns.EditorPiece pc in first) Note("landing check: " + pc.Count + " × " + pc.Label + " (" + pc.Parts + " parts): " + pc.Speed.ToString("0.0") + " m/s");
            Check(clampNames.Count > 0 && first.Count > 0 && first.All(pc => !pc.Names.Any(clampNames.Contains)), "no dropping piece includes a launch clamp (" + kx.parts.Count(p => clampNames.Contains(p.partInfo.name)) + " clamps on the ship)");
            // Its heat shield's jettison isn't staged, so the shield stays with the pod (Phil's Argo showed it as a
            // one-part piece of its own).
            Check(kx.parts.Any(p => p.partInfo.name == "HeatShield2") && !first.Any(pc => pc.Names.Contains("HeatShield2")), "the heat shield isn't a dropping piece of its own: its jettison isn't staged");
            Check(first.All(pc => pc.Label.StartsWith("Stage with ")), "pieces are called stages (" + first.FirstOrDefault()?.Label + ")");
            bool same = true;
            for (int i = 0; i < 4; i++)
            {
                EditorAddon.Instance.OpenBuildPanel();
                yield return new WaitForSeconds(1f);
                EditorAddon.Instance.Panel.Showing = false;
                List<StageReturns.EditorPiece> again = StageReturns.CheckEditorShip(kx);
                for (int j = 0; j < first.Count && j < again.Count; j++)
                    if (Math.Abs(again[j].Speed - first[j].Speed) > first[j].Speed * 0.005) { same = false; Note("landing check moved: " + first[j].Label + " " + first[j].Speed.ToString("0.0") + " → " + again[j].Speed.ToString("0.0") + " m/s"); }
                if (again.Count != first.Count) same = false;
            }
            Check(same, "checking the same ship five times gives the same landing speeds");
            // (The stage-recovery test checks that a piece measured on its own before it drops reads the same as
            // the real piece measured after it has dropped.)

            // A stage that can steer and has liquid engines is told how much propellant to keep for a landing
            // burn: the Z-MAP's middle stage (a QBE probe core and a Terrier), which lands that way in the
            // powered-landing test.
            EditorLogic.LoadShipFromFile(Path.Combine(KSPUtil.ApplicationRootPath, "Ships", "VAB", "Z-MAP Satellite Launch Kit.craft"));
            yield return new WaitForSeconds(3f);
            List<StageReturns.EditorPiece> zmap = StageReturns.CheckEditorShip(EditorLogic.fetch.ship);
            foreach (StageReturns.EditorPiece pc in zmap) Note("Z-MAP landing check: " + pc.Label + " (" + pc.Parts + " parts): " + pc.Speed.ToString("0") + " m/s, " + (pc.CanBurn ? (pc.TooWeakToBurn ? "engines too weak" : "keep " + pc.BurnPropellant.ToString("0.000") + " t") : "can't burn"));
            StageReturns.EditorPiece middle = zmap.FirstOrDefault(pc => pc.Names.Contains("probeCoreCube"));
            Check(middle != null && middle.CanBurn && !middle.TooWeakToBurn && middle.BurnPropellant > 0.01 && middle.BurnPropellant < 2, "the Z-MAP's middle stage is told to keep " + (middle != null ? middle.BurnPropellant.ToString("0.00") : "?") + " t of propellant for its landing burn");
            EditorAddon.Instance.OpenBuildPanel();
            yield return new WaitForSeconds(1f);
            Shot("build_panel_landing_burn");
            yield return new WaitForSeconds(1f);
            EditorAddon.Instance.Panel.Showing = false;
        }
    }
}
