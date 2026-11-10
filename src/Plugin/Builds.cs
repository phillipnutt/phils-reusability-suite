using System;
using System.Collections.Generic;
using System.Linq;
using PhilsReusabilitySuite.Core;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>The build queue: jobs on build lines, paid once at Build, finished ships into the hangar.</summary>
    public partial class PRSScenario
    {
        public readonly List<BuildJob> Jobs = new List<BuildJob>();
        double lastUT = -1;

        static Core.Settings S => Suite.Settings;

        // ---- Which parts are a real, built ship's ----

        /// <summary>The hangar ship whose current craft a VAB note points at.</summary>
        public HangarShip HangarOfCraft(string craftId) => craftId == null ? null : Hangar.FirstOrDefault(h => h.CraftId == craftId);

        /// <summary>The queued build whose current craft a VAB note points at.</summary>
        public BuildJob JobOfCraft(string craftId) => craftId == null ? null : Jobs.FirstOrDefault(j => j.CraftId == craftId);

        /// <summary>
        /// Builds cancelled lately, by their craft's id: which hangar parts their parts were. Pressing Build notes the VAB's
        /// ship as the build; cancelled, those parts are still the hangar ship's own hardware.
        /// </summary>
        readonly List<KeyValuePair<string, Dictionary<int, string>>> cancelled = new List<KeyValuePair<string, Dictionary<int, string>>>();

        Dictionary<int, string> CancelledOwners(string craftId) => cancelled.FirstOrDefault(c => c.Key == craftId).Value;

        /// <summary>A VAB note's craft is one the suite still knows: a hangar ship's, a queued build's or a cancelled build's.</summary>
        public bool Knows(string craftId) => HangarOfCraft(craftId) != null || JobOfCraft(craftId) != null || CancelledOwners(craftId) != null;

        /// <summary>
        /// A part with a note, as a build sees it: the hangar ship part it is, if that ship is still in the hangar with
        /// this very part, and free (or taken by this build). A queued build's own parts point at the hangar parts it
        /// took. Anything else is a new part: a note from an older copy of a ship points at nothing.
        /// </summary>
        public Owner OwnerOf(Node part, BuildJob editing)
        {
            if (!Hardware.Parse(Hardware.Of(part), out string craftId, out int at)) return null;
            HangarShip h = HangarOfCraft(craftId);
            if (h == null)
            {
                BuildJob j = JobOfCraft(craftId);
                Dictionary<int, string> owners = j == null ? CancelledOwners(craftId) : j == editing ? j.Owners : null;
                if (owners == null || !owners.TryGetValue(at, out string token) || !Hardware.Parse(token, out string id, out at)) return null;
                h = HangarOf(id);
            }
            if (h == null || (h.ReservedBy != null && h.ReservedBy != editing?.Id)) return null;
            Node stored = h.Craft.Children("PART").ElementAtOrDefault(at);
            if (stored == null || Hardware.PartName(stored) != Hardware.PartName(part)) return null;
            h.CameBack.TryGetValue(at, out Dictionary<string, double> left);
            // Anything added to it in the VAB (cargo, a bigger size or a dearer variant) is new: it costs its full price.
            double added = Math.Max(0, KspCosts.Instance.Cost(part).Dry - KspCosts.Instance.Cost(stored).Dry);
            return new Owner { Kind = OwnerKind.Hangar, ShipId = h.Id, Index = at, Label = h.DisplayName, Ready = h.Ready, Wear = h.Wear, Left = h.Ready ? null : left, Added = added };
        }

        /// <summary>The hangar ship this craft is exactly (every part its own, nothing else), free of any build; or null.</summary>
        public HangarShip ExactShipOf(Node craft)
        {
            List<Node> parts = craft?.Children("PART").ToList();
            if (parts == null || parts.Count == 0 || !Hardware.Parse(Hardware.Of(parts[0]), out string craftId, out _)) return null;
            HangarShip h = HangarOfCraft(craftId);
            if (h == null || h.ReservedBy != null || h.PartCount != parts.Count) return null;
            List<Node> stored = h.Craft.Children("PART").ToList();
            var seen = new HashSet<int>();
            foreach (Node p in parts)
                if (!Hardware.Parse(Hardware.Of(p), out string c, out int at) || c != craftId || at >= stored.Count || !seen.Add(at) || !SamePart(stored[at], p) || !SameLoad(stored[at], p)) return null;
            return h;
        }

        /// <summary>A stored part and an editor part are exactly the same: the same part at the same price (nothing resized,
        /// switched or added to its cargo).</summary>
        static bool SamePart(Node stored, Node part) =>
            Hardware.PartName(stored) == Hardware.PartName(part) && Math.Abs(KspCosts.Instance.Cost(stored).Dry - KspCosts.Instance.Cost(part).Dry) < 0.5;

        /// <summary>The same resources loaded (a ship changed only in its tanks isn't exactly the stored ship).</summary>
        static bool SameLoad(Node stored, Node part)
        {
            PartCost a = KspCosts.Instance.Cost(stored), b = KspCosts.Instance.Cost(part);
            return a.Amounts.Count == b.Amounts.Count && a.Amounts.All(kv => b.Amounts.TryGetValue(kv.Key, out double v) && Math.Abs(v - kv.Value) < 0.01);
        }

        /// <summary>The queued build these parts are (most of their notes point at its craft), if any.</summary>
        public BuildJob JobOf(Node craft)
        {
            var counts = new Dictionary<BuildJob, int>();
            foreach (Node p in craft.Children("PART"))
            {
                BuildJob j = Hardware.Parse(Hardware.Of(p), out string c, out _) ? JobOfCraft(c) : null;
                if (j != null) counts[j] = (counts.TryGetValue(j, out int n) ? n : 0) + 1;
            }
            return counts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
        }

        /// <summary>In a build's skip list: the player answered "No" to updating old-Mk bin parts, so none are offered.</summary>
        public const string SkipOldParts = "*old-mk";

        /// <param name="skipBin">SkipOldParts: no old-Mk bin part is offered.</param>
        public Quote QuoteFor(Node craft, string name, BuildJob editing, ICollection<string> skipBin = null)
        {
            return Quotes.Make(craft, name, p => OwnerOf(p, editing), KspCosts.Instance, S, BinMatcher(editing, skipBin), id => HangarOf(id)?.Craft.Children("PART"));
        }

        /// <summary>A hangar ship as it is: refurbishing it, refuelling it, launching it.</summary>
        public Quote QuoteFor(HangarShip h) => QuoteFor(h.Noted(), h.DisplayName, null);

        /// <summary>
        /// Matches a new part to the parts bin: a part of the same kind (part, Phil's Iterative Parts Mk and price), as many
        /// as the bin holds. A PIP part whose program has moved on since it was built is never used as it is: it comes back
        /// marked Old, so the build can ask whether to update it (Phil: old-Mk parts never fly unless updated).
        /// </summary>
        Func<Node, Owner> BinMatcher(BuildJob editing, ICollection<string> skipBin)
        {
            var left = Bin.ToDictionary(k => k.Key, k => k.Count);
            if (editing != null) foreach (KeyValuePair<string, int> t in editing.BinTaken) left[t.Key] = (left.TryGetValue(t.Key, out int n) ? n : 0) + t.Value;
            bool noOld = skipBin != null && skipBin.Contains(SkipOldParts);
            Dictionary<string, KeyValuePair<BinKind, string>> old = null;   // an old kind, by the kind it becomes once updated
            return part =>
            {
                string key = BinKeyOf(part);
                if (left.TryGetValue(key, out int n) && n > 0) { left[key] = n - 1; return new Owner { Kind = OwnerKind.Bin, BinKey = key, Label = "From the parts bin" }; }
                if (noOld) return null;
                if (old == null)
                {
                    old = new Dictionary<string, KeyValuePair<BinKind, string>>();
                    foreach (BinKind k in Bin.Where(b => b.Count > 0 && Pip.Behind(b.Sample) > 0))
                    {
                        string becomes = BinKeyOf(Pip.Preview(k.Sample));
                        if (!old.ContainsKey(becomes)) old[becomes] = new KeyValuePair<BinKind, string>(k, k.Key);
                    }
                }
                if (!old.TryGetValue(key, out KeyValuePair<BinKind, string> o) || !left.TryGetValue(o.Value, out int on) || on <= 0) return null;
                left[o.Value] = on - 1;
                int marks = Pip.Behind(o.Key.Sample, out string now, out string current);
                return new Owner { Kind = OwnerKind.Bin, Old = true, BinKey = o.Value, Marks = marks, Now = now, Current = current, Label = "From the parts bin" };
            };
        }

        // ---- Updating PIP parts to their program's current Mk ----

        /// <summary>A part's update: how many Marks, and what it costs.</summary>
        public double PipUpdatePrice(Node part, out int marks)
        {
            marks = Pip.Behind(part);
            // Priced as what it becomes (an old version updates to the newest version's part).
            return marks > 0 ? Pricing.PipUpdate(KspCosts.Instance.Cost(Pip.Preview(part)).Dry, marks, S) : 0;
        }

        /// <summary>What updating these parts costs, and how many of them are behind.</summary>
        public double PipUpdatePrice(IEnumerable<Node> parts, out int behind)
        {
            double total = 0;
            behind = 0;
            foreach (Node p in parts)
            {
                total += PipUpdatePrice(p, out int marks);
                if (marks > 0) behind++;
            }
            return total;
        }

        /// <summary>What updating these bin kinds costs (each part of each), and how many parts that is.</summary>
        public double PipUpdatePrice(IEnumerable<KeyValuePair<BinKind, int>> kinds, out int parts)
        {
            double total = 0;
            parts = 0;
            foreach (KeyValuePair<BinKind, int> k in kinds)
            {
                total += PipUpdatePrice(k.Key.Sample, out int marks) * k.Value;
                if (marks > 0) parts += k.Value;
            }
            return total;
        }

        /// <summary>
        /// "Update PIP parts" on a hangar ship: brings each part that's behind to its program's current Mk (PIP's own step,
        /// the one the VAB uses for a new part), paid now. Its wear and fuel stay as they are.
        /// </summary>
        public bool UpdatePip(HangarShip h, out string error)
        {
            error = null;
            if (h.ReservedBy != null) { error = h.DisplayName + " is in a build: update it when the build is done."; return false; }
            List<Node> parts = h.Craft.Children("PART").ToList();
            double price = PipUpdatePrice(parts, out int behind);
            if (behind == 0) { error = "Its Phil's Iterative Parts parts are all at their program's current Mk."; return false; }
            if (!CanAfford(price)) { error = "Not enough funds: updating costs " + Pricing.Funds(price) + "."; return false; }
            int done = 0;
            foreach (Node p in parts) if (Pip.Behind(p) > 0 && Pip.MakeCurrent(p, h.Craft)) done++;
            if (done == 0) { error = "Phil's Iterative Parts couldn't update them."; return false; }
            double paid = done == behind ? price : price * done / behind;
            Suite.Charge(paid);
            h.UpdateFuel();
            Changed();
            Log.Info("Updated " + Suite.Parts(done) + " of " + h.DisplayName + " to their programs' current Mk, for " + Pricing.Funds(paid));
            return true;
        }

        /// <summary>"Update PIP parts" in the parts bin (all of a kind, or as many as a build needs): each becomes its program's current Mk.</summary>
        public bool UpdatePip(IEnumerable<KeyValuePair<BinKind, int>> kinds, out string error)
        {
            error = null;
            List<KeyValuePair<BinKind, int>> list = kinds.Where(k => k.Value > 0 && k.Key.Count >= k.Value && Pip.Behind(k.Key.Sample) > 0).ToList();
            double price = PipUpdatePrice(list, out int parts);
            if (parts == 0) { error = "The parts bin's Phil's Iterative Parts parts are all at their program's current Mk."; return false; }
            if (!CanAfford(price)) { error = "Not enough funds: updating costs " + Pricing.Funds(price) + "."; return false; }
            int done = 0;
            foreach (KeyValuePair<BinKind, int> k in list)
            {
                Node updated = k.Key.Sample.Clone();
                if (!Pip.MakeCurrent(updated)) continue;
                k.Key.Count -= k.Value;
                AddToBin(updated, k.Value);
                done += k.Value;
            }
            if (done == 0) { error = "Phil's Iterative Parts couldn't update them."; return false; }
            double paid = done == parts ? price : price * done / parts;
            Suite.Charge(paid);
            Bin.RemoveAll(k => k.Count <= 0 && !Jobs.Any(j => j.BinTaken.ContainsKey(k.Key)));
            Changed();
            Log.Info("Updated " + Suite.Parts(done) + " in the parts bin to their programs' current Mk, for " + Pricing.Funds(paid));
            return true;
        }

        /// <summary>The bin kinds behind old-Mk offers in a quote, with how many of each the build wants.</summary>
        public List<KeyValuePair<BinKind, int>> BinKindsOf(IEnumerable<Owner> old) =>
            old.GroupBy(o => o.BinKey).Select(g => new KeyValuePair<BinKind, int>(Bin.FirstOrDefault(b => b.Key == g.Key), g.Count()))
               .Where(k => k.Key != null).ToList();

        // ---- Taking apart and selling ----

        /// <summary>What taking a stored ship apart costs: its parts are refurbished as they go into the bin (by its wear).</summary>
        public double TakeApartPrice(HangarShip h) => h.Craft.Children("PART").Sum(p => KspCosts.Instance.Cost(p).Dry) * Pricing.RefurbishShare(h.Wear, S);

        /// <summary>Takes a stored ship apart: every part goes to the parts bin, refurbished (paid now). The ship ends.</summary>
        public bool TakeApart(HangarShip h, out string error)
        {
            error = null;
            if (h.ReservedBy != null) { error = h.DisplayName + " is in a build in the queue."; return false; }
            double price = TakeApartPrice(h);
            if (!CanAfford(price)) { error = "Not enough funds: refurbishing its parts for the bin costs " + Pricing.Funds(price) + "."; return false; }
            Suite.Charge(price);
            int n = 0;
            foreach (Node p in h.Craft.Children("PART")) { AddToBin(p); n++; }
            Hangar.Remove(h);
            Changed();
            Log.Info("Took " + h.DisplayName + " apart: " + n + " parts to the parts bin, refurbished for " + Pricing.Funds(price));
            return true;
        }

        /// <summary>What a hangar ship sells for as scrap: half the price of its parts and the fuel it holds.</summary>
        public double ScrapValue(HangarShip h)
        {
            double total = 0;
            int i = -1;
            foreach (Node p in h.Craft.Children("PART"))
            {
                i++;
                PartCost c = KspCosts.Instance.Cost(p);
                total += c.Dry;
                IDictionary<string, double> left = !h.Ready && h.CameBack.TryGetValue(i, out Dictionary<string, double> d) ? d : null;
                foreach (KeyValuePair<string, double> res in c.Amounts)
                    total += (h.Ready ? res.Value : left != null && left.TryGetValue(res.Key, out double l) ? l : 0) * KspCosts.Instance.UnitCost(res.Key);
            }
            return total * S.ScrapValue;
        }

        public double ScrapValue(BinKind k) => KspCosts.Instance.Cost(k.Sample).Dry * S.ScrapValue * k.Count;

        public bool Sell(HangarShip h)
        {
            if (h.ReservedBy != null) return false;
            double v = ScrapValue(h);
            Suite.Charge(-v);
            Hangar.Remove(h);
            Changed();
            Log.Info("Sold " + h.DisplayName + " for scrap: " + Pricing.Funds(v));
            return true;
        }

        public double Sell(IEnumerable<BinKind> kinds)
        {
            double v = 0;
            foreach (BinKind k in kinds.ToList())
            {
                v += ScrapValue(k);
                k.Count = 0;
            }
            Bin.RemoveAll(k => k.Count <= 0 && !Jobs.Any(j => j.BinTaken.ContainsKey(k.Key)));
            Suite.Charge(-v);
            Changed();
            if (v > 0) Log.Info("Sold parts from the bin for scrap: " + Pricing.Funds(v));
            return v;
        }

        static bool CanAfford(double amount) => amount <= 0 || !Suite.Career || Suite.Funds >= amount - 0.5;

        // ---- Starting, changing and ending builds ----

        /// <summary>
        /// Why a quote no longer holds, or null if it does: a bin part it uses was sold or went into another build, or
        /// a hangar ship it uses was taken apart, sold or put in another build since it was priced.
        /// </summary>
        public string StaleQuote(Quote q, BuildJob editing, string keepShipId)
        {
            string mine = editing?.Id;
            if (editing != null && !Jobs.Contains(editing)) return "that build has been finished or cancelled";
            foreach (string id in q.HangarShips)
            {
                HangarShip h = HangarOf(id);
                if (h == null) return "a hangar ship it uses is gone";
                if (h.ReservedBy != null && h.ReservedBy != mine) return h.DisplayName + " is in another build now";
            }
            if (keepShipId != null && ShipOf(keepShipId) == null) return "the ship it keeps no longer exists";
            foreach (KeyValuePair<string, int> t in q.BinTaken)
            {
                int have = (Bin.FirstOrDefault(b => b.Key == t.Key)?.Count ?? 0) + (editing != null && editing.BinTaken.TryGetValue(t.Key, out int mineN) ? mineN : 0);
                if (have < t.Value) return "a part from the parts bin is gone";
            }
            return null;
        }

        /// <param name="keepShipId">The ship this build is: the hangar ship opened in the VAB (Phil, round 4: a build from a hangar
        /// ship is always that ship, whatever it's saved as), or the one Restore and Refurbish work on. Null: a new ship.</param>
        public BuildJob StartBuild(Node craft, string name, string facility, Quote q, bool overtime, bool outsource, string keepShipId, out string error)
        {
            error = null;
            string stale = StaleQuote(q, null, keepShipId);
            if (stale != null) { error = "Something changed since this was priced: " + stale + ". Check the new price and try again."; return null; }
            if (BuildsLink.OnlyLauncher(craft)) { error = OnlyLauncherText; return null; }
            // Old-Mk bin parts that would fit: the build asks first (update them, or build those parts new).
            if (q.OldBin.Count > 0) { error = "Answer the question about the parts bin's older parts first."; return null; }
            double extra = outsource ? Pricing.OutsourceCharge(q.BuildPrice, 1, S) : overtime ? Pricing.OvertimeCharge(q.BuildPrice, 1, S) : 0;
            double total = q.Total + extra;
            if (!CanAfford(total)) { error = "Not enough funds: this costs " + Pricing.Funds(total) + "."; return null; }
            var job = new BuildJob
            {
                Id = HangarShip.NewId(),
                Facility = facility,
                Overtime = overtime && !outsource,
                CreatedUT = Planetarium.GetUniversalTime(),
            };
            Apply(job, craft, name, q, keepShipId);
            Suite.Charge(total);
            Jobs.Add(job);
            Reserve(job);
            Changed();
            Log.Info("Build started: " + TitleOf(job) + " (" + facility + "), paid " + Pricing.Funds(total) + ", " + Pricing.Duration(job.SecondsLeft(S), S)
                     + (overtime ? " with overtime" : "") + (outsource ? ", outsourced" : ""));
            if (outsource || q.BuildPrice <= 0) Finish(job);
            return job;
        }

        void Apply(BuildJob job, Node craft, string name, Quote q, string keepShipId)
        {
            job.Name = name;
            job.Craft = WithHardware(Hardware.Plain(craft.Clone()), q);
            job.CraftId = HangarShip.NewId();
            job.NewPrice = q.NewPrice;
            job.RefurbishPrice = q.RefurbishPrice;
            job.FuelPrice = q.FuelPrice;
            job.Paid = q.Total;
            // The ship it keeps must be in it (its hardware); otherwise it's a new ship.
            job.ShipId = keepShipId != null && q.HangarShips.Contains(keepShipId) ? keepShipId : null;
            job.Owners.Clear();
            foreach (KeyValuePair<int, string> o in q.Owners) job.Owners[o.Key] = o.Value;
            job.BinTaken.Clear();
            foreach (KeyValuePair<string, int> t in q.BinTaken) job.BinTaken[t.Key] = t.Value;
        }

        /// <summary>
        /// Hardware keeps its own Phil's Iterative Parts Mk in a build, whatever the editor made of it: each hangar part
        /// takes its stored part's hardware modules (Hardware.CarryHardware). Changes the craft it's given (already a copy).
        /// </summary>
        Node WithHardware(Node craft, Quote q)
        {
            List<Node> parts = craft.Children("PART").ToList();
            foreach (KeyValuePair<int, string> o in q.Owners)
            {
                if (o.Key >= parts.Count || !Hardware.Parse(o.Value, out string id, out int at)) continue;
                Node stored = HangarOf(id)?.Craft.Children("PART").ElementAtOrDefault(at);
                if (stored != null) Hardware.CarryHardware(stored, parts[o.Key]);
            }
            return craft;
        }

        /// <summary>Applies an edit to a queued build: pays or refunds the difference and keeps the work done.</summary>
        public bool UpdateBuild(BuildJob job, Node craft, string name, Quote q, string keepShipId, out string error)
        {
            error = null;
            string stale = StaleQuote(q, job, keepShipId);
            if (stale != null) { error = "Something changed since this was priced: " + stale + ". Check the new price and try again."; return false; }
            if (q.OldBin.Count > 0) { error = "Answer the question about the parts bin's older parts first."; return false; }
            double workDone = job.Progress * job.BuildPrice;
            double newProgress = q.BuildPrice > 0 ? Math.Min(1, workDone / q.BuildPrice) : 1;
            double overtimeDiff = job.Overtime ? Pricing.OvertimeCharge(q.BuildPrice, 1 - newProgress, S) - Pricing.OvertimeCharge(job.BuildPrice, 1 - job.Progress, S) : 0;
            double diff = q.Total - job.Paid + overtimeDiff;
            if (!CanAfford(diff)) { error = "Not enough funds: the change costs " + Pricing.Funds(diff) + "."; return false; }
            Suite.Charge(diff);
            Release(job);
            Apply(job, craft, name, q, keepShipId);
            job.Progress = newProgress;
            Reserve(job);
            Changed();
            Log.Info("Build updated: " + TitleOf(job) + ", " + (diff >= 0 ? "paid " + Pricing.Funds(diff) : "refunded " + Pricing.Funds(-diff)) + ", " + Math.Round(newProgress * 100) + "% done");
            if (job.Progress >= 1) Finish(job);
            return true;
        }

        public void Cancel(BuildJob job)
        {
            if (!Jobs.Contains(job)) return;
            double refund = job.Paid + (job.Overtime ? Pricing.OvertimeCharge(job.BuildPrice, 1 - job.Progress, S) : 0);
            Suite.Charge(-refund);
            Release(job);
            Jobs.Remove(job);
            cancelled.Add(new KeyValuePair<string, Dictionary<int, string>>(job.CraftId, new Dictionary<int, string>(job.Owners)));
            if (cancelled.Count > 10) cancelled.RemoveAt(0);
            Changed();
            Log.Info("Build cancelled: " + TitleOf(job) + ", refunded " + Pricing.Funds(refund));
        }

        public bool SetOvertime(BuildJob job, bool on, out string error)
        {
            error = null;
            if (job.Overtime == on) return true;
            double charge = Pricing.OvertimeCharge(job.BuildPrice, 1 - job.Progress, S);
            if (on && !CanAfford(charge)) { error = "Not enough funds: overtime costs " + Pricing.Funds(charge) + "."; return false; }
            Suite.Charge(on ? charge : -charge);
            job.Overtime = on;
            return true;
        }

        public bool Outsource(BuildJob job, out string error)
        {
            error = null;
            double charge = Pricing.OutsourceCharge(job.BuildPrice, 1 - job.Progress, S);
            if (!CanAfford(charge)) { error = "Not enough funds: outsourcing costs " + Pricing.Funds(charge) + "."; return false; }
            // Overtime already paid for the rest of the build is no longer needed.
            double overtimeBack = job.Overtime ? Pricing.OvertimeCharge(job.BuildPrice, 1 - job.Progress, S) : 0;
            Suite.Charge(charge - overtimeBack);
            Finish(job);
            return true;
        }

        public void MoveUp(BuildJob job)
        {
            int i = Jobs.IndexOf(job);
            for (int k = i - 1; k >= 0; k--)
                if (Jobs[k].Facility == job.Facility)
                {
                    Jobs.RemoveAt(i);
                    Jobs.Insert(k, job);
                    return;
                }
        }

        /// <summary>Starts a refurbish of a hangar ship on its own: when done, it's at 0% wear, fuelled and ready to launch.</summary>
        public BuildJob RefurbishBuild(HangarShip h, out string error)
        {
            Quote q = QuoteFor(h);
            return StartBuild(h.Noted(), h.Name, Suite.FacilityOf(h.Craft), q, false, false, h.Id, out error);
        }

        /// <summary>Why a launcher on its own can't be built (Phil, round 4: launchers aren't ships).</summary>
        public const string OnlyLauncherText = "That's a launcher on its own, and launchers aren't ships: put a ship on it to build it.";

        /// <summary>
        /// A hangar ship rebuilt from its own blueprint ("Restore"): each part it still has in its place, as its own
        /// hardware, and every part it lost new (as the VAB would make it). Null when it has no blueprint.
        /// </summary>
        public Node RestoreCraft(HangarShip h, out int newParts)
        {
            newParts = 0;
            if (h.Ship.Blueprint == null) return null;
            Node craft = Hardware.Restore(h.Ship.Blueprint, h.Slots, h.Craft.Children("PART").ToList(), h.CraftId, out List<int> fresh);
            newParts = fresh.Count;
            Pip.MakeCurrent(craft, p => Hardware.Of(p) == null);
            return craft;
        }

        /// <summary>The build takes its hangar ships (greyed out until it's done) and its parts from the bin.</summary>
        void Reserve(BuildJob job)
        {
            foreach (string id in ShipsOf(job)) { HangarShip h = HangarOf(id); if (h != null) h.ReservedBy = job.Id; }
            foreach (KeyValuePair<string, int> t in job.BinTaken)
            {
                BinKind k = Bin.FirstOrDefault(b => b.Key == t.Key);
                if (k != null) k.Count = Math.Max(0, k.Count - t.Value);
            }
        }

        /// <summary>Gives back what a build took: its hangar ships, and its parts to the bin.</summary>
        void Release(BuildJob job)
        {
            foreach (HangarShip h in Hangar) if (h.ReservedBy == job.Id) h.ReservedBy = null;
            foreach (KeyValuePair<string, int> t in job.BinTaken)
            {
                BinKind k = Bin.FirstOrDefault(b => b.Key == t.Key);
                if (k != null) k.Count += t.Value;
            }
        }

        /// <summary>The hangar ships a build uses (their hardware).</summary>
        static IEnumerable<string> ShipsOf(BuildJob job)
        {
            var ids = new HashSet<string>();
            foreach (string token in job.Owners.Values) if (Hardware.Parse(token, out string id, out _)) ids.Add(id);
            return ids;
        }

        /// <summary>
        /// A finished build is a ready hangar ship. It's the ship it keeps (the same name, number, flights and log, however
        /// it was changed), or a new one with the design's next number. Any other hangar ship whose parts it used stops being
        /// a ship (Phil: the one you opened is the ship; the other's parts count as owned). Parts a hangar ship had that the
        /// build left out go to the parts bin (refurbished in the build's price). What was built, less the launcher it's on,
        /// is the ship's blueprint.
        /// </summary>
        void Finish(BuildJob job)
        {
            Jobs.Remove(job);
            var used = new Dictionary<string, HashSet<int>>();
            foreach (KeyValuePair<int, string> o in job.Owners)
            {
                if (!Hardware.Parse(o.Value, out string id, out int at)) continue;
                if (!used.TryGetValue(id, out HashSet<int> set)) used[id] = set = new HashSet<int>();
                set.Add(at);
            }
            List<Node> parts = job.Craft.Children("PART").ToList();
            var launcher = new HashSet<int>(Enumerable.Range(0, parts.Count).Where(i => BuildsLink.IsLauncherPart(parts[i])));
            Ship main = ShipOf(job.ShipId);
            if (main == null)
            {
                // A new ship belongs to its design for good: Phil's Builds counts it there, whatever it's rebuilt into.
                main = NewShip(job.Name);
                List<Node> own = parts.Where((p, i) => !launcher.Contains(i)).ToList();
                main.DesignId = BuildsLink.DesignIdFor(own.Select(p => Hardware.Module(p)?.Get("design")), Hardware.Module(own.FirstOrDefault())?.Get("design"));
            }

            HangarShip ready = HangarOf(main.Id);
            foreach (HangarShip h in Hangar.Where(x => x.ReservedBy == job.Id).ToList())
            {
                int i = -1;
                used.TryGetValue(h.Id, out HashSet<int> mine);
                foreach (Node p in h.Craft.Children("PART")) { i++; if (mine == null || !mine.Contains(i)) AddToBin(p); }
                if (h != ready) Hangar.Remove(h);
            }
            if (ready == null) { ready = new HangarShip { Ship = main }; Hangar.Add(ready); }
            ready.Built(Hardware.Plain(job.Craft.Clone()), launcher);
            // The same id as the build's craft, so the ship left in the VAB after pressing Build is this ship.
            ready.CraftId = job.CraftId;
            ready.UT = Planetarium.GetUniversalTime();
            ready.CameBack.Clear();
            ready.Missing = 0;
            ready.Ready = true;
            ready.Fuel = 1;
            // Built or refurbished: the whole craft leaves at 0% wear (Phil: Restore refurbishes the surviving parts too).
            ready.Wear = 0;
            ready.ReservedBy = null;
            Log.Info("Build finished: " + ready.DisplayName + " is ready in the hangar" + (launcher.Count > 0 ? ", on " + Suite.Parts(launcher.Count) + " of launcher" : ""));
            Suite.Message(ready.DisplayName + " is ready in the hangar");
            if (TimeWarp.fetch != null && TimeWarp.CurrentRateIndex > 0) TimeWarp.SetRate(0, true, false);
            Changed();
        }

        /// <summary>The ship a build keeps (a refit, refurbish or restore), or null for a new ship.</summary>
        public Ship KeptShipOf(BuildJob job) => ShipOf(job.ShipId);

        /// <summary>What a build is called on screen and in the log: the ship it keeps ("Peleus III #2"), or the design's name.</summary>
        public string TitleOf(BuildJob job) => KeptShipOf(job)?.DisplayName ?? job.Name;

        // ---- Time passing ----

        /// <summary>The builds being worked on now: the first ones in each building, one per build line.</summary>
        public List<BuildJob> ActiveJobs()
        {
            var active = new List<BuildJob>();
            foreach (string f in new[] { "VAB", "SPH" }) active.AddRange(Jobs.Where(j => j.Facility == f).Take(Suite.Lines(f)));
            return active;
        }

        void Update()
        {
            if (refundDue > 0 && Suite.Career && !Krash.SimActive)
            {
                Suite.Charge(-refundDue);
                Log.Info("Refunded " + Pricing.Funds(refundDue) + " for builds queued under the older save format");
                refundDue = 0;
            }
            if (!HighLogic.LoadedSceneHasPlanetarium || Planetarium.fetch == null) return;
            double now = Planetarium.GetUniversalTime();
            if (lastUT < 0 || now < lastUT) { lastUT = now; return; }
            if (now > lastUT && !Krash.SimActive) Advance(now - lastUT);
            lastUT = now;
        }

        void Advance(double dt)
        {
            for (int guard = 0; dt > 1e-6 && guard < 200; guard++)
            {
                List<BuildJob> active = ActiveJobs();
                if (active.Count == 0) return;
                double step = Math.Min(dt, active.Min(j => j.SecondsLeft(S)));
                foreach (BuildJob j in active)
                {
                    double total = j.Seconds(S);
                    j.Progress = total <= 0 ? 1 : Math.Min(1, j.Progress + step * j.Speed(S) / total);
                }
                dt -= step;
                foreach (BuildJob j in active.Where(x => x.Progress >= 1 - 1e-9).ToList()) Finish(j);
            }
        }

        /// <summary>Warps time to when the next build finishes.</summary>
        public void WarpToNextFinish()
        {
            List<BuildJob> active = ActiveJobs();
            if (active.Count == 0 || TimeWarp.fetch == null) return;
            double left = active.Min(j => j.SecondsLeft(S));
            TimeWarp.fetch.WarpTo(Planetarium.GetUniversalTime() + left + 1);
        }

        // ---- Launching ready ships ----

        /// <summary>
        /// Launches a hangar ship straight from the hangar, with the crew given (picked in the suite's crew step), or
        /// the crew KSP's own launch-pad dialog would pick. Refurbishing is optional (Phil): a ship that isn't fuelled
        /// is refuelled first, which is all it pays. A worn-out one must be refurbished first.
        /// </summary>
        public bool LaunchReady(HangarShip h, out string error, VesselCrewManifest crew = null)
        {
            if (!CanLaunch(h, out error, out string site)) return false;
            if (!Refuel(h, out error)) return false;
            string path = SuiteFiles.WriteCraft(h);
            if (crew == null) crew = DefaultCrew(path);
            // Anyone picked who has since gone elsewhere (another launch, KIA) stays home: KSP would refuse them.
            foreach (PartCrewManifest pcm in crew.GetCrewableParts())
            {
                ProtoCrewMember[] seats = pcm.GetPartCrew();
                for (int i = 0; i < seats.Length; i++)
                    if (seats[i] != null && seats[i].rosterStatus != ProtoCrewMember.RosterStatus.Available) pcm.RemoveCrewFromSeat(i);
            }
            List<ProtoCrewMember> going = crew.GetAllCrew(false);
            Log.Info("Launching " + h.DisplayName + " from the hangar with " + (going.Count == 0 ? "no crew" : string.Join(", ", going.Select(c => c.name).ToArray())));
            Launching(h);
            FlightDriver.StartWithNewLaunch(path, HighLogic.CurrentGame.flagURL, site, crew);
            return true;
        }

        /// <summary>The crew KSP's own launch-pad dialog would pick for a hangar ship: its saved crew, empty seats filled.</summary>
        public VesselCrewManifest DefaultCrew(HangarShip h) => DefaultCrew(SuiteFiles.WriteCraft(h));

        static VesselCrewManifest DefaultCrew(string path)
        {
            ConfigNode craft = ConfigNode.Load(path);
            VesselCrewManifest m = VesselCrewManifest.FromConfigNode(craft);
            return HighLogic.CurrentGame.CrewRoster.DefaultCrewForVessel(craft, m, true, false);
        }

        /// <summary>
        /// What must hold before a hangar ship goes to the pad (nothing is charged): not in a build, not worn out, and
        /// the launch site clear. Launch clamps left from the last launch are cleared away on the way.
        /// </summary>
        public bool CanLaunch(HangarShip h, out string error, out string site)
        {
            error = null;
            site = Suite.FacilityOf(h.Craft) == "SPH" ? "Runway" : "LaunchPad";
            if (h.ReservedBy != null) { error = h.DisplayName + " is in a build in the queue."; return false; }
            if (Core.Wear.WornOut(h.Wear)) { error = h.DisplayName + " is worn out (100% wear): refurbish it before it flies again."; return false; }
            List<ProtoVessel> inTheWay = ShipConstruction.FindVesselsLandedAt(HighLogic.CurrentGame.flightState, site);
            // Launch clamps left standing from the last launch are cleared away, as KSP's own launch would offer:
            // recovered the stock way, so they go to the parts bin for the next build.
            foreach (ProtoVessel pv in inTheWay.Where(OnlyLaunchClamps).ToList())
            {
                Log.Info("Clearing the launch clamps left on the " + (site == "Runway" ? "runway" : "launch pad") + " (" + pv.protoPartSnapshots.Count + " parts)");
                ShipConstruction.RecoverVesselFromFlight(pv, HighLogic.CurrentGame.flightState, true);
                inTheWay.Remove(pv);
            }
            if (inTheWay.Count > 0)
            {
                error = KSP.Localization.Localizer.Format(inTheWay[0].vesselName) + " is on the " + (site == "Runway" ? "runway" : "launch pad") + ". Recover it first (from the Tracking Station, or fly it off).";
                return false;
            }
            return true;
        }

        /// <summary>What refuelling a hangar ship to its launch levels costs (nothing once it's fuelled).</summary>
        public double RefuelPrice(HangarShip h) => h.Ready ? 0 : QuoteFor(h).FuelPrice;

        /// <summary>Refuels a hangar ship to its launch levels without refurbishing it: it keeps its wear, and launches free.</summary>
        public bool Refuel(HangarShip h, out string error)
        {
            error = null;
            if (h.Ready) return true;
            if (h.ReservedBy != null) { error = h.DisplayName + " is in a build in the queue."; return false; }
            if (Core.Wear.WornOut(h.Wear)) { error = h.DisplayName + " is worn out (100% wear): refurbish it before it flies again."; return false; }
            double fuel = RefuelPrice(h);
            if (!CanAfford(fuel)) { error = "Not enough funds: refuelling " + h.DisplayName + " costs " + Pricing.Funds(fuel) + "."; return false; }
            Suite.Charge(fuel);
            h.Ready = true;
            h.CameBack.Clear();
            h.Fuel = 1;
            Changed();
            Log.Info("Refuelled " + h.DisplayName + " for " + Pricing.Funds(fuel) + ", not refurbished (wear " + Core.Wear.Percent(h.Wear) + ")");
            return true;
        }

        static bool OnlyLaunchClamps(ProtoVessel pv) =>
            pv.protoPartSnapshots.Count > 0 && pv.protoPartSnapshots.All(ps => ps.partInfo?.partPrefab != null && ps.partInfo.partPrefab.FindModuleImplementing<LaunchClamp>() != null);
    }
}
