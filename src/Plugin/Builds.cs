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
                    return new Owner { Kind = OwnerKind.Hangar, ShipId = h.Id, Label = h.DisplayName, RefitDone = h.Ready };
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
            return null;
        }

        public Quote QuoteFor(Node craft, string name, BuildJob editing)
        {
            return Quotes.Make(craft, name, s => OwnerOf(s, editing), KspCosts.Instance, CameBackOf, S, BinMatcher(editing));
        }

        /// <summary>
        /// Matches a new part to a loose part in the bin, as ScrapYard did: the same part at the same price (so
        /// the same variant and options), not taken by another build, each bin part used once.
        /// </summary>
        Func<Node, Owner> BinMatcher(BuildJob editing)
        {
            var used = new HashSet<string>();
            var dryCache = new Dictionary<string, double>();
            return part =>
            {
                string r = part.Get("part") ?? "";
                string name = r.Contains("_") ? r.Substring(0, r.LastIndexOf('_')) : r;
                double dry = -1;
                foreach (BinPart b in Bin)
                {
                    if (b.Part != name || b.PartNode == null || used.Contains(b.Serial)) continue;
                    if (b.ReservedBy != null && (editing == null || b.ReservedBy != editing.Id)) continue;
                    if (dry < 0) dry = KspCosts.Instance.Cost(part).Dry;
                    if (!dryCache.TryGetValue(b.Serial, out double bd)) dryCache[b.Serial] = bd = KspCosts.Instance.Cost(b.PartNode).Dry;
                    if (Math.Abs(bd - dry) > 1) continue;
                    used.Add(b.Serial);
                    return new Owner { Kind = OwnerKind.Bin, BinSerial = b.Serial, Flights = b.Flights, Label = "From the parts bin" };
                }
                return null;
            };
        }

        // ---- Taking apart and selling ----

        /// <summary>Takes a stored ship apart: every part goes to the parts bin.</summary>
        public bool TakeApart(HangarShip h)
        {
            if (h.ReservedBy != null) return false;
            Craft c = Core.Craft.Read(h.Craft);
            foreach (CraftPart cp in c.Parts) if (!string.IsNullOrEmpty(cp.Serial)) Bin.Add(BinPartFrom(cp));
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
            double extra = outsource ? Pricing.OutsourceCharge(q.BuildPrice, 1, S) : overtime ? Pricing.OvertimeCharge(q.BuildPrice, 1, S) : 0;
            double total = q.Total + extra;
            if (!CanAfford(total)) { error = "Not enough funds: this costs " + Pricing.Funds(total) + "."; return null; }
            var job = new BuildJob
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Facility = facility,
                Craft = q.WithBinParts(craft),
                NewPrice = q.NewPrice,
                RefitPrice = q.RefitPrice,
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
            Log.Info("Build started: " + name + " (" + facility + "), paid " + Pricing.Funds(total) + ", " + Pricing.Duration(job.SecondsLeft(S), S)
                     + (overtime ? " with overtime" : "") + (outsource ? ", outsourced" : ""));
            if (outsource || q.BuildPrice <= 0) Finish(job);
            return job;
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
            job.Craft = q.WithBinParts(craft);
            job.Name = name;
            job.NewPrice = q.NewPrice;
            job.RefitPrice = q.RefitPrice;
            job.FuelPrice = q.FuelPrice;
            job.Paid = q.Total;
            job.Progress = newProgress;
            Reserve(job, q);
            Changed();
            Log.Info("Build updated: " + name + ", " + (diff >= 0 ? "paid " + Pricing.Funds(diff) : "refunded " + Pricing.Funds(-diff)) + ", " + Math.Round(newProgress * 100) + "% done");
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
            Log.Info("Build cancelled: " + job.Name + ", refunded " + Pricing.Funds(refund));
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

        /// <summary>Starts a refit of a hangar ship on its own: when done, it's ready to launch.</summary>
        public BuildJob RefitBuild(HangarShip h, out string error)
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
            foreach (HangarShip h in Hangar.Where(x => x.ReservedBy == job.Id && x.Id != job.FromShipId).ToList())
            {
                // Parts taken off a stored ship in the VAB go to the parts bin.
                Craft c = Core.Craft.Read(h.Craft);
                foreach (string s in h.Placed.Where(x => !serials.Contains(x)))
                    if (c.BySerial.TryGetValue(s, out CraftPart cp)) Bin.Add(BinPartFrom(cp));
                Hangar.Remove(h);
            }
            Bin.RemoveAll(b => b.ReservedBy == job.Id);

            HangarShip ready = job.FromShipId != null ? Hangar.FirstOrDefault(x => x.Id == job.FromShipId) : null;
            if (ready == null)
            {
                lastNumber.TryGetValue(job.Name, out int last);
                ready = new HangarShip { Id = Guid.NewGuid().ToString("N"), Name = job.Name, Number = last + 1 };
                lastNumber[job.Name] = ready.Number;
                Hangar.Add(ready);
            }
            else
            {
                // A ship rebuilt as itself (restored to its design, which can leave out a part added by hand):
                // anything not in the new build goes to the parts bin.
                Craft old = Core.Craft.Read(ready.Craft);
                foreach (string s in ready.Placed.Where(x => !serials.Contains(x)))
                    if (old.BySerial.TryGetValue(s, out CraftPart cp)) Bin.Add(BinPartFrom(cp));
            }
            ready.UT = Planetarium.GetUniversalTime();
            ready.Craft = job.Craft.Clone();
            ready.Placed.Clear();
            ready.Placed.AddRange(serials);
            ready.Missing.Clear();
            ready.CameBack.Clear();
            ready.Fuel = 1;
            ready.Ready = true;
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

        static int FlightsIn(Node part)
        {
            Node m = part.Children("MODULE").FirstOrDefault(x => x.Get("name") == Core.Craft.ModuleName);
            return int.TryParse(m?.Get("flights"), out int f) ? f : 0;
        }

        static BinPart BinPartFrom(CraftPart cp)
        {
            return new BinPart { Serial = cp.Serial, Part = cp.PartName, Flights = FlightsIn(cp.Node), PartNode = Builds.Loose(cp.Node) };
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

        /// <summary>Launches a ready hangar ship straight from the hangar, with its default crew.</summary>
        public bool LaunchReady(HangarShip h, out string error)
        {
            error = null;
            if (!h.Ready || h.ReservedBy != null) { error = h.DisplayName + " isn't ready to launch."; return false; }
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
            string path = SuiteFiles.WriteCraft(h);
            ConfigNode craft = ConfigNode.Load(path);
            VesselCrewManifest m = VesselCrewManifest.FromConfigNode(craft);
            m = HighLogic.CurrentGame.CrewRoster.DefaultCrewForVessel(craft, m, true, false);
            Log.Info("Launching " + h.DisplayName + " from the hangar");
            FlightDriver.StartWithNewLaunch(path, HighLogic.CurrentGame.flagURL, site, m);
            return true;
        }

        static bool OnlyLaunchClamps(ProtoVessel pv) =>
            pv.protoPartSnapshots.Count > 0 && pv.protoPartSnapshots.All(ps => ps.partInfo?.partPrefab != null && ps.partInfo.partPrefab.FindModuleImplementing<LaunchClamp>() != null);

        /// <summary>At rollout: a ready hangar ship launches free (KSP charges its price at rollout, so refund it).</summary>
        void ReadyShipRollout(ShipConstruct ship)
        {
            if (Krash.SimActive) return;
            var serials = ship.parts.Select(PRSPart.SerialOf).Where(s => s != null).ToList();
            HangarShip h = ReadyShipOf(serials);
            if (h == null)
            {
                if (serials.Count > 0) Log.Warn(KSP.Localization.Localizer.Format(ship.shipName) + " launched without being built: KSP charged its normal price");
                return;
            }
            float cost = ship.GetShipCosts(out float dry, out float fuel, ShipConstruction.ShipManifest);
            if (Suite.Career) Funding.Instance.AddFunds(cost, TransactionReasons.VesselRollout);
            Hangar.Remove(h);
            Changed();
            Log.Info("Launched " + h.DisplayName + " from the hangar: already paid for, " + Pricing.Funds(cost) + " handed back");
        }
    }
}
