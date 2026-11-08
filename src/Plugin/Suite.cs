using System.Collections.Generic;
using PhilsReusabilitySuite.Core;

namespace PhilsReusabilitySuite
{
    /// <summary>The suite's settings and KSP-side helpers shared by every scene.</summary>
    static class Suite
    {
        static Settings settings;

        public static Settings Settings
        {
            get
            {
                if (settings != null) return settings;
                ConfigNode[] nodes = GameDatabase.Instance != null ? GameDatabase.Instance.GetConfigNodes("PRS_SETTINGS") : new ConfigNode[0];
                settings = Settings.FromNode(nodes.Length > 0 ? KspNodes.FromKsp(nodes[0]) : null);
                if (KSPUtil.dateTimeFormatter != null && KSPUtil.dateTimeFormatter.Day > 0) settings.DaySeconds = KSPUtil.dateTimeFormatter.Day;
                return settings;
            }
        }

        public static bool Career => HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode == Game.Modes.CAREER && Funding.Instance != null;

        public static double Funds => Career ? Funding.Instance.Funds : double.PositiveInfinity;

        public static void Charge(double amount)
        {
            if (Career && amount != 0) Funding.Instance.AddFunds(-amount, TransactionReasons.VesselRollout);
        }

        /// <summary>Build lines in a building: one per facility level (times linesPerLevel).</summary>
        public static int Lines(string facility)
        {
            // The normalized level (0, 0.5, 1 for stock's three levels) is what KSP's own pre-launch checks use;
            // the level count isn't always reported (outside the Space Center), so stock's three is assumed then.
            SpaceCenterFacility f = facility == "SPH" ? SpaceCenterFacility.SpaceplaneHangar : SpaceCenterFacility.VehicleAssemblyBuilding;
            int count = ScenarioUpgradeableFacilities.GetFacilityLevelCount(f);
            if (count < 2) count = 3;
            float norm = ScenarioUpgradeableFacilities.GetFacilityLevel(f);
            int level = UnityEngine.Mathf.RoundToInt(UnityEngine.Mathf.Clamp01(norm) * (count - 1)) + 1;
            return System.Math.Max(1, level) * Settings.LinesPerLevel;
        }

        public static string FacilityOf(Node craft) => craft?.Get("type") == "SPH" ? "SPH" : "VAB";

        public static void Message(string text, float seconds = 6f)
        {
            ScreenMessages.PostScreenMessage("<color=#ffa62b>" + text + "</color>", seconds, ScreenMessageStyle.UPPER_CENTER);
        }
    }

    /// <summary>Part prices from craft nodes, the way KSP prices a launch (module costs included).</summary>
    sealed class KspCosts : ICostSource
    {
        public static readonly KspCosts Instance = new KspCosts();

        public PartCost Cost(Node part)
        {
            var c = new PartCost();
            string r = part.Get("part") ?? "";
            string name = r.Contains("_") ? r.Substring(0, r.LastIndexOf('_')) : r;
            AvailablePart ap = PartLoader.getPartInfoByName(name);
            if (ap == null) return c;
            ConfigNode n = KspNodes.ToKsp(part, "PART");
            ShipConstruction.GetPartCostsAndMass(n, ap, out float dry, out float fuel, out float dryMass, out float fuelMass);
            c.Dry = dry;
            foreach (ConfigNode res in n.GetNodes("RESOURCE"))
            {
                string rn = res.GetValue("name");
                if (rn != null && double.TryParse(res.GetValue("amount"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double a))
                    c.Amounts[rn] = (c.Amounts.TryGetValue(rn, out double had) ? had : 0) + a;
            }
            return c;
        }

        public double UnitCost(string resource)
        {
            PartResourceDefinition def = PartResourceLibrary.Instance != null ? PartResourceLibrary.Instance.GetDefinition(resource) : null;
            return def != null ? def.unitCost : 0;
        }
    }
}
