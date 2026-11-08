using System;

namespace PhilsReusabilitySuite.Core
{
    /// <summary>
    /// The suite's landing rules for stages that fall out of range, following StageRecovery's published rules:
    /// terminal speed from mass and drag, a powered landing with a 1.5× fuel margin, a burn-up check above
    /// 2,000 m/s, and the landing-speed limits (full value under 6 m/s, lost over 12 m/s).
    /// </summary>
    public static class Landing
    {
        public const double SafeSpeed = 6;      // m/s: lands intact
        public const double LostSpeed = 12;     // m/s: over this, nothing survives
        public const double PoweredTouchdown = 2;
        public const double BurnUpSpeed = 2000;  // m/s
        public const double MarginOnFuel = 1.5;
        const double G = 9.81;

        /// <summary>Terminal speed in m/s: sqrt(2000·m·g / (ρ·CdA)), mass in tonnes, CdA in m² (KSP's drag units).</summary>
        public static double TerminalSpeed(double massTonnes, double cdA, double seaLevelDensity)
        {
            if (cdA <= 0 || seaLevelDensity <= 0) return 200;
            return Math.Sqrt(2000 * massTonnes * G / (seaLevelDensity * cdA));
        }

        /// <summary>Propellant (tonnes) needed to slow a falling stage from <paramref name="speed"/> to a gentle
        /// touchdown, with the 1.5× margin, by the rocket equation.</summary>
        public static double PropellantNeeded(double massTonnes, double speed, double isp)
        {
            if (speed <= PoweredTouchdown || isp <= 0) return 0;
            double dv = (speed - PoweredTouchdown) * MarginOnFuel;
            return massTonnes * (1 - Math.Exp(-dv / (isp * G)));
        }

        /// <summary>Chance of burning up at this speed, less the ablator left (0 to 1).</summary>
        public static double BurnUpChance(double surfaceSpeed, double ablatorFraction)
        {
            if (surfaceSpeed <= BurnUpSpeed) return 0;
            double c = 2 * (surfaceSpeed / BurnUpSpeed - 1) - ablatorFraction;
            return c < 0 ? 0 : c > 1 ? 1 : c;
        }

        /// <summary>Chance that an outer part breaks off in a landing at this speed: none at 6 m/s, all at 12.</summary>
        public static double BreakChance(double landingSpeed)
        {
            if (landingSpeed <= SafeSpeed) return 0;
            if (landingSpeed >= LostSpeed) return 1;
            return (landingSpeed - SafeSpeed) / (LostSpeed - SafeSpeed);
        }
    }
}
