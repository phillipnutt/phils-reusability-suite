using System;
using System.Collections.Generic;
using System.Globalization;

namespace PhilsReusabilitySuite.Core
{
    /// <summary>Every number the suite uses, read from PRS_SETTINGS in GameData (Settings.cfg).</summary>
    public sealed class Settings
    {
        /// <summary>Build days = (work in funds ÷ BuildBase) ^ BuildExponent. Work is new parts plus refit, without fuel.</summary>
        public double BuildBase = 20000;
        public double BuildExponent = 0.8;
        /// <summary>Seconds in a day on the game clock (Kerbin: 6 hours). Set from KSP at runtime.</summary>
        public double DaySeconds = 21600;
        /// <summary>Overtime builds this many times faster...</summary>
        public double OvertimeSpeed = 2;
        /// <summary>...for this share of the build price still to build.</summary>
        public double OvertimeCost = 0.25;
        /// <summary>Outsourcing finishes at once for this share of the build price still to build.</summary>
        public double OutsourceCost = 1.0;
        /// <summary>Refit price share of a flown part: RefitBase plus RefitPerFlight for every flight it has made.</summary>
        public double RefitBase = 0.10;
        public double RefitPerFlight = 0.01;
        /// <summary>Selling for scrap returns this share of the part price.</summary>
        public double ScrapValue = 0.5;
        /// <summary>Build lines per VAB or SPH level.</summary>
        public int LinesPerLevel = 1;
        /// <summary>Only pieces built from a Phil's Builds design come home to the hangar; others go to the parts bin.</summary>
        public bool RequireDesign = true;

        public static Settings FromNode(Node n)
        {
            var s = new Settings();
            if (n == null) return s;
            s.BuildBase = D(n, "buildBase", s.BuildBase);
            s.BuildExponent = D(n, "buildExponent", s.BuildExponent);
            s.OvertimeSpeed = D(n, "overtimeSpeed", s.OvertimeSpeed);
            // Prices and shares may be 0 (free overtime, free refits, worthless scrap); rates and sizes must be above 0.
            s.OvertimeCost = D(n, "overtimeCost", s.OvertimeCost, true);
            s.OutsourceCost = D(n, "outsourceCost", s.OutsourceCost, true);
            s.RefitBase = D(n, "refitBase", s.RefitBase, true);
            s.RefitPerFlight = D(n, "refitPerFlight", s.RefitPerFlight, true);
            s.ScrapValue = D(n, "scrapValue", s.ScrapValue, true);
            s.LinesPerLevel = (int)Math.Max(1, D(n, "linesPerLevel", s.LinesPerLevel));
            if (n.Get("requireDesign") != null) s.RequireDesign = !string.Equals(n.Get("requireDesign"), "false", StringComparison.OrdinalIgnoreCase);
            return s;
        }

        static double D(Node n, string key, double def, bool zeroOk = false)
        {
            string v = n.Get(key);
            return v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && (d > 0 || (zeroOk && d == 0)) ? d : def;
        }
    }

    public static class Pricing
    {
        /// <summary>Seconds of game time to build this much work at normal pace.</summary>
        public static double BuildSeconds(double work, Settings s)
        {
            if (work <= 0) return 0;
            return Math.Pow(work / s.BuildBase, s.BuildExponent) * s.DaySeconds;
        }

        /// <summary>Share of its price that refitting a part costs: nothing if it has never flown.</summary>
        public static double RefitShare(int flights, Settings s)
        {
            return flights <= 0 ? 0 : s.RefitBase + s.RefitPerFlight * flights;
        }

        public static double OvertimeCharge(double buildPrice, double remaining, Settings s) => buildPrice * Clamp01(remaining) * s.OvertimeCost;
        public static double OutsourceCharge(double buildPrice, double remaining, Settings s) => buildPrice * Clamp01(remaining) * s.OutsourceCost;

        static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

        /// <summary>"1.1 days", "3.4 hours", "20 min".</summary>
        public static string Duration(double seconds, Settings s)
        {
            if (seconds <= 0) return "now";
            double days = seconds / s.DaySeconds;
            if (days >= 0.95) return days.ToString("0.0", CultureInfo.InvariantCulture) + (days < 1.05 ? " day" : " days");
            double hours = seconds / 3600;
            if (hours >= 1) return hours.ToString("0.0", CultureInfo.InvariantCulture) + " hours";
            return Math.Max(1, Math.Round(seconds / 60)).ToString(CultureInfo.InvariantCulture) + " min";
        }

        public static string Funds(double f) => Math.Round(f).ToString("N0", CultureInfo.InvariantCulture);
    }
}
