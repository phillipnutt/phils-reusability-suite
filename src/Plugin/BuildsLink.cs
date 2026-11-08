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
            Log.Info("Phil's Builds placed " + p.Design?.Name + " (" + p.How + ", " + p.Parts?.Count + " parts): parts stamped with their design slots");
        }

        void OnSaved(Saved s)
        {
            Stamp(s.Design, s.Parts);
        }

        // ---- Hangar copies in Phil's Builds' window ----

        static Core.Settings S => Suite.Settings;

        /// <summary>The design a hangar ship is a copy of: the design most of its parts were placed from.</summary>
        public static string DesignOf(HangarShip h)
        {
            return h.Craft.Children("PART").Select(p => DesignIn(p)).Where(x => !string.IsNullOrEmpty(x))
                .GroupBy(x => x).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        }

        static string DesignIn(Node part) => part.Children("MODULE").FirstOrDefault(m => m.Get("name") == Core.Craft.ModuleName)?.Get("design");

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
                    Text = what + " · " + (h.Flights == 0 ? "never flown" : h.Flights + (h.Flights == 1 ? " flight" : " flights")),
                    Warning = h.Missing.Count > 0 ? "missing " + h.Missing.Count + (h.Missing.Count == 1 ? " part" : " parts") : "",
                    Price = Price(q),
                    Reserved = h.ReservedBy != null,
                    ReservedText = h.ReservedBy != null ? "in the build of " + (sc.Jobs.FirstOrDefault(j => j.Id == h.ReservedBy)?.Name ?? "a ship") : "",
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
                if (q.RefitPrice > 0) d.Lines.Add(new CopyLine("From the parts bin (refit)", Pricing.Funds(q.RefitPrice), true));
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
            detail.Chips.Add(h.Missing.Count > 0 ? new CopyChip("Missing " + h.Missing.Count + (h.Missing.Count == 1 ? " part" : " parts"), ChipTone.Warn) : new CopyChip(h.Ready ? "Ready" : "Complete"));
            if (!h.Ready) detail.Lines.Add(new CopyLine("Refit", Pricing.Funds(cq.RefitPrice), true));
            int newCount = c != null ? c.NewSlots.Count : 0;
            if (newCount > 0) detail.Lines.Add(new CopyLine(newCount + " new " + (newCount == 1 ? "part" : "parts") + (design.Kind == DesignKind.Launcher && h.Missing.Count == 0 ? " (upper stage)" : ""), Pricing.Funds(cq.NewPrice)));
            detail.Lines.Add(new CopyLine(h.Ready ? "Fuel (full)" : "Fuel top-up", Pricing.Funds(cq.FuelPrice)));
            detail.Log.AddRange(sc.LogOf(h).Take(6));
            detail.Hint = newCount > 0
                ? "Picks up the whole " + design.Name + ": " + h.DisplayName + " plus " + newCount + " new " + (newCount == 1 ? "part" : "parts") + " from the design. Attach it under your payload."
                : "Picks up " + h.DisplayName + " exactly as it is.";
            return detail;
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
                foreach (string s in keep) PRSPart.EditorTrusted.Add(s);
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
