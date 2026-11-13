using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhilsReusabilitySuite.Core;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// An unattended in-game check of build step 2, started by the test harness through the hidden
    /// PRSSelfTestBuild event. It makes a throwaway career save (zz_PRSSelfTest) and runs the build queue:
    /// Build in the VAB, a second build waiting with overtime, time passing at the KSC, outsourcing, launching
    /// a ready ship free, recovering it to the hangar, wear and a refurbish cancelled for a refund, refuelling to fly as it is.
    /// </summary>
    class SelfTestBuild : MonoBehaviour
    {
        public const string SaveName = "zz_PRSSelfTest";
        static bool running;
        int pass, fail;

        public static void Begin()
        {
            if (running) return;
            running = true;
            var go = new GameObject("PRSSelfTestBuild");
            DontDestroyOnLoad(go);
            go.AddComponent<SelfTestBuild>();
        }

        void Check(bool ok, string what)
        {
            if (ok) { pass++; Debug.Log("[PRS-TEST] PASS " + what); }
            else { fail++; Debug.LogError("[PRS-TEST] FAIL " + what); }
        }

        static void Note(string s) { Debug.Log("[PRS-TEST] " + s); }
        static bool Near(double a, double b, double tol = 1.5) => Math.Abs(a - b) <= tol;
        static Core.Settings S => Suite.Settings;
        static double Funds => Funding.Instance != null ? Funding.Instance.Funds : double.NaN;

        void Start() { StartCoroutine(Run()); }

        IEnumerator Run()
        {
            // The test craft are stock ships, not filed in Phil's Builds: let their pieces come home to the hangar.
            bool designRule = Suite.Settings.RequireDesign;
            Suite.Settings.RequireDesign = false;
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
            // Entering a building from the Space Center saves the game first, as KSP's own buttons do.
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

        /// <summary>Phil (S10): scrolling over a suite window scrolls only the window. Puts the window under the mouse, then
        /// off it, and checks the window's own lock holds the camera (the scroll-wheel zoom) only while the mouse is over it.</summary>
        IEnumerator ScrollStaysInWindow(Action<Vector2?> offset, string lockId, string what, ControlTypes alsoHeld)
        {
            offset(new Vector2(-10, -10));
            yield return new WaitForSeconds(0.5f);
            ControlTypes held = InputLockManager.GetControlLock(lockId);
            Check((held & ControlTypes.CAMERACONTROLS) != 0 && (held & alsoHeld) == alsoHeld && InputLockManager.IsLocked(ControlTypes.CAMERACONTROLS),
                "with the mouse over " + what + ", the camera's scroll zoom and the scene under it are held (" + held + ")");
            offset(new Vector2(40, 40));
            yield return new WaitForSeconds(0.5f);
            Check((InputLockManager.GetControlLock(lockId) & ControlTypes.CAMERACONTROLS) == 0, "with the mouse off " + what + ", the camera is free again");
            offset(null);
        }

        IEnumerator LoadFlea()
        {
            EditorLogic.LoadShipFromFile(Path.Combine(KSPUtil.ApplicationRootPath, "Ships", "VAB", "Jumping Flea.craft"));
            yield return new WaitForSeconds(2f);
        }

        IEnumerator Body()
        {
            // 1. A career game of the test's own.
            GameParameters gp = GameParameters.GetDefaultParameters(Game.Modes.CAREER, GameParameters.Preset.Normal);
            Game g = GamePersistence.CreateNewGame(SaveName, Game.Modes.CAREER, gp, "Squad/Flags/default", GameScenes.SPACECENTER, EditorFacility.VAB);
            HighLogic.CurrentGame = g;
            HighLogic.SaveFolder = SaveName;
            g.startScene = GameScenes.SPACECENTER;
            g.Start();
            yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.SPACECENTER && PRSScenario.Instance != null && Funding.Instance != null, 120, "the career game reached the Space Center");
            yield return new WaitForSeconds(4f);
            Check(Suite.Career, "a career game with funds");
            Funding.Instance.SetFunds(1000000, TransactionReasons.Cheating);
            Check(Suite.Lines("VAB") == 1 && Suite.Lines("SPH") == 1, "a level-1 VAB and SPH have one build line each");

            // 2. Build the stock Jumping Flea in the VAB.
            yield return ToEditor();
            yield return LoadFlea();
            ShipConstruct ship = EditorLogic.fetch.ship;
            Check(ship.parts.Count > 0, "the Jumping Flea is in the VAB (" + ship.parts.Count + " parts)");
            Check(Suite.Lines("VAB") == 1, "the VAB still shows one build line from inside the VAB (" + Suite.Lines("VAB") + ")");
            ship.GetShipCosts(out float dry, out float fuel);
            EditorAddon.Instance.OpenBuildPanel();
            BuildPanel panel = EditorAddon.Instance.Panel;
            Quote q1 = panel.Quote;
            Check(q1 != null && Near(q1.NewPrice, dry), "new parts cost KSP's own dry price (" + q1?.NewPrice + " vs " + dry + ")");
            Check(q1 != null && Near(q1.FuelPrice, fuel), "fuel costs KSP's own fuel price (" + q1?.FuelPrice + " vs " + fuel + ")");
            Check(q1 != null && q1.RefurbishPrice == 0 && panel.Editing == null && panel.ReadyShip == null, "a fresh design: nothing to refurbish, not a queued build, not a ready ship");
            yield return new WaitForSeconds(1f);
            Shot("build_panel_new");
            yield return new WaitForSeconds(1f);

            bool checksPassed = false;
            BuildChecks.Run(() => checksPassed = true, () => { });
            yield return new WaitForSeconds(1f);
            Check(checksPassed, "KSP's own pre-launch checks pass for the Jumping Flea");

            PRSScenario sc = PRSScenario.Instance;
            double f0 = Funds;
            panel.Chosen = BuildPanel.Pace.Normal;
            Check(panel.Confirm(), "pressing Build starts a build " + panel.Error);
            BuildJob job1 = sc.Jobs.LastOrDefault();
            Check(job1 != null && Near(f0 - Funds, q1.Total), "Build charged the price once (" + Pricing.Funds(f0 - Funds) + " of " + Pricing.Funds(q1.Total) + ")");
            Check(job1 != null && Near(job1.SecondsLeft(S), Pricing.BuildSeconds(q1.BuildPrice, S), 1), "build time follows the formula (" + Pricing.Duration(job1?.SecondsLeft(S) ?? 0, S) + ")");

            EditorAddon.Instance.OpenBuildPanel();
            Check(panel.Editing == job1, "the ship still in the VAB is recognised as the queued build");
            Check(Math.Abs(panel.Quote.Total - job1.Paid) < 1, "the same ship has nothing more to pay");
            panel.Showing = false;

            // Changed in the VAB (half its solid fuel), it's still that queued build: the difference is settled.
            Part srb = EditorLogic.fetch.ship.parts.FirstOrDefault(p => p.Resources.Contains("SolidFuel"));
            Check(srb != null, "the Flea in the VAB has its solid booster");
            if (srb != null)
            {
                srb.Resources["SolidFuel"].amount = srb.Resources["SolidFuel"].maxAmount / 2;
                GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
                yield return new WaitForSeconds(0.5f);
                EditorAddon.Instance.OpenBuildPanel();
                Check(panel.Editing == job1, "the changed ship is still the queued build");
                double paidBefore = job1.Paid, fEdit = Funds, newTotal = panel.Quote.Total;
                Check(panel.Confirm(), "updating the queued build " + panel.Error);
                Check(newTotal < paidBefore - 0.5 && Near(job1.Paid, newTotal, 1) && Near(Funds - fEdit, paidBefore - newTotal, 1) && sc.Jobs.Contains(job1),
                      "half the solid fuel costs less: " + Pricing.Funds(paidBefore - newTotal) + " back, and it's the same build");
            }

            // A second Jumping Flea (fresh serials from the file), with overtime: it waits for the one VAB line.
            yield return LoadFlea();
            EditorAddon.Instance.OpenBuildPanel();
            Check(panel.Editing == null, "a design loaded from file is a new build, not the queued one");
            Quote q2 = panel.Quote;
            panel.Chosen = BuildPanel.Pace.Overtime;
            yield return new WaitForSeconds(0.5f);
            Shot("build_panel_overtime");
            yield return new WaitForSeconds(1f);
            double f1 = Funds;
            Check(panel.Confirm(), "a second build with overtime starts " + panel.Error);
            BuildJob job2 = sc.Jobs.LastOrDefault();
            Check(job2 != null && job2 != job1 && job2.Overtime, "the second build has overtime on");
            Check(Near(f1 - Funds, q2.Total + Pricing.OvertimeCharge(q2.BuildPrice, 1, S)), "overtime charged 25% of the build price on top (" + Pricing.Funds(f1 - Funds) + ")");
            Check(!sc.ActiveJobs().Contains(job2) && sc.ActiveJobs().Contains(job1), "the second build waits for the VAB's one line");
            SuiteWindow.Instance.Show = true;
            SuiteWindow.Instance.Tab = 0;
            yield return new WaitForSeconds(1f);
            Shot("suite_queue_vab");
            yield return new WaitForSeconds(1f);
            yield return ScrollStaysInWindow(o => SuiteWindow.Instance.TestOffset = o, "PRSSuiteWindow", "the suite window in the VAB", ControlTypes.EDITOR_PAD_PICK_PLACE);
            SuiteWindow.Instance.Show = false;

            // 3. Time passes at the Space Center.
            string id1 = job1.Id, id2 = job2.Id;
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            job1 = sc.Jobs.FirstOrDefault(j => j.Id == id1);
            job2 = sc.Jobs.FirstOrDefault(j => j.Id == id2);
            Check(job1 != null && job2 != null, "both builds survived leaving the VAB");
            if (job1 == null || job2 == null) yield break;
            double left1 = job1.SecondsLeft(S);
            Planetarium.SetUniversalTime(Planetarium.GetUniversalTime() + left1 / 2);
            yield return new WaitForSeconds(1f);
            Check(Near(job1.Progress, 0.5, 0.05), "half the build time later, the first build is half done (" + Math.Round(job1.Progress * 100) + "%)");
            Check(job2.Progress == 0, "the waiting build hasn't started");
            Planetarium.SetUniversalTime(Planetarium.GetUniversalTime() + left1 / 2 + 5);
            yield return new WaitForSeconds(1f);
            HangarShip ready1 = sc.Hangar.FirstOrDefault(h => h.Ready && h.Name == job1.Name);
            Check(!sc.Jobs.Contains(job1) && ready1 != null, "the first build finished into the hangar, ready (" + ready1?.DisplayName + ")");
            Check(sc.ActiveJobs().Contains(job2) && job2.Progress > 0, "the second build started on the free line");

            double f2 = Funds;
            double expectOutsource = Pricing.OutsourceCharge(job2.BuildPrice, 1 - job2.Progress, S) - Pricing.OvertimeCharge(job2.BuildPrice, 1 - job2.Progress, S);
            Check(sc.Outsource(job2, out string err), "outsourcing finishes the second build " + err);
            Check(Near(f2 - Funds, expectOutsource), "outsourcing charged the rest, less the unused overtime (" + Pricing.Funds(f2 - Funds) + ")");
            Check(sc.Hangar.Count(h => h.Ready) == 2, "two ready ships in the hangar");
            SuiteWindow.Instance.Show = true;
            SuiteWindow.Instance.Tab = 1;
            yield return new WaitForSeconds(1f);
            Shot("suite_hangar_ksc");
            yield return new WaitForSeconds(1f);
            yield return ScrollStaysInWindow(o => SuiteWindow.Instance.TestOffset = o, "PRSSuiteWindow", "the suite window at the Space Center", ControlTypes.KSC_FACILITIES);
            SuiteWindow.Instance.TestOffset = new Vector2(-10, -10);
            yield return new WaitForSeconds(0.5f);
            SuiteWindow.Instance.Show = false;
            yield return new WaitForSeconds(0.5f);
            SuiteWindow.Instance.TestOffset = null;
            Check(InputLockManager.GetControlLock("PRSSuiteWindow") == ControlTypes.None, "closing the suite window with the mouse over it lets go of the camera and the Space Center");


            // 4. Launch a ready ship from the hangar: it's paid for, so it costs nothing more.
            string readyId = ready1.Id, readyName = ready1.DisplayName;
            int readyParts = ready1.PartCount;
            double f3 = Funds;
            // First its crew (Phil, play-test 2026-11-09 #3: pick the crew before launch). The step starts with KSP's
            // own pick; Clear, Add someone else, then Launch.
            SuiteWindow w = SuiteWindow.Instance;
            Check(w != null && w.StartLaunch(ready1) && w.CrewShip == ready1, "Launch opens the crew step for a ship with a seat");
            VesselCrewManifest crew = w?.CrewList;
            var picked = crew != null ? crew.GetAllCrew(false) : new System.Collections.Generic.List<ProtoCrewMember>();
            Check(picked.Count == 1, "it starts with KSP's own pick (" + string.Join(", ", picked.Select(c => c.name).ToArray()) + ")");
            yield return new WaitForSeconds(1f);
            Shot("suite_crew_step");
            yield return new WaitForSeconds(1f);
            string otherName = null;
            if (crew != null)
            {
                ProtoCrewMember other = SuiteWindow.AtHome(crew).FirstOrDefault();
                SuiteWindow.Clear(crew);
                Check(crew.GetAllCrew(false).Count == 0 && picked.Count == 1 && SuiteWindow.AtHome(crew).Contains(picked[0]), "Clear empties the seat, and that kerbal is back in the list at home");
                Check(other != null && SuiteWindow.Seat(crew, other) && !SuiteWindow.Seat(crew, picked.FirstOrDefault()), "Add puts " + other?.name + " in the seat; with it taken, no one else fits");
                otherName = other?.name;
                Check(sc.DefaultCrew(ready1).GetAllCrew(false).Count == 1, "Reset would go back to KSP's pick");
            }
            Check(w != null && w.ConfirmCrew(), "launching the ready ship with that crew");
            yield return WaitFor(() => HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.loaded && !FlightGlobals.ActiveVessel.packed, 120, "the ready ship reached the pad");
            yield return new WaitForSeconds(3f);
            sc = PRSScenario.Instance;
            Check(Near(Funds, f3), "launching a ready ship cost nothing (" + Pricing.Funds(f3 - Funds) + ")");
            Check(sc.HangarOf(readyId) == null, "the launched ship left the hangar");
            Vessel v = FlightGlobals.ActiveVessel;
            var aboard = v.GetVesselCrew().Select(c => c.name).ToList();
            Check(otherName != null && aboard.Count == 1 && aboard[0] == otherName && !picked.Any(c => c.name == otherName), "it flies with the crew picked: " + string.Join(", ", aboard.ToArray()));
            LaunchRecord rec = sc.RecordOf(v.parts[0].launchID, v.parts[0].flightID);
            Check(rec != null && rec.ShipId == readyId && rec.Slots.Count == readyParts && rec.Slots.All(s => s >= 0) && v.parts.Count == readyParts,
                  "the launch was recorded as " + readyName + " going up, all " + readyParts + " parts its own");
            Check(sc.ShipOf(readyId)?.Flights == 1 && v.vesselName == readyName, "it flies as " + readyName + ", on its first flight");
            Check(v.parts.All(p => string.IsNullOrEmpty(PRSPart.Of(p).hw)), "no part carries a VAB note into flight");

            // Going back to a ship still on the pad (here: loading the flight again) isn't a new launch.
            int launchesBefore = sc.Launches.Count;
            uint padId = v.persistentId;
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            int padIdx = HighLogic.CurrentGame.flightState.protoVessels.FindIndex(p => p.persistentId == padId);
            Check(padIdx >= 0, "found the ship on the pad in the save");
            if (padIdx >= 0)
            {
                FlightDriver.StartAndFocusVessel("persistent", padIdx);
                yield return new WaitForSeconds(2f);
                yield return WaitFor(() => HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.persistentId == padId && FlightGlobals.ActiveVessel.loaded && !FlightGlobals.ActiveVessel.packed, 120, "back at the ship on the pad");
                yield return new WaitForSeconds(3f);
                v = FlightGlobals.ActiveVessel;
                sc = PRSScenario.Instance;
                Check(sc.ShipOf(readyId)?.Flights == 1 && sc.Launches.Count == launchesBefore, "going back to it on the pad isn't a new launch: still its first flight, no new launch record");
            }

            // 5. Recover it from the pad. The Recover button asks first: Cancel leaves it, To the hangar keeps it.
            var altimeter = FindObjectOfType<KSP.UI.Screens.AltimeterSliderButtons>();
            var recoverMethod = typeof(KSP.UI.Screens.AltimeterSliderButtons).GetMethod("recoverVessel", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Check(altimeter != null && recoverMethod != null, "found the flight Recover button");
            if (altimeter == null || recoverMethod == null) yield break;
            recoverMethod.Invoke(altimeter, null);
            yield return new WaitForSeconds(1f);
            Check(RecoverPrompt.Current != null && RecoverPrompt.Current.Vessel == v, "pressing Recover shows the suite's prompt instead of recovering");
            Shot("recover_prompt");
            yield return new WaitForSeconds(1f);
            RecoverPrompt prompt = RecoverPrompt.Current;
            if (prompt != null)
            {
                yield return ScrollStaysInWindow(o => prompt.TestOffset = o, "PRSRecoverPrompt", "the Recover prompt", ControlTypes.STAGING);
                Check((InputLockManager.GetControlLock("PRSRecoverPrompt") & ControlTypes.STAGING) != 0, "off the Recover prompt, the ship's other controls stay held while it's up");
            }
            RecoverPrompt.Current?.TestChoose(null);
            yield return new WaitForSeconds(2f);
            Check(HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel == v && RecoverPrompt.Current == null, "Cancel leaves the ship where it is");
            double fBefore = Funds;
            recoverMethod.Invoke(altimeter, null);
            yield return new WaitForSeconds(1f);
            RecoverPrompt.Current?.TestChoose(RecoverChoice.Hangar);
            yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.SPACECENTER && PRSScenario.Instance != null, 120, "recovery returned to the Space Center");
            yield return new WaitForSeconds(4f);
            Check(Near(Funds, fBefore, 2), "To the hangar from the pad: no refund, no transport (" + Pricing.Funds(Funds - fBefore) + ")");
            sc = PRSScenario.Instance;
            HangarShip stored = sc.HangarOf(readyId);
            Check(stored != null && !stored.Ready && stored.PartCount == readyParts, "the recovered ship is back in the hangar, stored (" + stored?.DisplayName + ")");
            if (stored == null) yield break;
            Check(stored.Flights == 1, "it has 1 flight");
            Check(stored.DisplayName == readyName, "it comes home as the same ship (" + stored.DisplayName + ", launched as " + readyName + ")");

            // 6. Wear (Phil, 2026-11-08). A hop from the pad adds next to none; give it 20% to check the price: the
            // refurbish is the price × wear × half, so 10% of it. Then cancel for a full refund.
            Check(stored.Wear < 0.02, "a recovery from the pad adds next to no wear (" + Core.Wear.Percent(stored.Wear) + ")");
            stored.Wear = 0.2;
            Quote rq = sc.QuoteFor(stored);
            Check(rq.NewPrice == 0 && rq.RefurbishPrice > 0, "a refurbish has no new parts, only refurbishing (" + Pricing.Funds(rq.RefurbishPrice) + ")");
            Check(Near(rq.RefurbishPrice, q1.NewPrice * 0.2 * S.RefurbishShare, 2), "at 20% wear the refurbish is 10% of its price (" + Pricing.Funds(rq.RefurbishPrice) + " of " + Pricing.Funds(q1.NewPrice) + ")");
            double f4 = Funds;
            BuildJob refit = sc.RefurbishBuild(stored, out string err2);
            Check(refit != null && Near(f4 - Funds, rq.Total), "Refurbish queued and paid (" + Pricing.Funds(f4 - Funds) + ") " + err2);
            Check(stored.ReservedBy == refit?.Id && refit?.ShipId == readyId, "the stored ship is reserved by its refurbish, which keeps it");
            Check(refit != null && sc.TitleOf(refit) == stored.DisplayName, "the queue calls the refurbish by the ship's own name (" + (refit != null ? sc.TitleOf(refit) : "") + ")");
            SuiteWindow.Instance.Show = true;
            SuiteWindow.Instance.Tab = 0;
            yield return new WaitForSeconds(1f);
            Shot("suite_queue_ksc");
            yield return new WaitForSeconds(1f);
            SuiteWindow.Instance.Show = false;
            double f5 = Funds;
            sc.Cancel(refit);
            Check(Near(Funds - f5, rq.Total) && stored.ReservedBy == null && !sc.Jobs.Contains(refit), "cancelling refunds it in full and frees the ship");
            // Refurbishing is optional: it can be refuelled and flown as it is, keeping its wear. At 100% it can't.
            stored.Wear = 1;
            Check(!sc.Refuel(stored, out err) && !sc.LaunchReady(stored, out err) && !stored.Ready, "a worn-out ship can't be refuelled or launched until it's refurbished (" + err + ")");
            stored.Wear = 0.2;
            double f5b = Funds, fuelPrice = sc.RefuelPrice(stored);
            Check(sc.Refuel(stored, out err) && stored.Ready && Near(f5b - Funds, fuelPrice) && Near(stored.Wear, 0.2, 1e-6), "refuelling without refurbishing: ready to launch, pays only fuel (" + Pricing.Funds(fuelPrice) + "), keeps its wear " + err);
            SuiteWindow.Instance.ShowHangarShip(stored.Id);
            yield return new WaitForSeconds(1f);
            Shot("suite_hangar_wear");
            yield return new WaitForSeconds(1f);
            SuiteWindow.Instance.Show = false;

            // 6b. "Open in the VAB" from the Space Center (Phil): it goes to the VAB and opens the ship as it's stored.
            // The VAB knows every part is that ship's real hardware. Built from there it stays that ship: the build is
            // named "X", not "X #1" (which came home as "X #1 #1").
            string storedId = stored.Id, storedName = stored.Name, storedDisplay = stored.DisplayName;
            double refurbishPrice = rq.RefurbishPrice;
            EditorAddon.OpenHangarShip(stored);
            yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.EDITOR && EditorLogic.fetch != null && EditorAddon.Instance != null && EditorLogic.fetch.ship != null && EditorLogic.fetch.ship.parts.Count == readyParts, 90, "Open in the VAB went to the VAB with the ship");
            yield return new WaitForSeconds(3f);
            sc = PRSScenario.Instance;
            Check(sc.ExactShipOf(EditorAddon.EditorCraft())?.Id == storedId, "it's the stored ship's own hardware, all " + readyParts + " parts");
            Check(EditorLogic.fetch.ship.parts.All(EditorAddon.IsHardware), "Phil's Iterative Parts is told every part of it is hardware, so they keep their Mk in the VAB");
            EditorAddon.Instance.OpenBuildPanel();
            panel = EditorAddon.Instance.Panel;
            Check(panel.ReadyShip?.Id == storedId && panel.BuildName == storedName && panel.KeepShipId == storedId, "the Build panel knows it as " + storedDisplay + ", to be built under its own name (" + panel.BuildName + ")");
            Check(panel.Quote != null && Near(panel.Quote.RefurbishPrice, refurbishPrice, 2) && panel.Quote.FuelPrice < 0.5, "refurbishing it from here costs the same, by its wear, with nothing to refuel (" + Pricing.Funds(panel.Quote?.RefurbishPrice ?? 0) + ")");
            yield return new WaitForSeconds(1f);
            Shot("build_panel_ready_worn");
            double f5c = Funds;
            Check(panel.Confirm(), "refurbishing it from the VAB " + panel.Error);
            BuildJob vabRefurb = sc.Jobs.LastOrDefault();
            Check(vabRefurb != null && vabRefurb.Name == storedName && sc.TitleOf(vabRefurb) == storedDisplay, "the build keeps the ship's identity (" + vabRefurb?.Name + ", shown as " + (vabRefurb != null ? sc.TitleOf(vabRefurb) : "") + ")");
            if (vabRefurb != null) sc.Cancel(vabRefurb);
            Check(Near(Funds, f5c, 2), "cancelled for a full refund");
            // Phil (S8): cancelling a refit and starting it again keeps it the same ship: same number, same history.
            EditorAddon.Instance.OpenBuildPanel();
            Check(panel.Editing == null && panel.KeepShipId == storedId && panel.Title == storedDisplay, "after the cancel, the ship still in the VAB is still " + storedDisplay + " (" + panel.Title + ")");
            Check(panel.Confirm(), "starting the cancelled refit again " + panel.Error);
            BuildJob again = sc.Jobs.LastOrDefault();
            Check(again != null && again != vabRefurb && again.ShipId == storedId && sc.TitleOf(again) == storedDisplay, "started again, the build is still " + storedDisplay + " (" + (again != null ? sc.TitleOf(again) : "") + "), not a new ship");
            if (again != null) sc.Cancel(again);
            Check(Near(Funds, f5c, 2), "cancelled again for a full refund");
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            stored = sc.HangarOf(storedId);
            Check(stored != null && stored.Ready && Near(stored.Wear, 0.2, 1e-6), "back at the Space Center it's still in the hangar, fuelled, at 20% wear");
            if (stored == null) yield break;

            // ---- Step 3: selling, taking apart, the parts bin in a new build, selling on recovery ----

            // 7. Sell the other ready ship for scrap: half its price.
            HangarShip ready2 = sc.Hangar.FirstOrDefault(h => h.Ready && h != stored);
            Check(ready2 != null, "the second ready ship is still in the hangar");
            if (ready2 == null) yield break;
            double scrap = sc.ScrapValue(ready2), f6 = Funds;
            Check(Near(scrap, (q1.NewPrice + q1.FuelPrice) * S.ScrapValue, 2), "scrap value is half its price with full tanks (" + Pricing.Funds(scrap) + ")");
            Check(sc.Sell(ready2) && Near(Funds - f6, scrap) && !sc.Hangar.Contains(ready2), "selling a hangar ship pays its scrap value and removes it");

            // 8. Take the stored ship apart (Phil, round 4): its parts go to the bin, refurbished and paid for now, and
            // forgetting the ship, their flights and their fuel.
            int binBefore = BinCount(sc), partsStored = stored.PartCount;
            double apart = sc.TakeApartPrice(stored), f6b = Funds;
            Check(Near(apart, q1.NewPrice * 0.2 * S.RefurbishShare, 2), "taking it apart costs its refurbish at 20% wear (" + Pricing.Funds(apart) + ")");
            Check(sc.TakeApart(stored, out err) && BinCount(sc) == binBefore + partsStored && sc.HangarOf(storedId) == null && Near(f6b - Funds, apart), "taking a ship apart puts its " + partsStored + " parts in the bin, paid " + Pricing.Funds(f6b - Funds) + " " + err);
            Check(sc.Bin.All(k => k.Sample.Children("RESOURCE").All(r => r.Get("amount") == "0") && Hardware.Module(k.Sample)?.Get("design") == null), "bin parts are just parts: no fuel, no ship, no design");

            // 9. A new Jumping Flea in the VAB uses those bin parts: already paid for, so they cost nothing, filled from empty.
            yield return ToEditor();
            yield return LoadFlea();
            EditorAddon.Instance.OpenBuildPanel();
            panel = EditorAddon.Instance.Panel;
            Quote qb = panel.Quote;
            Check(qb.BinParts == partsStored && qb.NewPrice < 1, "every part comes from the bin (" + qb.BinParts + " of " + partsStored + "), nothing new");
            Check(qb.RefurbishPrice < 0.5, "bin parts were refurbished as they went in: nothing to refurbish (" + Pricing.Funds(qb.RefurbishPrice) + ")");
            Check(Near(qb.FuelPrice, q1.FuelPrice, 1), "bin parts were emptied as they went in: the fuel is bought again (" + Pricing.Funds(qb.FuelPrice) + ")");
            Check(!EditorLogic.fetch.ship.parts.Any(EditorAddon.IsHardware) && panel.MainShip == null, "a design loaded from a file is new parts (current Mk), and no hangar ship");
            QuoteLine binLine = qb.Lines.FirstOrDefault(l => l.Kind == OwnerKind.Bin);
            var items = binLine != null ? BuildPanel.BinItems(binLine) : new System.Collections.Generic.List<string>();
            Check(items.Count > 0 && items.All(t => !t.Contains("wear") && !t.Contains("refurbish")), "the Build panel names the bin's parts, nothing about where they came from (" + string.Join("; ", items.ToArray()) + ")");
            yield return new WaitForSeconds(1f);
            Shot("build_panel_bin");
            yield return new WaitForSeconds(1f);
            sc = PRSScenario.Instance;
            Check(panel.Confirm(), "building from bin parts " + panel.Error);
            // Bin parts are built parts already paid for: with nothing new, there's nothing to build, so it's ready at once.
            Check(BinCount(sc) == 0 && !sc.Jobs.Any(), "the build took the " + partsStored + " parts out of the bin, and with nothing to build it's done at once");

            // 10. Launch it, and Sell it from the Recover prompt: the scrap money, nothing to the hangar.
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            HangarShip ready3 = sc.Hangar.FirstOrDefault(h => h.Ready && h.Name == storedName);
            Check(ready3 != null && ready3.Id != storedId && ready3.DisplayName == storedName + " #3" && ready3.Flights == 0,
                  "the bin-part build is ready as a new ship with the next number, " + storedName + " #3, never flown (" + ready3?.DisplayName + ")");
            if (ready3 == null) yield break;
            string ready3Id = ready3.Id;
            Check(sc.LaunchReady(ready3, out err), "launching it " + err);
            yield return WaitFor(() => HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.loaded && !FlightGlobals.ActiveVessel.packed, 120, "it reached the pad");
            yield return new WaitForSeconds(3f);
            v = FlightGlobals.ActiveVessel;
            double value = RecoveryValue.Value(v.BackupVessel());
            double f7 = Funds;
            altimeter = FindObjectOfType<KSP.UI.Screens.AltimeterSliderButtons>();
            recoverMethod.Invoke(altimeter, null);
            yield return new WaitForSeconds(1f);
            RecoverPrompt.Current?.TestChoose(RecoverChoice.Sell);
            yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.SPACECENTER && PRSScenario.Instance != null, 120, "the sale returned to the Space Center");
            yield return new WaitForSeconds(4f);
            sc = PRSScenario.Instance;
            // Half of what KSP itself paid back (KSP's value runs a little above the suite's own sum).
            Check(Near(Funds - f7, value * S.ScrapValue, value * 0.03), "Sell for scrap from the pad paid half its value (" + Pricing.Funds(Funds - f7) + " of " + Pricing.Funds(value) + ")");
            Check(sc.HangarOf(ready3Id) == null && BinCount(sc) == 0, "a sold ship goes to neither the hangar nor the bin");

            // 11. KSP's own launch-pad dialog launches a saved craft file straight into flight. A file saved from a ready
            // hangar ship (notes and all) isn't that ship: only the suite's own launches are hangar ships.
            yield return ToEditor();
            yield return LoadFlea();
            EditorAddon.Instance.OpenBuildPanel();
            panel = EditorAddon.Instance.Panel;
            sc = PRSScenario.Instance;
            Check(panel.Confirm(), "building one more Flea " + panel.Error);
            string lastJobId = sc.Jobs.LastOrDefault()?.Id;
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            BuildJob lastJob = sc.Jobs.FirstOrDefault(j => j.Id == lastJobId);
            if (lastJob != null) sc.Outsource(lastJob, out err);
            HangarShip held = sc.Hangar.FirstOrDefault(h => h.Ready);
            Check(held != null, "it's ready in the hangar");
            if (held == null) yield break;
            string heldId = held.Id;
            int heldParts = held.PartCount;
            // One fin taken off, and every reference to it (its parent's link and attachment, the other fins' symmetry).
            Node craftCopy = held.Noted();
            Node fin = craftCopy.Children("PART").FirstOrDefault(p => (p.Get("part") ?? "").StartsWith("basicFin"));
            if (fin != null)
            {
                string finRef = fin.Get("part");
                craftCopy.Nodes.Remove(fin);
                foreach (Node p in craftCopy.Children("PART")) p.Values.RemoveAll(kv => kv.Value != null && kv.Value.Contains(finRef));
            }
            ConfigNode file = KspNodes.ToKsp(craftCopy, "");
            string filePath = Path.Combine(SuiteFiles.SaveDir, "selftest-padlaunch.craft");
            file.Save(filePath);
            VesselCrewManifest manifest = HighLogic.CurrentGame.CrewRoster.DefaultCrewForVessel(file, VesselCrewManifest.FromConfigNode(file), true, false);
            FlightDriver.StartWithNewLaunch(filePath, HighLogic.CurrentGame.flagURL, "LaunchPad", manifest);
            yield return WaitFor(() => HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.loaded && !FlightGlobals.ActiveVessel.packed, 120, "the saved craft reached the pad");
            yield return new WaitForSeconds(3f);
            v = FlightGlobals.ActiveVessel;
            sc = PRSScenario.Instance;
            LaunchRecord padRec = sc.RecordOf(v.parts[0].launchID, v.parts[0].flightID);
            Check(fin != null && v.parts.Count == heldParts - 1, "the saved craft is the ready Flea less a fin (" + v.parts.Count + " parts)");
            Check(padRec != null && padRec.ShipId == null && padRec.Slots.All(s => s < 0), "launched from a saved file, it's no hangar ship: no ship went up in it");
            Check(v.parts.All(p => string.IsNullOrEmpty(PRSPart.Of(p).hw)), "the notes it was saved with are gone in flight");
            Check(sc.HangarOf(heldId) != null && sc.HangarOf(heldId).Ready, "the ready hangar ship is untouched");

            // 12. A build made from two ready hangar ships (Phil stacked a ready Argo I #1 on a ready Rockall 002 #1) is the
            // one opened (Phil, round 4: "Sure i guess that works"): same name, number and flight log. The other one's
            // parts are owned parts of it, and that ship ends.
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            HangarShip top = sc.HangarOf(heldId);
            if (top == null) { Check(false, "the ready Flea is still in the hangar for the stack test"); yield break; }
            Node baseCraft = Hardware.Plain(top.Craft.Clone());
            Quote qBase = sc.QuoteFor(baseCraft, "Stack Base", null);
            BuildJob baseJob = sc.StartBuild(baseCraft, "Stack Base", "VAB", qBase, false, true, null, out err);
            HangarShip under = sc.Hangar.FirstOrDefault(h => h.Ready && h.Name == "Stack Base");
            Check(baseJob != null && under != null && under.DisplayName == "Stack Base #1", "a second ready ship to stack under it (" + under?.DisplayName + ") " + err);
            if (under == null) yield break;
            int hangarBefore = sc.Hangar.Count;
            string topId = top.Id, topName = top.DisplayName, underId = under.Id, underName = under.DisplayName;
            int parts = top.PartCount + under.PartCount;
            // Stacked as the VAB would: the base's parts under the top ship's (their own part ids, its root linked below).
            Node stack = top.Noted();
            List<Node> baseParts = Renumbered(under.Noted(), 500000).Children("PART").ToList();
            stack.Children("PART").Last().Add("link", baseParts[0].Get("part"));
            foreach (Node p in baseParts) stack.Nodes.Add(p);
            Quote qs = sc.QuoteFor(stack, top.Name, null);
            Check(qs.BuildPrice == 0 && qs.Lines.Count == 2 && qs.Lines.All(l => l.Kind == OwnerKind.Hangar), "both pieces are ready hangar ships: nothing to build (" + qs.Lines.Count + " pieces, " + Pricing.Funds(qs.BuildPrice) + ")");
            BuildJob stackJob = sc.StartBuild(stack, top.Name, "VAB", qs, false, false, topId, out err);
            HangarShip result = sc.HangarOf(topId);
            Check(stackJob != null && !sc.Jobs.Contains(stackJob) && result != null && result.Ready, "with nothing to build, it's finished at once " + err);
            if (result == null) yield break;
            Check(result.DisplayName == topName && result.PartCount == parts && result.Ship.BlueprintParts == parts && result.Slots.All(s => s >= 0),
                  "it's still " + topName + " (the one opened), now with " + result.PartCount + " parts, all in its blueprint (" + result.DisplayName + ")");
            Check(sc.HangarOf(underId) == null && sc.Hangar.Count == hangarBefore - 1 && BuildsLink.Under(result) == null, "the other ship's parts are its own now: one ship fewer in the hangar, and it isn't 'on' anything");
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            result = sc.HangarOf(topId);
            Check(sc.ShipOf(underId) == null && result != null, underName + " is no longer a ship anywhere");
            if (result == null) yield break;

            yield return OldMkSteps(result);
        }

        static int BinCount(PRSScenario sc) => sc.Bin.Sum(k => k.Count);

        /// <summary>A craft with every part id moved on by this much (and every link to it), so it can be stacked with its copy.</summary>
        static Node Renumbered(Node craft, uint by)
        {
            Node c = craft.Clone();
            List<Node> parts = c.Children("PART").ToList();
            var map = new Dictionary<string, string>();
            foreach (Node p in parts)
            {
                string r = p.Get("part") ?? "";
                int u = r.LastIndexOf('_');
                if (u > 0 && uint.TryParse(r.Substring(u + 1), out uint id)) map[r] = r.Substring(0, u + 1) + (id - by);
            }
            foreach (KeyValuePair<string, string> m in map)
                foreach (Node p in parts) { if (p.Get("part") == m.Key) p.Set("part", m.Value); else Hardware.RenameRef(p, m.Key, m.Value); }
            return c;
        }

        static Node FakePip(Node part) => part.Children("MODULE").FirstOrDefault(m => m.Get("name") == "ModulePipPart");

        /// <summary>
        /// 13. Old-Mk parts (Phil's design, play-test 2026-11-08 #5), with a stand-in for Phil's Iterative Parts: the Flea's
        /// top part is a program at Mk 3, so a new one is Mk 3. A Mk 1 bin part that would fit is offered, not used; "No"
        /// builds it new and leaves it in the bin; "Update and use" pays the fee and uses it. A hangar ship's old part
        /// updates too. The real PIP is checked by its own test.
        /// </summary>
        IEnumerator OldMkSteps(HangarShip result)
        {
            Pip.BehindFn realBehind = Pip.BehindOf;
            Func<Node, bool> realCurrent = Pip.MakeCurrentOf;
            Func<Node, string> realTitle = Pip.TitleOf;
            string rootName = Hardware.PartName(result.Craft.Children("PART").First());
            Func<Node, int> mkOf = p => FakePip(p) != null && int.TryParse(FakePip(p).Get("mk"), out int k) ? k : Hardware.PartName(p) == rootName ? 3 : 0;
            Pip.BehindOf = (Node p, out string now, out string cur) =>
            {
                int mk = mkOf(p);
                now = "Fake I Mk. " + mk; cur = "Fake I Mk. 3";
                return mk == 0 ? 0 : 3 - mk;
            };
            Pip.MakeCurrentOf = p => { Node m = FakePip(p); if (m == null || m.Get("mk") == "3") return false; m.Set("mk", "3"); return true; };
            Pip.TitleOf = p => mkOf(p) > 0 ? "Fake I Mk. " + mkOf(p) : null;

            PRSScenario sc = PRSScenario.Instance;
            string err;
            Check(sc.TakeApart(result, out err), "the stack taken apart for the old-Mk test " + err);
            yield return ToEditor();
            yield return LoadFlea();
            sc = PRSScenario.Instance;
            BinKind tops = sc.Bin.FirstOrDefault(k => k.Count > 0 && Hardware.PartName(k.Sample) == rootName);
            if (tops != null)
            {
                Node m = tops.Sample.AddNode("MODULE"); m.Add("name", "ModulePipPart"); m.Add("mk", "1");
                Pip.Forget(tops.Sample);
                tops.Key = PRSScenario.BinKeyOf(tops.Sample);
            }
            Check(tops != null && tops.Count == 2 && tops.Key.Contains("Fake I Mk. 1"), "the bin has " + tops?.Count + " of the Flea's top part, made Mk 1 of a Mk 3 program");
            if (tops == null) yield break;
            EditorAddon.Instance.OpenBuildPanel();
            BuildPanel panel = EditorAddon.Instance.Panel;
            Quote q = panel.Quote;
            Check(q.OldBin.Count == 1 && q.OldBin[0].BinKey == tops.Key && !q.BinTaken.ContainsKey(tops.Key), "the old part is offered, not used (" + q.OldBin.Count + " offered)");
            Check(OldPartsQuestion.List(q.OldBin) == "Fake I Mk. 1 → Mk. 3", "the question names it: " + OldPartsQuestion.List(q.OldBin));
            Check(!panel.Confirm() && panel.Error != null && sc.Jobs.Count(j => j.Name == "Jumping Flea") == 0, "Build waits for an answer (" + panel.Error + ")");
            yield return new WaitForSeconds(1f);
            Shot("build_panel_old_mk");
            yield return new WaitForSeconds(1f);

            // "No": built new, the old ones stay in the bin.
            double newBefore = q.NewPrice;
            panel.SkipOld.Add(PRSScenario.SkipOldParts);
            panel.Refresh();
            q = panel.Quote;
            Check(q.OldBin.Count == 0 && Near(q.NewPrice, newBefore, 0.5) && !q.BinTaken.ContainsKey(tops.Key), "after No, it's built new (" + Pricing.Funds(q.NewPrice) + " new, as priced while asking), and the old parts are neither offered nor used");

            // "Update and use": the fee, then the bin part is current and the build takes it.
            panel.SkipOld.Clear();
            panel.Refresh();
            q = panel.Quote;
            double fee = q.UpdatePrice, expect = KspCosts.Instance.Cost(tops.Sample).Dry * 2 * S.PipUpdatePerMk, f0 = Funds;
            Check(Near(fee, expect, 0.5), "updating it costs 10% of its price per Mk gained (" + Pricing.Funds(fee) + ")");
            Check(sc.UpdatePip(sc.BinKindsOf(q.OldBin), out err) && Near(f0 - Funds, fee, 0.5), "Update and use charged the fee " + err);
            BinKind current = sc.Bin.FirstOrDefault(k => k.Count > 0 && k.Key.Contains("Fake I Mk. 3") && Hardware.PartName(k.Sample) == rootName);
            Check(current != null && current.Count == 1 && tops.Count == 1, "one of them is now Mk 3; the other is still Mk 1");
            panel.Refresh();
            q = panel.Quote;
            Check(q.OldBin.Count == 0 && current != null && q.BinTaken.ContainsKey(current.Key), "and the build now uses it");
            Check(panel.Confirm(), "building with the updated part " + panel.Error);
            BuildJob job = sc.Jobs.LastOrDefault();
            if (job != null) sc.Outsource(job, out err);
            HangarShip h = sc.Hangar.Where(x => x.Ready && x.Name == "Jumping Flea").OrderByDescending(x => x.Number).FirstOrDefault();
            Check(h != null && current.Count == 0, "it's built, with the updated part (" + h?.DisplayName + ")");

            // A hangar ship's own old part: "Update PIP parts" on the hangar copy.
            if (h != null)
            {
                Node part = h.Craft.Children("PART").Last();
                Node m = part.AddNode("MODULE"); m.Add("name", "ModulePipPart"); m.Add("mk", "2");
                Pip.Forget(part);
                double price = sc.PipUpdatePrice(h.Craft.Children("PART"), out int behind), f1 = Funds;
                Check(behind == 1 && price > 0, "the hangar ship has " + behind + " part behind (" + Pricing.Funds(price) + " to update)");
                Check(sc.UpdatePip(h, out err) && Near(f1 - Funds, price, 0.5) && h.Craft.Children("PART").All(p => FakePip(p) == null || FakePip(p).Get("mk") == "3"), "Update PIP parts on a hangar ship " + err);
                Check(!sc.UpdatePip(h, out err), "with nothing behind, there's nothing to update (" + err + ")");
            }
            Pip.BehindOf = realBehind;
            Pip.MakeCurrentOf = realCurrent;
            Pip.TitleOf = realTitle;
        }
    }
}
