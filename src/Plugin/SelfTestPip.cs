using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using PhilsBuilds;
using PhilsReusabilitySuite.Core;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// The combined check of play-test round 2 with the real Phil's Iterative Parts, started by the test harness through
    /// the hidden PRSSelfTestPip event. A throwaway career (zz_PRSSelfTestPip) gets one PIP battery program and a filed
    /// launcher of two of its batteries. With a flown copy at Mk 1 and the program at Mk 3: the Space Center builds new
    /// parts at Mk 3; the VAB loads hangar hardware beside a new part and keeps the hardware at Mk 1 while the new part
    /// comes out Mk 3, and building it keeps both; "Use it"'s reload gives a live part its stored Mk; an old-Mk bin part
    /// is offered, not used, until it's updated; and Update PIP parts brings a hangar ship up to date.
    /// </summary>
    class SelfTestPip : MonoBehaviour
    {
        public const string SaveName = "zz_PRSSelfTestPip";
        const string DesignName = "Testcell Stack";
        static bool running;
        int pass, fail;

        public static void Begin()
        {
            if (running) return;
            running = true;
            var go = new GameObject("PRSSelfTestPip");
            DontDestroyOnLoad(go);
            go.AddComponent<SelfTestPip>();
        }

        void Check(bool ok, string what)
        {
            if (ok) { pass++; Debug.Log("[PRS-TEST] PASS " + what); }
            else { fail++; Debug.LogError("[PRS-TEST] FAIL " + what); }
        }

        static void Note(string s) { Debug.Log("[PRS-TEST] " + s); }
        static bool Near(double a, double b, double tol = 1.5) => Math.Abs(a - b) <= tol;
        static double Funds => Funding.Instance != null ? Funding.Instance.Funds : double.NaN;

        void Start() { StartCoroutine(Run()); }

        IEnumerator Run()
        {
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

        // ---- PIP, by reflection (test only) ----

        static Type PipType(string name) => AssemblyLoader.loadedAssemblies.Select(a => a.assembly.GetType(name, false)).FirstOrDefault(x => x != null);

        static object Member(object o, string name)
        {
            Type t = o as Type ?? o.GetType();
            object target = o is Type ? null : o;
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            PropertyInfo p = t.GetProperty(name, all);
            if (p != null) return p.GetValue(target, null);
            FieldInfo f = t.GetField(name, all);
            return f?.GetValue(target);
        }

        /// <summary>A part node's own Mk, as saved.</summary>
        static string MkIn(Node part) => part.Children("MODULE").FirstOrDefault(m => m.Get("name") == "ModulePipPart")?.Get("mk");

        /// <summary>A live part's Mk.</summary>
        static int LiveMk(Part p)
        {
            PartModule m = p.Modules.Cast<PartModule>().FirstOrDefault(x => x.moduleName == "ModulePipPart");
            object v = m?.GetType().GetField("mk")?.GetValue(m);
            return v is int i ? i : -1;
        }

        /// <summary>Two of the program's batteries, one on top of the other, as a craft file (PIP fills in the module).</summary>
        static ConfigNode CellCraft(string partName, string programId, string shipName, AvailablePart ap)
        {
            AttachNode top = ap.partPrefab.FindAttachNode("top"), bottom = ap.partPrefab.FindAttachNode("bottom");
            float dy = top != null && bottom != null ? bottom.position.y - top.position.y : -0.2f;
            var c = new ConfigNode("");
            c.AddValue("ship", shipName);
            c.AddValue("version", "1.12.5");
            c.AddValue("description", "");
            c.AddValue("type", "VAB");
            c.AddValue("size", "1,1,1");
            string a = partName + "_1001", b = partName + "_1002";
            for (int i = 0; i < 2; i++)
            {
                ConfigNode p = c.AddNode("PART");
                p.AddValue("part", i == 0 ? a : b);
                p.AddValue("partName", "Part");
                p.AddValue("persistentId", (1001 + i).ToString(CultureInfo.InvariantCulture));
                string pos = "0," + (10 + i * dy).ToString("R", CultureInfo.InvariantCulture) + ",0";
                p.AddValue("pos", pos);
                p.AddValue("attPos", "0,0,0");
                p.AddValue("attPos0", pos);
                p.AddValue("rot", "0,0,0,1");
                p.AddValue("attRot", "0,0,0,1");
                p.AddValue("attRot0", "0,0,0,1");
                p.AddValue("mir", "1,1,1");
                p.AddValue("symMethod", "Radial");
                p.AddValue("istg", "0");
                p.AddValue("dstg", "0");
                p.AddValue("sidx", "-1");
                p.AddValue("sqor", "-1");
                p.AddValue("sepI", "0");
                p.AddValue("attm", "0");
                p.AddValue("modCost", "0");
                p.AddValue("modMass", "0");
                p.AddValue("modSize", "0,0,0");
                if (i == 0) { p.AddValue("link", b); p.AddValue("attN", "bottom," + b); }
                else p.AddValue("attN", "top," + a);
                ConfigNode m = p.AddNode("MODULE");
                m.AddValue("name", "ModulePipPart");
                m.AddValue("isEnabled", "True");
                m.AddValue("programId", programId);
            }
            return c;
        }

        /// <summary>A copy of a craft as a hangar ship's: its parts placed from the design.</summary>
        static Node AsHardware(ConfigNode craft, string designId)
        {
            Node n = KspNodes.FromKsp(craft);
            int i = 0;
            foreach (Node p in n.Children("PART"))
            {
                Node m = p.AddNode("MODULE");
                m.Add("name", Core.Craft.ModuleName);
                m.Add("isEnabled", "True");
                m.Add("design", designId);
                m.Add("slot", i.ToString(CultureInfo.InvariantCulture));
                i++;
            }
            return n;
        }

        IEnumerator Body()
        {
            // 1. A career game of its own, every technology researched, plenty of funds.
            GameParameters gp = GameParameters.GetDefaultParameters(Game.Modes.CAREER, GameParameters.Preset.Normal);
            Game g = GamePersistence.CreateNewGame(SaveName, Game.Modes.CAREER, gp, "Squad/Flags/default", GameScenes.SPACECENTER, EditorFacility.VAB);
            HighLogic.CurrentGame = g;
            HighLogic.SaveFolder = SaveName;
            g.startScene = GameScenes.SPACECENTER;
            g.Start();
            yield return WaitFor(() => HighLogic.LoadedScene == GameScenes.SPACECENTER && PRSScenario.Instance != null && Funding.Instance != null && ResearchAndDevelopment.Instance != null, 120, "the career game reached the Space Center");
            yield return new WaitForSeconds(4f);
            Funding.Instance.SetFunds(5000000, TransactionReasons.Cheating);
            foreach (ProtoRDNode rd in AssetBase.RnDTechTree.GetTreeNodes())
            {
                ProtoTechNode t = rd.tech;
                t.state = RDTech.State.Available;
                ResearchAndDevelopment.Instance.SetTechState(t.techID, t);
            }

            Type api = PipType("PIP.PipApi"), scenT = PipType("PIP.PipScenario"), typeT = PipType("PIP.PartType");
            Check(api != null && scenT != null && typeT != null && Pip.BehindOf != null && Pip.MakeCurrentOf != null, "Phil's Iterative Parts and its calls are installed");
            if (api == null || scenT == null || typeT == null) yield break;
            MethodInfo toCurrent = api.GetMethod("ToCurrent", new[] { typeof(ConfigNode) });

            // 2. A battery program, at Mk 1.
            object scen = Member(scenT, "Instance");
            object battery = Member(PipType("PIP.PartTypes") ?? typeT, "Battery");
            Check(battery != null, "found PIP's battery part type");
            if (battery == null) yield break;
            string[] model = (string[])Member(battery, "DefaultModel");
            int cats = ((Array)Member(battery, "Categories")).Length;
            MethodInfo create = scenT.GetMethod("Create", new[] { typeof(string), typeT, typeof(string[]), typeof(int[]), typeof(string).MakeByRefType() });
            var args = new object[] { "Testcell", battery, model, new int[cats], null };
            object program = scen != null && create != null ? create.Invoke(scen, args) : null;
            Check(program != null, "a PIP battery program was started " + args[4]);
            if (program == null) yield break;
            string programId = (string)Member(program, "Id"), partName = (string)Member(program, "PartName");
            Type factory = PipType("PIP.PartFactory");
            AvailablePart ap = PartLoader.getPartInfoByName(partName);
            if (ap == null) ap = factory?.GetMethod("Ensure", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new[] { program }) as AvailablePart;
            Check(ap != null && (int)Member(program, "Mk") == 1, "its part " + partName + " is in the parts list, at Mk " + Member(program, "Mk"));
            if (ap == null) yield break;

            // 3. Filed as a craft (a probe) of two batteries, saved at Mk 1, the way an older design file is.
            ConfigNode file = CellCraft(partName, programId, DesignName, ap);
            toCurrent?.Invoke(null, new object[] { file });
            string saveDir = Path.Combine(KSPUtil.ApplicationRootPath, "saves", SaveName);
            string craftDir = Path.Combine(saveDir, "Ships", "VAB", "Probes");
            Directory.CreateDirectory(craftDir);
            file.Save(Path.Combine(craftDir, DesignName + ".craft"));
            Library.Scan();
            Design d = BuildsApi.Designs().FirstOrDefault(x => x.Name == DesignName);
            Check(d != null && d.Kind == DesignKind.Craft && file.GetNodes("PART").All(p => p.GetNode("MODULE")?.GetValue("mk") == "1"), "the craft is filed, its file saved with Mk 1 parts (" + d?.Name + ")");
            if (d == null) yield break;

            // 4. A flown copy of it in the hangar, built at Mk 1. Then the program reaches Mk 3.
            PRSScenario sc = PRSScenario.Instance;
            Node flown = AsHardware(file, d.Id);
            var h = new HangarShip { Ship = sc.NewShip(DesignName), Craft = flown, Ready = true, UT = Planetarium.GetUniversalTime() };
            h.Ship.Flights = 1;
            sc.Hangar.Add(h);
            string hId = h.Id;
            // 7 experience is Mk 3 (2, 7, 14, 25 for Mk 2 to 5); PIP counts one per flight.
            FieldInfo launches = program.GetType().GetField("Launches");
            launches?.SetValue(program, 7);
            try { factory?.GetMethod("Refresh", BindingFlags.Public | BindingFlags.Static, null, new[] { program.GetType() }, null)?.Invoke(null, new[] { program }); }
            catch (Exception ex) { Note("PIP's part refresh after the Mk change: " + (ex.InnerException ?? ex).Message); }
            Check((int)Member(program, "Mk") == 3, "the program reached Mk 3 (" + Member(program, "Mk") + ")");
            Node hwA = flown.Children("PART").First();
            int behind = Pip.Behind(hwA, out string now, out string current);
            Check(behind == 2 && now == "Testcell I Mk. 1" && current == "Testcell I Mk. 3", "the flown battery is 2 Marks behind: " + now + " → " + current);
            Check(Pip.Title(hwA) == "Testcell I Mk. 1", "its own title: " + Pip.Title(hwA));

            // 5. A Space Center build makes new parts at the current Mk, not the file's Mk 1 (play-test #7).
            List<BuildsLink.LibraryBuild> lib = BuildsLink.Library();
            BuildsLink.LibraryBuild item = lib.FirstOrDefault(i => i.Design.Id == d.Id);
            Check(item != null && item.Craft.Children("PART").All(p => MkIn(p) == "3"), "the library build's parts are Mk 3 (" + string.Join(", ", item?.Craft.Children("PART").Select(MkIn).ToArray() ?? new string[0]) + ")");
            Check(item != null && item.Quote.OldBin.Count == 0 && item.Blocked == null, "it can be built: " + item?.Blocked);
            if (item == null) yield break;
            BuildJob libJob = BuildsLink.BuildFromLibrary(item, false, true, out string err);
            HangarShip built = sc.Hangar.FirstOrDefault(x => x.Ready && x.Id != hId && x.Name == DesignName);
            Check(libJob != null && built != null && built.Craft.Children("PART").All(p => MkIn(p) == "3"), "built from the Space Center, it's a Mk 3 ship " + err);
            if (built != null) sc.Sell(built);

            // 6. The VAB: the flown battery (hangar hardware) and a new one in one load. The hardware keeps Mk 1, the new
            // part comes out Mk 3 (PIP asks the suite as the editor builds each part).
            yield return ToEditor();
            sc = PRSScenario.Instance;
            h = sc.Hangar.FirstOrDefault(x => x.Id == hId);
            if (h == null) { Check(false, "the flown copy is still in the hangar"); yield break; }
            // The first battery is its hardware (noted by the suite), the second a new part (no note).
            Node mix = h.Noted();
            Hardware.Set(mix.Children("PART").Last(), null);
            string hwNote = Hardware.Make(h.CraftId, 0);
            EditorAddon.LoadNoted(mix, h.DisplayName, h.CraftId, "pip-mix");
            yield return WaitFor(() => EditorLogic.fetch.ship != null && EditorLogic.fetch.ship.parts.Count == 2, 30, "the mixed ship loaded in the VAB");
            yield return new WaitForSeconds(2f);
            Part hwPart = EditorLogic.fetch.ship.parts.FirstOrDefault(p => PRSPart.NoteOf(p) == hwNote);
            Part newPart = EditorLogic.fetch.ship.parts.FirstOrDefault(p => PRSPart.NoteOf(p) == null);
            Check(hwPart != null && LiveMk(hwPart) == 1, "in the VAB the hangar hardware stays Mk 1 (" + (hwPart != null ? LiveMk(hwPart) : -1) + ")");
            Check(newPart != null && LiveMk(newPart) == 3, "and the new part beside it comes out Mk 3 (" + (newPart != null ? LiveMk(newPart) : -1) + ")");
            yield return new WaitForSeconds(1f);
            Shot("pip_vab_mixed");

            // Built as it is, the build keeps both.
            EditorAddon.Instance.OpenBuildPanel();
            BuildPanel panel = EditorAddon.Instance.Panel;
            Check(panel.KeepShipId == hId, "the Build panel keeps the flown ship (it's the ship opened): " + panel.Title);
            Check(panel.Confirm(), "building it " + panel.Error);
            BuildJob job = sc.Jobs.LastOrDefault();
            List<Node> jobParts = job?.Craft.Children("PART").ToList() ?? new List<Node>();
            Node jobHw = jobParts.Where((p, i) => job.Owners.ContainsKey(i)).FirstOrDefault(), jobNew = jobParts.Where((p, i) => !job.Owners.ContainsKey(i)).FirstOrDefault();
            Check(jobHw != null && MkIn(jobHw) == "1" && jobNew != null && MkIn(jobNew) == "3", "the build has the hardware at Mk 1 and the new part at Mk 3 (" + MkIn(jobHw ?? new Node()) + ", " + MkIn(jobNew ?? new Node()) + ")");
            if (job != null) sc.Outsource(job, out err);
            h = sc.Hangar.FirstOrDefault(x => x.Id == hId);
            Check(h != null && h.Ready && h.PartCount == 2 && h.Craft.Children("PART").Count(p => MkIn(p) == "3") == 1, "finished, it's still " + h?.DisplayName + ", with the new battery");
            BinKind oldB = sc.Bin.FirstOrDefault(k => k.Count > 0 && MkIn(k.Sample) == "1");
            Check(oldB != null && oldB.Count == 1 && Hardware.PartName(oldB.Sample) == partName && Pip.Title(oldB.Sample) == "Testcell I Mk. 1", "the battery it left out is in the parts bin, still Mk 1");

            // 8. Back at the Space Center: the old-Mk bin battery fits the design, so a build offers it, doesn't use it.
            yield return ToSpaceCenter();
            sc = PRSScenario.Instance;
            item = BuildsLink.Library().FirstOrDefault(i => i.Design.Id == d.Id);
            Owner offer = item?.Quote.OldBin.FirstOrDefault();
            oldB = sc.Bin.FirstOrDefault(k => k.Count > 0 && MkIn(k.Sample) == "1");
            Check(offer != null && oldB != null && offer.BinKey == oldB.Key && !item.Quote.BinTaken.ContainsKey(oldB.Key) && OldPartsQuestion.List(item.Quote.OldBin) == "Testcell I Mk. 1 → Mk. 3",
                  "the library build offers the Mk 1 bin battery, not using it: " + (item != null ? OldPartsQuestion.List(item.Quote.OldBin) : ""));
            Check(item != null && BuildsLink.BuildFromLibrary(item, false, true, out err) == null, "and won't build until that's answered (" + err + ")");
            double fee = item?.Quote.UpdatePrice ?? 0, f0 = Funds, expect = oldB != null ? KspCosts.Instance.Cost(Pip.Preview(oldB.Sample)).Dry * 2 * Suite.Settings.PipUpdatePerMk : -1;
            Check(fee > 0 && Near(fee, expect, 0.5), "updating it costs 10% of its price for each of 2 Marks (" + Pricing.Funds(fee) + ")");
            Check(oldB != null && sc.UpdatePip(sc.BinKindsOf(item.Quote.OldBin), out err) && Near(f0 - Funds, fee, 0.5) && sc.Bin.Any(k => k.Count == 1 && Pip.Title(k.Sample) == "Testcell I Mk. 3") && !sc.Bin.Any(k => k.Count > 0 && MkIn(k.Sample) == "1"),
                  "Update and use: paid, and the bin battery is Mk 3 " + err);
            item.Requote();
            Check(item.Quote.OldBin.Count == 0 && item.Quote.BinParts == 1, "now the build uses it");

            // 9. Update PIP parts on the hangar ship: its flown Mk 1 battery becomes Mk 3, for the fee.
            h = sc.Hangar.FirstOrDefault(x => x.Id == hId);
            double price = h != null ? sc.PipUpdatePrice(h.Craft.Children("PART"), out int n) : 0;
            double f1 = Funds;
            Check(h != null && price > 0 && sc.UpdatePip(h, out err) && Near(f1 - Funds, price, 0.5) && h.Craft.Children("PART").All(p => MkIn(p) == "3" && Pip.Behind(p) == 0),
                  "Update PIP parts on " + h?.DisplayName + ": every battery Mk 3, paid " + Pricing.Funds(price) + " " + err);
            if (SuiteWindow.Instance != null) { SuiteWindow.Instance.ShowHangarShip(hId); yield return new WaitForSeconds(1.5f); Shot("pip_hangar"); yield return new WaitForSeconds(1f); SuiteWindow.Instance.Show = false; }
        }
    }
}
