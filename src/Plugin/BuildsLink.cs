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
    /// The link with Phil's Builds. Phil's Builds tells the suite which design and slot each part was placed
    /// from (and saved as), and the suite shows hangar copies in Phil's Builds' library window: the flown
    /// copies of each design with their price, detail and flight log. Picking a flown first stage puts the
    /// whole launcher on the cursor: that exact stage plus new parts from the design.
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
            BuildsApi.SetCopies(this);
            Log.Info("Linked with Phil's Builds (API " + BuildsApi.Version + ")");
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
            EditorAddon.CheckParts(p.Parts);   // picked up from a file: the editor's one serial rule, at once
            Log.Info("Phil's Builds placed " + p.Design?.Name + " (" + p.How + ", " + p.Parts?.Count + " parts): parts stamped with their design slots");
        }

        void OnSaved(Saved s)
        {
            Stamp(s.Design, s.Parts);
        }

        // ---- Hangar copies in Phil's Builds' window ----

        static Core.Settings S => Suite.Settings;

        /// <summary>
        /// The design a hangar ship is a copy of: the one design every design-placed part of it came from (parts
        /// added by hand don't count). A ship made of two designs, such as a payload stacked on a launcher, is a
        /// copy of neither: it waits in the hangar as itself, until its first stage comes home on its own.
        /// </summary>
        public static string DesignOf(HangarShip h) => SingleDesign(h.Craft.Children("PART").Select(DesignIn));

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

        static readonly Dictionary<string, KeyValuePair<Node, string>> underCache = new Dictionary<string, KeyValuePair<Node, string>>();

        /// <summary>
        /// What a hangar ship is stacked on (Phil: "could the UI show that it's the Argo on a Rockall?"): another
        /// hangar ship's own hardware by its name and number ("Rockall 002 #1"), or a design built new with it
        /// ("Rockall 002"). Null for a ship of one piece. Parts added by hand don't count.
        /// </summary>
        public static string Under(HangarShip h)
        {
            if (h?.Craft == null) return null;
            if (underCache.TryGetValue(h.Id, out var cached) && ReferenceEquals(cached.Key, h.Craft)) return cached.Value;
            var parts = h.Craft.Children("PART").ToList();
            string rootDesign = parts.Count > 0 ? DesignIn(parts[0]) : null;
            var labels = new List<string>();
            foreach (Node p in parts)
            {
                Node m = p.Children("MODULE").FirstOrDefault(x => x.Get("name") == Core.Craft.ModuleName);
                if (m == null) continue;
                string hull = m.Get("hull"), design = m.Get("design"), label = null;
                if (!string.IsNullOrEmpty(hull) && hull != h.Hull && HangarShip.ParseHull(hull, out string name, out int number)) label = name + " #" + number;
                else if (!string.IsNullOrEmpty(design) && design != rootDesign) label = NameOf(design);
                if (label != null && !labels.Contains(label)) labels.Add(label);
            }
            string under = labels.Count == 0 ? null : string.Join(" and ", labels);
            underCache[h.Id] = new KeyValuePair<Node, string>(h.Craft, under);
            return under;
        }

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

        /// <summary>A hangar ship completed to its design: its own parts in their slots, the rest new.</summary>
        public static CompletionResult Complete(Design d, HangarShip h)
        {
            Node file = DesignFile(d);
            return file == null ? null : Completion.Complete(file, d.Id, h.Craft.Children("PART"));
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
        }

        /// <summary>The design as a brand-new build: every part new, each with a serial of its own.</summary>
        public static Node FreshCraft(Design d)
        {
            Node file = DesignFile(d);
            if (file == null) return null;
            Node craft = Completion.Complete(file, d.Id, new Node[0]).Craft;
            craft.Set("ship", d.Name);
            foreach (Node part in craft.Children("PART"))
            {
                Node m = part.Children("MODULE").FirstOrDefault(x => x.Get("name") == Core.Craft.ModuleName);
                if (m != null) m.Set("serial", PRSPart.NewSerial());
            }
            return craft;
        }

        /// <summary>Every filed craft, launcher and upper stage, newest marks first, priced as a new build.</summary>
        public static List<LibraryBuild> Library()
        {
            var list = new List<LibraryBuild>();
            PRSScenario sc = PRSScenario.Instance;
            if (sc == null) return list;
            IEnumerable<Design> designs;
            try { designs = BuildsApi.Designs().Where(d => d.Kind != DesignKind.Block).ToList(); }
            catch (Exception ex) { Log.Warn("Couldn't read Phil's Builds' library: " + ex.Message); return list; }
            foreach (Design d in designs)
            {
                try
                {
                    Node craft = FreshCraft(d);
                    if (craft == null) continue;
                    string facility = d.Kind == DesignKind.Craft && d.Facility == "SPH" ? "SPH" : "VAB";
                    var item = new LibraryBuild
                    {
                        Design = d,
                        Craft = craft,
                        Facility = facility,
                        Parts = craft.Children("PART").Count(),
                        Quote = sc.QuoteFor(craft, d.Name, null),
                        Ready = sc.Hangar.Count(h => h.Ready && h.ReservedBy == null && DesignOf(h) == d.Id),
                        KindLabel = d.Kind == DesignKind.Launcher ? "Launcher" + (d.Tons > 0 ? " · " + d.Tons.ToString(d.Tons < 1 ? "0.##" : "0.#") + " t" : "")
                                  : d.Kind == DesignKind.UpperStage ? "Upper stage" : (d.CategoryLabel + " · " + facility),
                    };
                    item.Blocked = WhyNot(craft, facility);
                    list.Add(item);
                }
                catch (Exception ex) { Log.Warn("Couldn't price " + d.Name + ": " + ex.Message); }
            }
            return list.OrderBy(i => i.Design.Kind == DesignKind.Craft ? 1 : 0).ThenBy(i => i.Design.Family).ThenByDescending(i => i.Design.Mark).ThenBy(i => i.Design.Name).ToList();
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

        /// <summary>"Restore to design" for a hangar ship: its own parts in their slots, new parts for the rest.</summary>
        public sealed class Restore
        {
            public Design Design;
            public Node Craft;
            public Quote Quote;
            public int NewParts;
            public string Facility;
            public string Blocked;
        }

        /// <summary>
        /// The hangar ship built back up to its whole design (a flown first stage becomes the whole launcher
        /// again), or null when it isn't a copy of a filed design or already has every part.
        /// </summary>
        public static Restore RestoreOf(HangarShip h)
        {
            PRSScenario sc = PRSScenario.Instance;
            string id = DesignOf(h);
            if (sc == null || id == null) return null;
            Design d;
            try { d = BuildsApi.Find(id); } catch (Exception) { return null; }
            if (d == null) return null;
            CompletionResult c = Complete(d, h);
            if (c == null || c.NewSlots.Count == 0) return null;
            Node craft = c.Craft;
            craft.Set("ship", h.Name);
            foreach (Node part in craft.Children("PART"))
            {
                Node m = part.Children("MODULE").FirstOrDefault(x => x.Get("name") == Core.Craft.ModuleName);
                if (m != null && string.IsNullOrEmpty(m.Get("serial"))) m.Set("serial", PRSPart.NewSerial());
            }
            string facility = Suite.FacilityOf(craft);
            return new Restore
            {
                Design = d,
                Craft = craft,
                Quote = sc.QuoteFor(craft, h.DisplayName, null),
                NewParts = c.NewSlots.Count,
                Facility = facility,
                Blocked = WhyNot(craft, facility),
            };
        }

        /// <summary>Queues the restore. The ship keeps its name and number, and is ready to launch when it's done.</summary>
        public static BuildJob RestoreBuild(HangarShip h, Restore r, bool overtime, bool outsource, out string error)
        {
            error = null;
            PRSScenario sc = PRSScenario.Instance;
            if (sc == null || r == null) { error = "There's nothing to restore."; return null; }
            if (r.Blocked != null) { error = h.DisplayName + " can't be restored yet. " + r.Blocked; return null; }
            BuildJob job = sc.StartBuild(r.Craft, h.Name, r.Facility, r.Quote, overtime, outsource, h.Id, out error);
            if (job != null && !outsource) Suite.Message(h.DisplayName + " is being restored to " + r.Design.Name + ": " + Pricing.Duration(job.SecondsLeft(S), S));
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

        public IList<CopyRow> CopiesOf(Design design)
        {
            var rows = new List<CopyRow>();
            PRSScenario sc = PRSScenario.Instance;
            if (sc == null || design == null) return rows;
            if (design.Kind == DesignKind.Launcher || design.Kind == DesignKind.UpperStage)
            {
                Node file = DesignFile(design);
                if (file != null)
                {
                    Node fresh = Completion.Complete(file, design.Id, new Node[0]).Craft;
                    rows.Add(new CopyRow { Id = "new", IsNew = true, Label = "New", Text = "built from the design", Price = Price(sc.QuoteFor(fresh, design.Name, null)) });
                }
            }
            foreach (HangarShip h in sc.Hangar.Where(x => DesignOf(x) == design.Id))
            {
                CompletionResult c = Complete(design, h);
                Quote q = c != null ? sc.QuoteFor(c.Craft, h.DisplayName, null) : sc.QuoteFor(h.Craft, h.DisplayName, null);
                bool whole = c == null || c.NewSlots.Count == 0;
                string what = whole ? (h.Ready ? "ready" : "complete") : (design.Kind == DesignKind.Launcher ? "first stage" : "part of it");
                rows.Add(new CopyRow
                {
                    Id = h.Id,
                    Label = "#" + h.Number,
                    Text = what + " · " + (h.Flights == 0 ? "never flown" : h.Flights + (h.Flights == 1 ? " flight" : " flights")) + " · wear " + Core.Wear.Percent(h.Wear),
                    Warning = h.Missing.Count > 0 ? "missing " + Suite.Parts(h.Missing.Count) : "",
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
            if (flown > 0) bits.Add(flown + (design.Kind == DesignKind.Launcher ? (flown == 1 ? " flown first stage" : " flown first stages") : " flown"));
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
                Quote q = sc.QuoteFor(Completion.Complete(file, design.Id, new Node[0]).Craft, design.Name, null);
                var d = new CopyDetail { Eyebrow = kind + " · " + design.Family + " family", Title = design.Name + ", new", TotalLabel = "Total · build time", Total = Price(q) };
                d.Lines.Add(new CopyLine("New parts", Pricing.Funds(q.NewPrice)));
                if (q.RefurbishPrice > 0) d.Lines.Add(new CopyLine("From the parts bin (refurbished)", Pricing.Funds(q.RefurbishPrice), true));
                d.Lines.Add(new CopyLine("Fuel", Pricing.Funds(q.FuelPrice)));
                d.Hint = "A brand-new " + design.Name + ".";
                return d;
            }
            HangarShip h = sc.Hangar.FirstOrDefault(x => x.Id == row.Id);
            if (h == null) return null;
            CompletionResult c = Complete(design, h);
            Quote cq = sc.QuoteFor(c != null ? c.Craft : h.Craft, h.DisplayName, null);
            var detail = new CopyDetail
            {
                Eyebrow = "Hangar copy · " + design.Name,
                Title = h.DisplayName,
                TotalLabel = "Total · build time",
                Total = Price(cq),
                LogTitle = "Flights",
            };
            detail.Chips.Add(new CopyChip((c != null && c.NewSlots.Count > 0 && design.Kind == DesignKind.Launcher ? "First stage · " : "") + (h.Flights == 1 ? "1 flight" : h.Flights + " flights"), ChipTone.Good));
            detail.Chips.Add(h.Missing.Count > 0 ? new CopyChip("Missing " + Suite.Parts(h.Missing.Count), ChipTone.Warn) : new CopyChip(h.Ready ? "Ready" : "Complete"));
            detail.Chips.Add(new CopyChip("Wear " + Core.Wear.Percent(h.Wear), Core.Wear.WornOut(h.Wear) ? ChipTone.Warn : ChipTone.Plain));
            if (cq.RefurbishPrice >= 0.5) detail.Lines.Add(new CopyLine("Refurbish to 0% wear", Pricing.Funds(cq.RefurbishPrice), true));
            int newCount = c != null ? c.NewSlots.Count : 0;
            if (newCount > 0) detail.Lines.Add(new CopyLine(Suite.Parts(newCount).Replace(" part", " new part") + (design.Kind == DesignKind.Launcher && h.Missing.Count == 0 ? " (upper stage)" : ""), Pricing.Funds(cq.NewPrice)));
            detail.Lines.Add(new CopyLine(h.Ready ? "Fuel (full)" : "Fuel top-up", Pricing.Funds(cq.FuelPrice)));
            detail.Log.AddRange(sc.LogOf(h).Take(6));
            // Phil: "load", not "pick up". Phil's Builds' own button says Load (or "Add to craft" when the editor
            // already has one); "Load as stored" beside it (Phil's Builds 2 and later) brings only what's stored.
            bool canOpen = h.ReservedBy == null && openLabel != null && openAction != null;
            detail.Hint = newCount > 0
                ? "The whole " + design.Name + ": " + h.DisplayName + " plus " + Suite.Parts(newCount).Replace(" part", " new part") + " from the design."
                  + (canOpen ? " Load as stored brings only its own " + Suite.Parts(h.Placed.Count) + "." : "")
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

        public void Build(Design design, CopyRow row, Action<ShipConstruct> hold)
        {
            try
            {
                PRSScenario sc = PRSScenario.Instance;
                HangarShip h = sc?.Hangar.FirstOrDefault(x => x.Id == row.Id);
                if (h == null || h.ReservedBy != null) { hold(null); return; }
                CompletionResult c = Complete(design, h);
                Node craft = c != null ? c.Craft : h.Craft;
                IEnumerable<string> keep = c != null ? c.Used : (IEnumerable<string>)h.Placed;
                // The flown parts keep their serials in the editor; new parts get theirs when they're made.
                EditorAddon.Claim(keep);
                ConfigNode n = KspNodes.ToKsp(craft, "");
                n.SetValue("ship", h.DisplayName, true);
                var template = new ShipTemplate();
                template.LoadShip(n);
                ShipConstruction.CreateConstructFromTemplate(template, ship =>
                {
                    if (ship != null) Log.Info("Picked up " + h.DisplayName + " from the hangar as a whole " + design.Name + " (" + ship.parts.Count + " parts)");
                    hold(ship);
                });
            }
            catch (Exception ex)
            {
                Log.Error("Couldn't build " + design?.Name + " from the hangar: " + ex);
                hold(null);
            }
        }
    }
}
