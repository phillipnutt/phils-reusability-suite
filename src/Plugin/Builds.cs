using System;
using System.Collections.Generic;
using System.Linq;
using PhilsReusabilitySuite.Core;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    static class Builds
    {
        /// <summary>A copy of a part node as a loose part: it no longer belongs to any hangar ship.</summary>
        public static Node Loose(Node part)
        {
            Node c = part.Clone();
            Node m = c.Children("MODULE").FirstOrDefault(x => x.Get("name") == Core.Craft.ModuleName);
            if (m != null) m.Set("hull", "");
            return c;
        }
    }

    /// <summary>The build queue: jobs on build lines, paid once at Build, finished ships into the hangar.</summary>
    public partial class PRSScenario
    {
        public readonly List<BuildJob> Jobs = new List<BuildJob>();
        double lastUT = -1;

        static Core.Settings S => Suite.Settings;

        // ---- Who owns a part ----

        /// <summary>The hangar ship or bin part a serial belongs to, or null for a new part. Parts already
        /// taken by another build count as new (they can't be used twice).</summary>
        public Owner OwnerOf(string serial, BuildJob editing)
        {
            foreach (HangarShip h in Hangar)
                if (h.Placed.Contains(serial))
                {
                    if (h.ReservedBy != null && (editing == null || h.ReservedBy != editing.Id)) return null;
                    return new Owner { Kind = OwnerKind.Hangar, ShipId = h.Id, Label = h.DisplayName, Ready = h.Ready };
                }
            foreach (BinPart b in Bin)
                if (b.Serial == serial)
                {
                    if (b.ReservedBy != null && (editing == null || b.ReservedBy != editing.Id)) return null;
                    return new Owner { Kind = OwnerKind.Bin };
                }
            return null;
        }

        IDictionary<string, double> CameBackOf(string serial)
        {
            foreach (HangarShip h in Hangar)
                if (h.CameBack.TryGetValue(serial, out Dictionary<string, double> d)) return d;
            foreach (BinPart b in Bin)
                if (b.Serial == serial) return b.Left;
            return null;
        }

        /// <summary>In a build's skip list: the player answered "No" to updating old-Mk bin parts, so none are offered.</summary>
        public const string SkipOldParts = "*old-mk";

        /// <param name="skipBin">Bin parts this build mustn't use (by serial), or SkipOldParts: no old-Mk part is offered.</param>
        public Quote QuoteFor(Node craft, string name, BuildJob editing, ICollection<string> skipBin = null)
        {
            return Quotes.Make(craft, name, s => OwnerOf(s, editing), KspCosts.Instance, CameBackOf, S, BinMatcher(editing, craft, skipBin));
        }

        /// <summary>
        /// Matches a new part to a loose part in the bin, as ScrapYard did: the same part at the same price (so
        /// the same variant and options), not taken by another build, each bin part used once. A Phil's Iterative
        /// Parts part whose program has moved on since it was built is never used as it is: it comes back marked
        /// Old, so the build can ask whether to update it (Phil: old-Mk parts never fly unless updated). A current
        /// one is taken first.
        /// </summary>
        Func<Node, Owner> BinMatcher(BuildJob editing, Node craft, ICollection<string> skipBin)
        {
            // A bin part already in the ship (its own serial is in the craft) can't also stand in for a new part.
            var used = new HashSet<string>(craft.Children("PART").Select(Core.Craft.SerialOf).Where(s => !string.IsNullOrEmpty(s)));
            var dryCache = new Dictionary<string, double>();
            var behindCache = new Dictionary<string, Owner>();
            bool noOld = skipBin != null && skipBin.Contains(SkipOldParts);
            return part =>
            {
                string r = part.Get("part") ?? "";
                string name = r.Contains("_") ? r.Substring(0, r.LastIndexOf('_')) : r;
                double dry = -1;
                Owner old = null;
                foreach (BinPart b in Bin)
                {
                    if (b.Part != name || b.PartNode == null || used.Contains(b.Serial)) continue;
                    if (b.ReservedBy != null && (editing == null || b.ReservedBy != editing.Id)) continue;
                    if (skipBin != null && skipBin.Contains(b.Serial)) continue;
                    if (dry < 0) dry = KspCosts.Instance.Cost(part).Dry;
                    if (!dryCache.TryGetValue(b.Serial, out double bd)) dryCache[b.Serial] = bd = KspCosts.Instance.Cost(b.PartNode).Dry;
                    if (Math.Abs(bd - dry) > 1) continue;
                    if (!behindCache.TryGetValue(b.Serial, out Owner behind))
                    {
                        int marks = Pip.Behind(b.PartNode, out string now, out string current);
                        behind = marks <= 0 ? null : new Owner
                        {
                            Kind = OwnerKind.Bin, Old = true, BinSerial = b.Serial, Flights = b.Flights, Wear = b.Wear, BinPart = b.PartNode,
                            Marks = marks, Now = now, Current = current, Label = "From the parts bin",
                        };
                        behindCache[b.Serial] = behind;
                    }
                    // Answered "No, build them new": old-Mk parts aren't offered again for this build.
                    if (behind != null) { if (old == null && !noOld) old = behind; continue; }
                    used.Add(b.Serial);
                    return new Owner { Kind = OwnerKind.Bin, BinSerial = b.Serial, Flights = b.Flights, Wear = b.Wear, BinPart = b.PartNode, Label = "From the parts bin" };
                }
                if (old != null) used.Add(old.BinSerial);
                return old;
            };
        }

        // ---- Updating PIP parts to their program's current Mk ----

        /// <summary>A part's update: how many Marks, and what it costs.</summary>
        public double PipUpdatePrice(Node part, out int marks)
        {
            marks = Pip.Behind(part);
            return marks > 0 ? Pricing.PipUpdate(KspCosts.Instance.Cost(part).Dry, marks, S) : 0;
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

        /// <summary>
        /// "Update PIP parts": brings each part that's behind to its program's current Mk (PIP's own step, the one the
        /// VAB uses for a new part), paid now. Wear, flights and fuel stay as they are.
        /// </summary>
        bool UpdatePip(List<Node> parts, string what, out string error)
        {
            error = null;
            double price = PipUpdatePrice(parts, out int behind);
            if (behind == 0) { error = "Its Phil's Iterative Parts parts are all at their program's current Mk."; return false; }
            if (Suite.Career && price > Suite.Funds) { error = "Not enough funds: updating costs " + Pricing.Funds(price) + "."; return false; }
            int done = 0;
            foreach (Node p in parts) if (Pip.Behind(p) > 0 && Pip.MakeCurrent(p)) done++;
            if (done == 0) { error = "Phil's Iterative Parts couldn't update them."; return false; }
            // Charged for what was updated (all of them, unless PIP refused one).
            Suite.Charge(done == behind ? price : price * done / behind);
            Changed();
            Log.Info("Updated " + Suite.Parts(done) + " of " + what + " to their programs' current Mk, for " + Pricing.Funds(done == behind ? price : price * done / behind));
            return true;
        }

        public bool UpdatePip(IEnumerable<BinPart> parts, out string error)
        {
            List<Node> nodes = parts.Where(b => b.ReservedBy == null && b.PartNode != null).Select(b => b.PartNode).ToList();
            return UpdatePip(nodes, "the parts bin", out error);
        }

        public bool UpdatePip(HangarShip h, out string error)
        {
            if (h.ReservedBy != null) { error = h.DisplayName + " is in a build: update it when the build is done."; return false; }
            return UpdatePip(h.Craft.Children("PART").ToList(), h.DisplayName, out error);
        }

        /// <summary>The bin parts behind old-Mk offers in a quote, as they are now.</summary>
        public List<BinPart> BinPartsOf(IEnumerable<Owner> old)
        {
            var serials = new HashSet<string>(old.Select(o => o.BinSerial));
            return Bin.Where(b => serials.Contains(b.Serial)).ToList();
        }

        // ---- Taking apart and selling ----

        /// <summary>Takes a stored ship apart: every part goes to the parts bin.</summary>
        public bool TakeApart(HangarShip h)
        {
            if (h.ReservedBy != null) return false;
            Craft c = Core.Craft.Read(h.Craft);
            foreach (CraftPart cp in c.Parts) if (!string.IsNullOrEmpty(cp.Serial)) Bin.Add(BinPartFrom(cp, h));
            Hangar.Remove(h);
            Changed();
            Log.Info("Took " + h.DisplayName + " apart: " + c.Parts.Count + " parts to the parts bin");
            return true;
        }

        /// <summary>What a hangar ship sells for as scrap: half the price of its parts and the fuel it holds.</summary>
        public double ScrapValue(HangarShip h)
        {
            double total = 0;
            foreach (Node p in h.Craft.Children("PART"))
            {
                PartCost c = KspCosts.Instance.Cost(p);
                total += c.Dry;
                string serial = Core.Craft.SerialOf(p);
                IDictionary<string, double> left = !h.Ready && serial != null && h.CameBack.TryGetValue(serial, out Dictionary<string, double> d) ? d : null;
                foreach (KeyValuePair<string, double> res in c.Amounts)
                    total += (h.Ready ? res.Value : left != null && left.TryGetValue(res.Key, out double l) ? l : 0) * KspCosts.Instance.UnitCost(res.Key);
            }
            return total * S.ScrapValue;
        }

        public double ScrapValue(BinPart b) => b.PartNode != null ? KspCosts.Instance.Cost(b.PartNode).Dry * S.ScrapValue : 0;

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

        public double Sell(IEnumerable<BinPart> parts)
        {
            double v = 0;
            foreach (BinPart b in parts.Where(x => x.ReservedBy == null).ToList())
            {
                v += ScrapValue(b);
                Bin.Remove(b);
            }
            Suite.Charge(-v);
            if (v > 0) Log.Info("Sold parts from the bin for scrap: " + Pricing.Funds(v));
            return v;
        }

        /// <summary>The queued build these serials belong to (the editor ship being edited), if any.</summary>
        public BuildJob JobOf(ICollection<string> serials)
        {
            BuildJob best = null;
            int bestCount = 0;
            foreach (BuildJob j in Jobs)
            {
                var js = new HashSet<string>(j.Serials());
                int n = serials.Count(js.Contains);
                if (n > bestCount) { best = j; bestCount = n; }
            }
            return best;
        }

        /// <summary>A ready hangar ship whose parts are exactly these (launching it costs nothing).</summary>
        public HangarShip ReadyShipOf(ICollection<string> serials)
        {
            if (serials.Count == 0) return null;
            return Hangar.FirstOrDefault(h => h.Ready && h.ReservedBy == null && h.Placed.Count == serials.Count && serials.All(h.Placed.Contains));
        }

        static bool CanAfford(double amount) => amount <= 0 || !Suite.Career || Suite.Funds >= amount - 0.5;

        // ---- Starting, changing and ending builds ----

        /// <summary>
        /// Why a quote no longer holds, or null if it does: a bin part it uses was sold or went into another build,
        /// or a hangar ship it uses was taken apart, sold or put in another build since it was priced.
        /// </summary>
        public string StaleQuote(Quote q, BuildJob editing, string fromShipId)
        {
            string mine = editing?.Id;
            if (editing != null && !Jobs.Contains(editing)) return "that build has been finished or cancelled";
            if (q.BinSerials.Count != q.BinSerials.Distinct().Count()) return "a part from the parts bin was counted twice";
            foreach (string s in q.BinSerials)
            {
                BinPart b = Bin.FirstOrDefault(x => x.Serial == s);
                if (b == null) return "a part from the parts bin is gone";
                if (b.ReservedBy != null && b.ReservedBy != mine) return "a part from the parts bin is in another build now";
            }
            foreach (string id in q.HangarShips.Concat(fromShipId != null ? new[] { fromShipId } : new string[0]))
            {
                HangarShip h = Hangar.FirstOrDefault(x => x.Id == id);
                if (h == null) return "a hangar ship it uses is gone";
                if (h.ReservedBy != null && h.ReservedBy != mine) return h.DisplayName + " is in another build now";
            }
            return null;
        }

        public BuildJob StartBuild(Node craft, string name, string facility, Quote q, bool overtime, bool outsource, string fromShipId, out string error)
        {
            error = null;
            string stale = StaleQuote(q, null, fromShipId);
            if (stale != null) { error = "Something changed since this was priced: " + stale + ". Check the new price and try again."; return null; }
            // Old-Mk bin parts that would fit: the build asks first (update them, or build those parts new).
            if (q.OldBin.Count > 0) { error = "Answer the question about the parts bin's older parts first."; return null; }
            double extra = outsource ? Pricing.OutsourceCharge(q.BuildPrice, 1, S) : overtime ? Pricing.OvertimeCharge(q.BuildPrice, 1, S) : 0;
            double total = q.Total + extra;
            if (!CanAfford(total)) { error = "Not enough funds: this costs " + Pricing.Funds(total) + "."; return null; }
            var job = new BuildJob
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Facility = facility,
                Craft = WithHardware(q.WithBinParts(craft)),
                NewPrice = q.NewPrice,
                RefurbishPrice = q.RefurbishPrice,
                FuelPrice = q.FuelPrice,
                Paid = q.Total,
                Overtime = overtime && !outsource,
                CreatedUT = Planetarium.GetUniversalTime(),
                FromShipId = fromShipId,
            };
            Suite.Charge(total);
            Jobs.Add(job);
            Reserve(job, q);
            Changed();
            Log.Info("Build started: " + TitleOf(job) + " (" + facility + "), paid " + Pricing.Funds(total) + ", " + Pricing.Duration(job.SecondsLeft(S), S)
                     + (overtime ? " with overtime" : "") + (outsource ? ", outsourced" : ""));
            if (outsource || q.BuildPrice <= 0) Finish(job);
            return job;
        }

        /// <summary>
        /// Hardware keeps its own Phil's Iterative Parts Mk in a build, whatever the editor made of it ("Use it" turns new
        /// editor parts into a hangar copy's hardware): each part that is a hangar ship's or a bin part's takes that stored
        /// part's hardware modules (Completion.CarryHardware). Changes the craft it's given (already a copy).
        /// </summary>
        Node WithHardware(Node craft)
        {
            var stored = new Dictionary<string, Node>();
            foreach (HangarShip h in Hangar)
                foreach (Node p in h.Craft.Children("PART"))
                {
                    string s = Core.Craft.SerialOf(p);
                    if (!string.IsNullOrEmpty(s) && h.Placed.Contains(s)) stored[s] = p;
                }
            foreach (BinPart b in Bin) if (b.PartNode != null && !string.IsNullOrEmpty(b.Serial)) stored[b.Serial] = b.PartNode;
            foreach (Node part in craft.Children("PART"))
            {
                string s = Core.Craft.SerialOf(part);
                if (!string.IsNullOrEmpty(s) && stored.TryGetValue(s, out Node from) && !ReferenceEquals(from, part)) Completion.CarryHardware(from, part);
            }
            return craft;
        }

        /// <summary>Applies an edit to a queued build: pays or refunds the difference and keeps the work done.</summary>
        public bool UpdateBuild(BuildJob job, Node craft, string name, Quote q, out string error)
        {
            error = null;
            string stale = StaleQuote(q, job, job.FromShipId);
            if (stale != null) { error = "Something changed since this was priced: " + stale + ". Check the new price and try again."; return false; }
            double workDone = job.Progress * job.BuildPrice;
            double newProgress = q.BuildPrice > 0 ? Math.Min(1, workDone / q.BuildPrice) : 1;
            double overtimeDiff = job.Overtime ? Pricing.OvertimeCharge(q.BuildPrice, 1 - newProgress, S) - Pricing.OvertimeCharge(job.BuildPrice, 1 - job.Progress, S) : 0;
            double diff = q.Total - job.Paid + overtimeDiff;
            if (!CanAfford(diff)) { error = "Not enough funds: the change costs " + Pricing.Funds(diff) + "."; return false; }
            Suite.Charge(diff);
            Release(job);
            job.Craft = WithHardware(q.WithBinParts(craft));
            job.Name = name;
            job.NewPrice = q.NewPrice;
            job.RefurbishPrice = q.RefurbishPrice;
            job.FuelPrice = q.FuelPrice;
            job.Paid = q.Total;
            job.Progress = newProgress;
            Reserve(job, q);
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
            Quote q = QuoteFor(h.Craft, h.DisplayName, null);
            return StartBuild(h.Craft, h.Name, Suite.FacilityOf(h.Craft), q, false, false, h.Id, out error);
        }

        void Reserve(BuildJob job, Quote q)
        {
            foreach (HangarShip h in Hangar) if (q.HangarShips.Contains(h.Id)) h.ReservedBy = job.Id;
            foreach (BinPart b in Bin) if (q.BinSerials.Contains(b.Serial)) b.ReservedBy = job.Id;
            if (job.FromShipId != null) foreach (HangarShip h in Hangar) if (h.Id == job.FromShipId) h.ReservedBy = job.Id;
        }

        void Release(BuildJob job)
        {
            foreach (HangarShip h in Hangar) if (h.ReservedBy == job.Id) h.ReservedBy = null;
            foreach (BinPart b in Bin) if (b.ReservedBy == job.Id) b.ReservedBy = null;
        }

        /// <summary>A finished build becomes a ready hangar ship; hangar ships it used are absorbed into it.</summary>
        void Finish(BuildJob job)
        {
            Jobs.Remove(job);
            var serials = new HashSet<string>(job.Serials());
            // A build made from hangar ships (Argo I #1 stacked on Rockall 002 #1) stays the ship at its top: same
            // name, number and flight log, with the others under it. Phil, 2026-11-08: keep the top piece's identity.
            string keepId = KeptShipOf(job)?.Id;
            foreach (HangarShip h in Hangar.Where(x => x.ReservedBy == job.Id && x.Id != keepId).ToList())
            {
                // Parts taken off a stored ship in the VAB go to the parts bin.
                Craft c = Core.Craft.Read(h.Craft);
                foreach (string s in h.Placed.Where(x => !serials.Contains(x)))
                    if (c.BySerial.TryGetValue(s, out CraftPart cp)) Bin.Add(BinPartFrom(cp, h));
                Hangar.Remove(h);
            }
            Bin.RemoveAll(b => b.ReservedBy == job.Id);

            HangarShip ready = keepId != null ? Hangar.FirstOrDefault(x => x.Id == keepId) : null;
            if (ready == null)
            {
                lastNumber.TryGetValue(job.Name, out int last);
                ready = new HangarShip { Id = Guid.NewGuid().ToString("N"), Name = job.Name, Number = last + 1 };
                lastNumber[job.Name] = ready.Number;
                Hangar.Add(ready);
            }
            else
            {
                // A ship rebuilt as itself (restored to its design, which can leave out a part added by hand, or
                // built on as the top of a stack): anything not in the new build goes to the parts bin.
                Craft old = Core.Craft.Read(ready.Craft);
                foreach (string s in ready.Placed.Where(x => !serials.Contains(x)))
                    if (old.BySerial.TryGetValue(s, out CraftPart cp)) Bin.Add(BinPartFrom(cp, ready));
            }
            ready.UT = Planetarium.GetUniversalTime();
            ready.Craft = job.Craft.Clone();
            ready.Placed.Clear();
            ready.Placed.AddRange(serials);
            ready.Missing.Clear();
            ready.CameBack.Clear();
            ready.Ready = true;
            ready.Fuel = 1;
            // Built or refurbished: the whole craft leaves at 0% wear (Phil: Restore refurbishes the surviving parts too).
            ready.SetWear(0);
            ready.ReservedBy = null;
            // New parts now belong to this ship; reused ones keep their own ship (a flown first stage stays
            // "Oak 005 #4" under its new payload, and comes home as itself).
            ready.StampHull(false);
            ready.Flights = Core.Craft.Read(ready.Craft).Parts.Select(p => FlightsIn(p.Node)).DefaultIfEmpty(0).Max();
            Log.Info("Build finished: " + ready.DisplayName + " is ready in the hangar");
            Suite.Message(ready.DisplayName + " is ready in the hangar");
            if (TimeWarp.fetch != null && TimeWarp.CurrentRateIndex > 0) TimeWarp.SetRate(0, true, false);
            Changed();
        }

        /// <summary>
        /// The hangar ship whose identity a build keeps: the one it refurbishes or restores, or the one it's built on
        /// top of (taken into the build, holding its root part, under the same name). Null for a new ship.
        /// </summary>
        public HangarShip KeptShipOf(BuildJob job) => job.FromShipId != null ? Hangar.FirstOrDefault(h => h.Id == job.FromShipId) : TopShipOf(job);

        /// <summary>What a build is called on screen and in the log: the hangar ship it keeps ("Gallant I #1"), or its name.</summary>
        public string TitleOf(BuildJob job) => KeptShipOf(job)?.DisplayName ?? job.Name;

        HangarShip TopShipOf(BuildJob job)
        {
            // KSP writes a craft's root part first.
            Node first = job.Craft?.Children("PART").FirstOrDefault();
            string root = first != null ? Core.Craft.SerialOf(first) : null;
            if (string.IsNullOrEmpty(root)) return null;
            return Hangar.FirstOrDefault(h => h.ReservedBy == job.Id && h.Placed.Contains(root) && h.Name == job.Name);
        }

        static int FlightsIn(Node part)
        {
            Node m = part.Children("MODULE").FirstOrDefault(x => x.Get("name") == Core.Craft.ModuleName);
            return int.TryParse(m?.Get("flights"), out int f) ? f : 0;
        }

        /// <summary>A hangar ship's part as a loose bin part, holding what it held in that ship (a ready ship's are full).</summary>
        static BinPart BinPartFrom(CraftPart cp, HangarShip from)
        {
            Dictionary<string, double> left = from.Ready ? BinPart.Held(cp.Node)
                : from.CameBack.TryGetValue(cp.Serial, out Dictionary<string, double> d) ? new Dictionary<string, double>(d)
                : new Dictionary<string, double>();
            return new BinPart { Serial = cp.Serial, Part = cp.PartName, Flights = FlightsIn(cp.Node), PartNode = Builds.Loose(cp.Node), Left = left };
        }

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
        /// Launches a hangar ship straight from the hangar, with its default crew. Refurbishing is optional (Phil): a
        /// ship that isn't fuelled is refuelled first, which is all it pays. A worn-out one must be refurbished first.
        /// </summary>
        public bool LaunchReady(HangarShip h, out string error)
        {
            error = null;
            if (h.ReservedBy != null) { error = h.DisplayName + " is in a build in the queue."; return false; }
            if (Core.Wear.WornOut(h.Wear)) { error = h.DisplayName + " is worn out (100% wear): refurbish it before it flies again."; return false; }
            string site = Suite.FacilityOf(h.Craft) == "SPH" ? "Runway" : "LaunchPad";
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
            if (!Refuel(h, out error)) return false;
            string path = SuiteFiles.WriteCraft(h);
            ConfigNode craft = ConfigNode.Load(path);
            VesselCrewManifest m = VesselCrewManifest.FromConfigNode(craft);
            m = HighLogic.CurrentGame.CrewRoster.DefaultCrewForVessel(craft, m, true, false);
            Log.Info("Launching " + h.DisplayName + " from the hangar");
            FlightDriver.StartWithNewLaunch(path, HighLogic.CurrentGame.flagURL, site, m);
            return true;
        }

        /// <summary>What refuelling a hangar ship to its launch levels costs (nothing once it's fuelled).</summary>
        public double RefuelPrice(HangarShip h) => h.Ready ? 0 : QuoteFor(h.Craft, h.DisplayName, null).FuelPrice;

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

        /// <summary>A hangar ship, ready or not, whose parts are exactly these and that no build has taken.</summary>
        public HangarShip HangarShipOf(ICollection<string> serials)
        {
            if (serials.Count == 0) return null;
            return Hangar.FirstOrDefault(h => h.ReservedBy == null && h.Placed.Count == serials.Count && serials.All(h.Placed.Contains));
        }

        static bool OnlyLaunchClamps(ProtoVessel pv) =>
            pv.protoPartSnapshots.Count > 0 && pv.protoPartSnapshots.All(ps => ps.partInfo?.partPrefab != null && ps.partInfo.partPrefab.FindModuleImplementing<LaunchClamp>() != null);

        /// <summary>At rollout: a ready hangar ship launches free (KSP charges its price at rollout, so refund it).</summary>
        /// <summary>True if the ship rolling out is a ready hangar ship (launched free).</summary>
        bool ReadyShipRollout(ShipConstruct ship)
        {
            if (Krash.SimActive) return false;
            var serials = ship.parts.Select(PRSPart.SerialOf).Where(s => s != null).ToList();
            HangarShip h = ReadyShipOf(serials);
            if (h == null)
            {
                if (serials.Count > 0) Log.Warn(KSP.Localization.Localizer.Format(ship.shipName) + " launched without being built: KSP charged its normal price");
                return false;
            }
            float cost = ship.GetShipCosts(out float dry, out float fuel, ShipConstruction.ShipManifest);
            if (Suite.Career) Funding.Instance.AddFunds(cost, TransactionReasons.VesselRollout);
            Hangar.Remove(h);
            Changed();
            Log.Info("Launched " + h.DisplayName + " from the hangar: already paid for, " + Pricing.Funds(cost) + " handed back");
            return true;
        }

        /// <summary>Serials of hardware the suite keeps: hangar ships', bin parts' and builds'.</summary>
        public HashSet<string> StoredSerials()
        {
            var stored = new HashSet<string>(Hangar.SelectMany(x => x.Placed));
            stored.UnionWith(Bin.Select(b => b.Serial));
            stored.UnionWith(Jobs.SelectMany(j => j.Serials()));
            return stored;
        }

        /// <summary>Serials that are some particular piece of hardware: stored, or on a ship in flight.</summary>
        public HashSet<string> TakenSerials()
        {
            HashSet<string> taken = StoredSerials();
            if (HighLogic.CurrentGame?.flightState != null)
                foreach (ProtoVessel pv in HighLogic.CurrentGame.flightState.protoVessels)
                    foreach (ProtoPartSnapshot ps in pv.protoPartSnapshots)
                    {
                        string s = PRSPart.ValueIn(ps, "serial");
                        if (!string.IsNullOrEmpty(s)) taken.Add(s);
                    }
            return taken;
        }

        /// <summary>
        /// A ship launched without being built (KSP's own launch-pad dialog launches a saved craft file straight
        /// into flight) can carry serials saved in the file: those of parts that are still hangar ships, bin parts,
        /// builds or flying ships. Those parts are new, so they get new serials.
        /// </summary>
        void RenewTakenSerials(List<Part> parts)
        {
            HashSet<string> taken = TakenSerials();
            int renewed = 0;
            var seen = new HashSet<string>();
            foreach (PRSPart m in parts.Select(PRSPart.Of).Where(x => x != null))
            {
                if (string.IsNullOrEmpty(m.serial) || (!taken.Contains(m.serial) && seen.Add(m.serial))) continue;
                m.MakeNew();
                seen.Add(m.serial);
                renewed++;
            }
            if (renewed > 0) Log.Info("Gave " + renewed + " parts of a ship launched without being built new serial numbers (their saved ones belong to other hardware)");
        }
    }
}
