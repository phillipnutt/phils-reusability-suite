using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PhilsBuilds;
using PhilsReusabilitySuite.Core;
using UnityEngine;

[assembly: KSPAssemblyDependency("PhilsBuilds", 0, 1)]

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// The link with Phil's Builds. Phil's Builds tells the suite which design each part was placed from: parts from a
    /// launcher (or upper stage) design are the launcher a ship is built on, not the ship (Phil, round 4: "if I have it
    /// saved as just the ship, and slap a Boulder 001 underneath it and clicked build - Builds and Suite both see what's
    /// going on"). The suite lists hangar ships in Phil's Builds' library window under the design each was first built
    /// as, with their price, detail and flight log. Picking one there brings in that real ship; loading a design is
    /// always a new build.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class BuildsLink : MonoBehaviour, ICopies
    {
        public static BuildsLink Instance { get; private set; }

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            Instance = this;
            BuildsApi.DesignPlaced += OnPlaced;
            BuildsApi.DesignSaved += OnSaved;
            BuildsApi.LibraryChanged += OnLibraryChanged;
            BuildsApi.SetCopies(this);
            Log.Info("Linked with Phil's Builds (API " + BuildsApi.Version + ")");
            Pip.Find();
        }

        /// <summary>Asks Phil's Builds to redraw its window, after the hangar changed.</summary>
        public static void HangarChanged()
        {
            try { BuildsApi.RefreshWindow(); } catch (Exception ex) { Log.Warn("Couldn't refresh Phil's Builds' window: " + ex.Message); }
        }

        // ---- Stamping parts with their design ----

        static void Stamp(Design d, IList<Part> parts)
        {
            if (d == null || parts == null) return;
            for (int i = 0; i < parts.Count; i++)
            {
                PRSPart m = PRSPart.Of(parts[i]);
                if (m == null) continue;
                m.design = d.Id;
                m.slot = i;
            }
        }

        void OnPlaced(Placed p)
        {
            Stamp(p.Design, p.Parts);
            EditorAddon.Arrive(p.Parts);   // from a file: new parts, unless the suite itself is bringing a hangar copy in
            Log.Info("Phil's Builds placed " + p.Design?.Name + " (" + p.How + ", " + p.Parts?.Count + " parts): parts stamped with their design slots");
        }

        void OnSaved(Saved s)
        {
            Stamp(s.Design, s.Parts);
        }

        /// <summary>Designs filed, renamed or deleted: which ones are launchers is looked up again.</summary>
        static void OnLibraryChanged()
        {
            launcherDesigns.Clear();
            underCache.Clear();
        }

        // ---- Hangar copies in Phil's Builds' window ----

        static Core.Settings S => Suite.Settings;

        /// <summary>The design a hangar ship was first built as (set once, never changed: Phil's Builds lists it there).</summary>
        public static string DesignOf(HangarShip h) => h?.Ship.DesignId;

        /// <summary>The one design id among these (blanks ignored), or null when there are none or several.</summary>
        public static string SingleDesign(IEnumerable<string> designs)
        {
            var ids = designs.Where(x => !string.IsNullOrEmpty(x)).Distinct().Take(2).ToList();
            return ids.Count == 1 ? ids[0] : null;
        }

        /// <summary>A design's name ("Kopje 005"), or null if Phil's Builds doesn't know the id.</summary>
        public static string NameOf(string designId)
        {
            try { return BuildsApi.Find(designId)?.Name; }
            catch (Exception) { return null; }
        }

        static string DesignIn(Node part) => part.Children("MODULE").FirstOrDefault(m => m.Get("name") == Core.Craft.ModuleName)?.Get("design");

        // ---- Launchers are parts (Phil, round 4) ----

        static readonly Dictionary<string, bool> launcherDesigns = new Dictionary<string, bool>();

        /// <summary>A Phil's Builds launcher or upper-stage design (looked up once, until the library changes).</summary>
        public static bool IsLauncher(string designId)
        {
            if (string.IsNullOrEmpty(designId)) return false;
            if (launcherDesigns.TryGetValue(designId, out bool known)) return known;
            Design d;
            try { d = BuildsApi.Find(designId); } catch (Exception) { d = null; }
            bool launcher = d != null && (d.Kind == DesignKind.Launcher || d.Kind == DesignKind.UpperStage);
            launcherDesigns[designId] = launcher;
            return launcher;
        }

        /// <summary>A part placed from a launcher design: the launcher a ship is built on, not the ship.</summary>
        public static bool IsLauncherPart(Node part) => IsLauncher(DesignIn(part));

        /// <summary>Every part is a launcher's: a launcher on its own, which isn't a ship.</summary>
        public static bool OnlyLauncher(Node craft)
        {
            List<Node> parts = craft.Children("PART").ToList();
            return parts.Count > 0 && parts.All(IsLauncherPart);
        }

        /// <summary>The launcher design a craft is built on (the first launcher part's), or null.</summary>
        public static string LauncherDesign(Node craft) => craft?.Children("PART").Select(DesignIn).FirstOrDefault(IsLauncher);

        // ---- Pictures (Phil, play-test 2026-11-08 #17: pictures in the Build queue and the Hangar) ----

        /// <summary>Phil's Builds' own picture of a design, the one its library shows (its API 3: BuildsApi.PictureOf).</summary>
        static readonly System.Reflection.MethodInfo pictureCall = typeof(BuildsApi).GetMethod("PictureOf",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(string) }, null);

        /// <summary>This Phil's Builds can show design pictures (an older one can't: then the suite shows none).</summary>
        public static bool HasPictures => pictureCall != null && pictureCall.ReturnType == typeof(Texture2D);

        /// <summary>A design's picture, owned by Phil's Builds, or null when it has none.</summary>
        public static Texture2D Picture(string designId)
        {
            if (!HasPictures || string.IsNullOrEmpty(designId)) return null;
            try { return pictureCall.Invoke(null, new object[] { designId }) as Texture2D; }
            catch (Exception) { return null; }
        }

        /// <summary>The design at a craft's top (its root part, written first): the payload of a stack.</summary>
        public static string TopDesign(Node craft)
        {
            Node first = craft?.Children("PART").FirstOrDefault();
            string d = first != null ? DesignIn(first) : null;
            return string.IsNullOrEmpty(d) ? null : d;
        }

        /// <summary>
        /// What a hangar ship sits on (Phil: "could the UI show that it's the Argo on a Rockall?"): the launcher designs
        /// it's built on ("Kupe I #1 · on Boulder 001"). Null for a ship with no launcher under it.
        /// </summary>
        public static string Under(HangarShip h)
        {
            if (h?.Craft == null) return null;
            if (underCache.TryGetValue(h.Id, out var cached) && ReferenceEquals(cached.Key, h.Craft)) return cached.Value;
            var names = h.Craft.Children("PART").Select(DesignIn).Where(IsLauncher).Distinct().Select(NameOf).Where(n => n != null).ToList();
            string under = names.Count == 0 ? null : string.Join(" and ", names.ToArray());
            underCache[h.Id] = new KeyValuePair<Node, string>(h.Craft, under);
            return under;
        }

        static readonly Dictionary<string, KeyValuePair<Node, string>> underCache = new Dictionary<string, KeyValuePair<Node, string>>();

        /// <summary>
        /// The name a ship made of these parts goes by: its one design ("Kopje 005"), or else the design of its first part.
        /// Null when neither is a filed design.
        /// </summary>
        public static string NameFor(IEnumerable<string> designs, string rootDesign)
        {
            string id = DesignIdFor(designs, rootDesign);
            return id != null ? NameOf(id) : null;
        }

        /// <summary>The design a new ship of these parts belongs to (by the same rule as its name), or null.</summary>
        /// <remarks>Launcher parts aren't the ship: leave them out of <paramref name="designs"/>.</remarks>
        public static string DesignIdFor(IEnumerable<string> designs, string rootDesign) =>
            SingleDesign(designs) ?? (string.IsNullOrEmpty(rootDesign) ? null : rootDesign);

        /// <summary>The design's file, read fresh (cached while the file doesn't change).</summary>
        static readonly Dictionary<string, KeyValuePair<DateTime, Node>> designFiles = new Dictionary<string, KeyValuePair<DateTime, Node>>();

        static Node DesignFile(Design d)
        {
            if (d == null || string.IsNullOrEmpty(d.Path) || !File.Exists(d.Path)) return null;
            DateTime written = File.GetLastWriteTimeUtc(d.Path);
            if (designFiles.TryGetValue(d.Path, out KeyValuePair<DateTime, Node> cached) && cached.Key == written) return cached.Value;
            ConfigNode n = ConfigNode.Load(d.Path);
            Node node = n != null ? KspNodes.FromKsp(Upgraded(n, d.Name)) : null;
            designFiles[d.Path] = new KeyValuePair<DateTime, Node>(written, node);
            return node;
        }

        /// <summary>
        /// A design file as KSP itself would load it: KSP renames some old parts on load ("RCSBlock" became
        /// "RCSBlock.v2"), so an older file is run through KSP's own upgrade scripts before its part names are
        /// compared with hangar parts. Runs the scripts directly, without KSP's "couldn't upgrade" dialog.
        /// </summary>
        internal static ConfigNode Upgraded(ConfigNode n, string name)
        {
            try
            {
                var pipe = typeof(KSPUpgradePipeline).GetProperty("Pipeline", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(null, null) as SaveUpgradePipeline.SaveUpgradePipeline;
                if (pipe == null) return n;
                var app = new Version(Versioning.version_major, Versioning.version_minor, Versioning.Revision);
                ConfigNode up = pipe.Run(n, SaveUpgradePipeline.LoadContext.Craft, app, out bool ok, out string info);
                return ok && up != null ? up : n;
            }
            catch (Exception ex)
            {
                Log.Warn("Couldn't run KSP's upgrade scripts on " + name + ": " + ex.Message);
                return n;
            }
        }

        static string Price(Quote q) => Pricing.Funds(q.Total) + " · " + Pricing.Duration(q.Seconds, S);

        // ---- Building a filed design without opening the editor ----

        /// <summary>A filed design that can be queued from the suite window, with its price and anything stopping it.</summary>
        public sealed class LibraryBuild
        {
            public Design Design;
            public Node Craft;
            public Quote Quote;
            public string Facility;
            public int Parts;
            /// <summary>Ready, unreserved copies already in the hangar.</summary>
            public int Ready;
            /// <summary>Why it can't be built here and now, or null.</summary>
            public string Blocked;
            public string KindLabel;
            /// <summary>Old-Mk bin parts the player chose to build new instead.</summary>
            public HashSet<string> Skip = new HashSet<string>();
            public void Requote() { Quote = PRSScenario.Instance?.QuoteFor(Craft, Design.Name, null, Skip); }
        }

        /// <summary>The design as a brand-new build: every part new.</summary>
        public static Node FreshCraft(Design d)
        {
            Node file = DesignFile(d);
            if (file == null) return null;
            Node craft = Hardware.Stamped(file, d.Id);
            craft.Set("ship", d.Name);
            // New parts are what the VAB would make: each Phil's Iterative Parts part at its program's current Mk,
            // not the Mk the design file was saved with (play-test #7: Gallant I #4 flew Mk 1 parts, paid as Mk 3).
            Pip.MakeCurrent(craft, p => true);
            return craft;
        }

        /// <summary>
        /// Every filed craft, newest marks first, priced as a new build. Not launchers or upper stages: on their own they
        /// aren't ships (Phil, round 4), so they're built under a ship in the VAB, from the parts bin where it has them.
        /// </summary>
        public static List<LibraryBuild> Library()
        {
            var list = new List<LibraryBuild>();
            PRSScenario sc = PRSScenario.Instance;
            if (sc == null) return list;
            IEnumerable<Design> designs;
            try { designs = BuildsApi.Designs().Where(d => d.Kind == DesignKind.Craft).ToList(); }
            catch (Exception ex) { Log.Warn("Couldn't read Phil's Builds' library: " + ex.Message); return list; }
            foreach (Design d in designs)
            {
                try
                {
                    Node craft = FreshCraft(d);
                    if (craft == null) continue;
                    string facility = d.Facility == "SPH" ? "SPH" : "VAB";
                    var item = new LibraryBuild
                    {
                        Design = d,
                        Craft = craft,
                        Facility = facility,
                        Parts = craft.Children("PART").Count(),
                        Quote = sc.QuoteFor(craft, d.Name, null),
                        Ready = sc.Hangar.Count(h => h.Ready && h.ReservedBy == null && DesignOf(h) == d.Id),
                        KindLabel = d.CategoryLabel + " · " + facility,
                    };
                    item.Blocked = WhyNot(craft, facility);
                    list.Add(item);
                }
                catch (Exception ex) { Log.Warn("Couldn't price " + d.Name + ": " + ex.Message); }
            }
            return list.OrderBy(i => i.Design.Family).ThenByDescending(i => i.Design.Mark).ThenBy(i => i.Design.Name).ToList();
        }

        /// <summary>
        /// What KSP's own checks would stop, done from the craft file since there's no ship in an editor: parts not
        /// researched, not bought or not installed, too many parts for the building, too heavy or too big for the
        /// launch pad (or runway). Null when it can be built.
        /// </summary>
        public static string WhyNot(Node craft, string facility)
        {
            bool vab = facility != "SPH";
            var parts = craft.Children("PART").ToList();
            bool tech = HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode != Game.Modes.SANDBOX && ResearchAndDevelopment.Instance != null;
            double mass = 0;
            foreach (Node p in parts)
            {
                string r = p.Get("part") ?? "";
                string name = r.Contains("_") ? r.Substring(0, r.LastIndexOf('_')) : r;
                AvailablePart ap = PartLoader.getPartInfoByName(name);
                if (ap == null) return "It uses a part that isn't installed (" + name + ").";
                if (tech && !ResearchAndDevelopment.PartTechAvailable(ap)) return "It uses the " + ap.title + ", which isn't researched yet.";
                if (tech && !ResearchAndDevelopment.PartModelPurchased(ap)) return "It uses the " + ap.title + ", which isn't bought yet.";
                ShipConstruction.GetPartCostsAndMass(KspNodes.ToKsp(p, "PART"), ap, out float dry, out float fuel, out float dryMass, out float fuelMass);
                mass += dryMass + fuelMass;
            }
            if (GameVariables.Instance == null) return null;
            float building = ScenarioUpgradeableFacilities.GetFacilityLevel(vab ? SpaceCenterFacility.VehicleAssemblyBuilding : SpaceCenterFacility.SpaceplaneHangar);
            int maxParts = GameVariables.Instance.GetPartCountLimit(building, vab);
            if (maxParts > 0 && maxParts < int.MaxValue && parts.Count > maxParts)
                return "It has " + parts.Count + " parts; the " + facility + " takes " + maxParts + " until it's upgraded.";
            float site = ScenarioUpgradeableFacilities.GetFacilityLevel(vab ? SpaceCenterFacility.LaunchPad : SpaceCenterFacility.Runway);
            float maxMass = GameVariables.Instance.GetCraftMassLimit(site, vab);
            if (maxMass > 0 && maxMass < float.MaxValue && mass > maxMass)
                return "It weighs " + Math.Round(mass, 1) + " t; the " + (vab ? "launch pad" : "runway") + " takes " + Math.Round(maxMass, 1) + " t until it's upgraded.";
            Vector3 size = ParseSize(craft.Get("size"));
            Vector3 maxSize = GameVariables.Instance.GetCraftSizeLimit(site, vab);
            if (size != Vector3.zero && maxSize.x > 0 && maxSize.x < float.MaxValue && (size.x > maxSize.x || size.y > maxSize.y || size.z > maxSize.z))
                return "It's too big for the " + (vab ? "launch pad" : "runway") + " (" + size.y.ToString("0.#") + " m tall; it takes " + maxSize.y.ToString("0.#") + " m).";
            return null;
        }

        static Vector3 ParseSize(string s)
        {
            if (string.IsNullOrEmpty(s)) return Vector3.zero;
            string[] bits = s.Split(',');
            if (bits.Length != 3) return Vector3.zero;
            float[] v = new float[3];
            for (int i = 0; i < 3; i++)
                if (!float.TryParse(bits[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v[i])) return Vector3.zero;
            return new Vector3(v[0], v[1], v[2]);
        }

        /// <summary>"Restore" for a hangar ship: rebuilt from its own blueprint, its parts in their places, new ones for what it lost.</summary>
        public sealed class Restore
        {
            public Node Craft;
            public Quote Quote;
            public string ShipName;
            public int NewParts;
            public string Facility;
            public string Blocked;
            /// <summary>Old-Mk bin parts the player chose to build new instead.</summary>
            public HashSet<string> Skip = new HashSet<string>();
            public void Requote() { Quote = PRSScenario.Instance?.QuoteFor(Craft, ShipName, null, Skip); }
        }

        /// <summary>
        /// The hangar ship rebuilt from its own blueprint (Phil, round 4: "couldn't it have a blueprint of the 'Peleus III #2'
        /// and restore uses that?"), or null when it has lost nothing.
        /// </summary>
        public static Restore RestoreOf(HangarShip h, HashSet<string> skip = null)
        {
            PRSScenario sc = PRSScenario.Instance;
            if (sc == null) return null;
            Node craft = sc.RestoreCraft(h, out int newParts);
            if (craft == null || newParts == 0) return null;
            string facility = Suite.FacilityOf(craft);
            var r = new Restore
            {
                Craft = craft,
                ShipName = h.DisplayName,
                NewParts = newParts,
                Facility = facility,
                Blocked = WhyNot(craft, facility),
            };
            if (skip != null) r.Skip = skip;
            r.Requote();
            return r;
        }

        /// <summary>Queues the restore. The ship keeps its name and number, and is ready to launch when it's done.</summary>
        public static BuildJob RestoreBuild(HangarShip h, Restore r, bool overtime, bool outsource, out string error)
        {
            error = null;
            PRSScenario sc = PRSScenario.Instance;
            if (sc == null || r == null) { error = "There's nothing to restore."; return null; }
            if (r.Blocked != null) { error = h.DisplayName + " can't be restored yet. " + r.Blocked; return null; }
            BuildJob job = sc.StartBuild(r.Craft, h.Name, r.Facility, r.Quote, overtime, outsource, h.Id, out error);
            if (job != null && !outsource) Suite.Message(h.DisplayName + " is being restored: " + Pricing.Duration(job.SecondsLeft(S), S));
            return job;
        }

        /// <summary>Queues a library build, paid now, exactly as the Build panel would.</summary>
        public static BuildJob BuildFromLibrary(LibraryBuild item, bool overtime, bool outsource, out string error)
        {
            error = null;
            PRSScenario sc = PRSScenario.Instance;
            if (sc == null || item == null) { error = "The suite's save data isn't loaded."; return null; }
            if (item.Blocked != null) { error = item.Design.Name + " can't be built yet. " + item.Blocked; return null; }
            BuildJob job = sc.StartBuild(item.Craft, item.Design.Name, item.Facility, item.Quote, overtime, outsource, null, out error);
            if (job != null && !outsource) Suite.Message(item.Design.Name + " is being built: " + Pricing.Duration(job.SecondsLeft(S), S));
            return job;
        }

        /// <summary>What picking a hangar ship up brings in: the whole ship from its own blueprint when it lost parts (what it
        /// lost new), else exactly as it's stored (a ready ship with the launcher it was built on).</summary>
        static Node WholeShip(PRSScenario sc, HangarShip h, out int newParts)
        {
            Node restored = sc.RestoreCraft(h, out newParts);
            if (restored != null && newParts > 0) return restored;
            newParts = 0;
            return h.Noted();
        }

        public IList<CopyRow> CopiesOf(Design design)
        {
            var rows = new List<CopyRow>();
            PRSScenario sc = PRSScenario.Instance;
            if (sc == null || design == null) return rows;
            if (design.Kind == DesignKind.Launcher || design.Kind == DesignKind.UpperStage)
            {
                Node file = DesignFile(design);
                if (file != null) rows.Add(new CopyRow { Id = "new", IsNew = true, Label = "New", Text = "built from the design", Price = Price(sc.QuoteFor(Hardware.Stamped(file, design.Id), design.Name, null)) });
            }
            // The design each ship was first built as, for good (Phil: "The Peleus III #2 knows it's a Peleus III").
            foreach (HangarShip h in sc.Hangar.Where(x => DesignOf(x) == design.Id))
            {
                Quote q = sc.QuoteFor(WholeShip(sc, h, out _), h.DisplayName, null);
                rows.Add(new CopyRow
                {
                    Id = h.Id,
                    Label = "#" + h.Number,
                    Text = (h.Ready ? "ready" : "stored") + " · " + (h.Flights == 0 ? "never flown" : h.Flights + (h.Flights == 1 ? " flight" : " flights")) + (h.Flights > 0 || h.Wear >= 0.005 ? " · wear " + Core.Wear.Percent(h.Wear) : ""),
                    Warning = h.Missing > 0 ? "missing " + Suite.Parts(h.Missing) : "",
                    Price = Price(q),
                    Reserved = h.ReservedBy != null,
                    ReservedText = h.ReservedBy != null ? "in the build of " + (sc.Jobs.FirstOrDefault(j => j.Id == h.ReservedBy) is BuildJob bj ? sc.TitleOf(bj) : "a ship") : "",
                });
            }
            return rows;
        }

        public string Note(Design design)
        {
            PRSScenario sc = PRSScenario.Instance;
            if (sc == null || design == null) return "";
            var copies = sc.Hangar.Where(x => DesignOf(x) == design.Id).ToList();
            if (copies.Count == 0) return "";
            int ready = copies.Count(x => x.Ready), flown = copies.Count - ready;
            var bits = new List<string>();
            if (ready > 0) bits.Add(ready + " ready");
            if (flown > 0) bits.Add(flown + " flown");
            return string.Join(", ", bits.ToArray()) + " in the hangar";
        }

        public CopyDetail Detail(Design design, CopyRow row)
        {
            PRSScenario sc = PRSScenario.Instance;
            if (sc == null || design == null || row == null) return null;
            string kind = design.Kind == DesignKind.Launcher ? "Launcher" : design.Kind == DesignKind.UpperStage ? "Upper stage" : design.CategoryLabel;
            if (row.IsNew)
            {
                Node file = DesignFile(design);
                if (file == null) return null;
                Quote q = sc.QuoteFor(Hardware.Stamped(file, design.Id), design.Name, null);
                var d = new CopyDetail { Eyebrow = kind + " · " + design.Family + " family", Title = design.Name + ", new", TotalLabel = "Total · build time", Total = Price(q) };
                d.Lines.Add(new CopyLine("New parts", Pricing.Funds(q.NewPrice)));
                if (q.BinParts > 0) d.Lines.Add(new CopyLine(Suite.Parts(q.BinParts) + " from the parts bin, already paid for", Pricing.Funds(0), true));
                d.Lines.Add(new CopyLine("Fuel", Pricing.Funds(q.FuelPrice)));
                d.Hint = "A brand-new " + design.Name + ".";
                return d;
            }
            HangarShip h = sc.Hangar.FirstOrDefault(x => x.Id == row.Id);
            if (h == null) return null;
            Quote cq = sc.QuoteFor(WholeShip(sc, h, out int newCount), h.DisplayName, null);
            var detail = new CopyDetail
            {
                Eyebrow = "In the hangar · " + design.Name,
                Title = h.DisplayName,
                TotalLabel = "Total · build time",
                Total = Price(cq),
                LogTitle = "Flights",
            };
            detail.Chips.Add(new CopyChip(h.Flights == 1 ? "1 flight" : h.Flights + " flights", ChipTone.Good));
            detail.Chips.Add(h.Missing > 0 ? new CopyChip("Missing " + Suite.Parts(h.Missing), ChipTone.Warn) : new CopyChip(h.Ready ? "Ready" : "Stored"));
            detail.Chips.Add(new CopyChip("Wear " + Core.Wear.Percent(h.Wear), Core.Wear.WornOut(h.Wear) ? ChipTone.Warn : ChipTone.Plain));
            // Its own Phil's Iterative Parts parts that are behind their programs (updated from the suite's hangar).
            int behind = h.Craft.Children("PART").Count(p => Pip.Behind(p) > 0);
            if (behind > 0) detail.Chips.Add(new CopyChip((behind == 1 ? "1 PIP part" : behind + " PIP parts") + " behind", ChipTone.Warn));
            if (cq.RefurbishPrice >= 0.5) detail.Lines.Add(new CopyLine("Refurbish to 0% wear", Pricing.Funds(cq.RefurbishPrice), true));
            if (newCount > 0) detail.Lines.Add(new CopyLine(Suite.Parts(newCount).Replace(" part", " new part") + " for what it lost", Pricing.Funds(cq.NewPrice)));
            detail.Lines.Add(new CopyLine(h.Ready ? "Fuel (full)" : "Fuel top-up", Pricing.Funds(cq.FuelPrice)));
            detail.Log.AddRange(sc.LogOf(h).Take(6));
            // Phil: "load", not "pick up". Phil's Builds' own button brings the whole ship (what it lost new); "Load as
            // stored" beside it (Phil's Builds 2 and later) brings only what's stored.
            bool canOpen = h.ReservedBy == null && openLabel != null && openAction != null;
            detail.Hint = newCount > 0
                ? h.DisplayName + " whole again: its own parts and " + Suite.Parts(newCount).Replace(" part", " new part") + " for what it lost."
                  + (canOpen ? " Load as stored brings only its own " + Suite.Parts(h.PartCount) + "." : "")
                : h.DisplayName + " exactly as it is.";
            if (canOpen) OfferOpen(detail, h);
            return detail;
        }

        static readonly System.Reflection.FieldInfo openLabel = typeof(CopyDetail).GetField("OpenLabel"), openAction = typeof(CopyDetail).GetField("Open");

        static void OfferOpen(CopyDetail detail, HangarShip h)
        {
            string id = h.Id;
            openLabel.SetValue(detail, "Load as stored");
            openAction.SetValue(detail, (Action)(() =>
            {
                HangarShip ship = PRSScenario.Instance?.Hangar.FirstOrDefault(x => x.Id == id);
                if (ship != null && ship.ReservedBy == null) EditorAddon.OpenHangarShip(ship);
            }));
        }

        /// <summary>Phil's Builds' Load (or Pick up) on a hangar ship: the real ship, whole, onto the cursor or into the editor.</summary>
        public void Build(Design design, CopyRow row, Action<ShipConstruct> hold)
        {
            try
            {
                PRSScenario sc = PRSScenario.Instance;
                HangarShip h = sc?.Hangar.FirstOrDefault(x => x.Id == row.Id);
                if (h == null || h.ReservedBy != null) { hold(null); return; }
                Node craft = WholeShip(sc, h, out int newParts);
                // Its own parts come in as its hardware (noted by the suite itself); what it lost comes in new. Into an
                // empty editor it's the ship opened; onto another ship, it's owned parts of that one's build.
                EditorAddon.Expect(h.CraftId);
                if (EditorLogic.fetch == null || EditorLogic.fetch.ship == null || EditorLogic.fetch.ship.parts.Count == 0) EditorAddon.OpenedCraft = h.CraftId;
                ConfigNode n = KspNodes.ToKsp(craft, "");
                n.SetValue("ship", h.Name, true);
                var template = new ShipTemplate();
                template.LoadShip(n);
                ShipConstruction.CreateConstructFromTemplate(template, ship =>
                {
                    if (ship != null) Log.Info("Picked up " + h.DisplayName + " from the hangar (" + ship.parts.Count + " parts, " + newParts + " of them new)");
                    hold(ship);
                });
            }
            catch (Exception ex)
            {
                Log.Error("Couldn't bring " + row?.Id + " in from the hangar: " + ex);
                hold(null);
            }
        }
    }
}
