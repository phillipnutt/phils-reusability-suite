using System;
using System.Globalization;

namespace PhilsReusabilitySuite.Core
{
    /// <summary>
    /// Wear: one number per craft, 0 to 1, from how hard its flights were (Phil, 2026-11-08). A flight adds wear for
    /// its top surface speed, its hottest part (as a share of that part's own limit) and its highest G. Wear sets the
    /// refurbish price, and a craft at 100% must be refurbished before it flies again. Nothing else: no failures.
    /// </summary>
    public static class Wear
    {
        /// <summary>The wear one flight adds, from its peaks: m/s, share of the heat limit, G.</summary>
        public static double OfFlight(double speed, double heat, double g, Settings s)
        {
            double w = s.WearSpeed * Pricing.Clamp01(speed / s.WearSpeedFull);
            w += s.WearHeat * Pricing.Clamp01((heat - s.WearHeatFrom) / (1 - s.WearHeatFrom));
            w += s.WearG * Pricing.Clamp01((g - s.WearGFrom) / (s.WearGFull - s.WearGFrom));
            return w;
        }

        /// <summary>A craft's wear after a flight that added this much (never above 100%).</summary>
        public static double After(double before, double added) => Pricing.Clamp01(before + added);

        /// <summary>Worn out: it must be refurbished before it flies again. (Anything that shows as 100%.)</summary>
        public static bool WornOut(double wear) => wear >= 0.995;

        /// <summary>"23%".</summary>
        public static string Percent(double wear) => Math.Round(Pricing.Clamp01(wear) * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    }
}
