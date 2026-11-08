using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// While a ship is loaded in flight, notes the hardest moments of its flight for its wear (Core.Wear): its top
    /// surface speed, its hottest part as a share of that part's own limit (the worse of skin and core), and its
    /// highest G (KSP's smoothed reading, not the spiky instant one). The peaks are written onto every part a few
    /// times a second, so a piece that comes off takes its own record home. A ship that isn't loaded (a stage
    /// falling out of range) has no readings: it comes home with the peaks it had when it left physics range,
    /// which for a dropped stage is about its speed at separation.
    /// </summary>
    public class PRSFlightWear : VesselModule
    {
        const float Every = 0.25f;     // seconds between writing the peaks to the parts
        const float Settle = 2f;       // seconds after loading (or coming off rails) before readings count

        float speed, g, wait, settled;
        bool sim, simKnown;

        public override Activation GetActivation() => Activation.LoadedVessels;

        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded || vessel.packed) { settled = 0; return; }
            // Loading and coming off rails can jolt a ship for a moment: that isn't flying.
            if (settled < Settle) { settled += TimeWarp.fixedDeltaTime; return; }
            if (vessel.situation == Vessel.Situations.PRELAUNCH) return;
            speed = Mathf.Max(speed, (float)vessel.srfSpeed);
            g = Mathf.Max(g, (float)vessel.geeForce);
            wait -= TimeWarp.fixedDeltaTime;
            if (wait > 0) return;
            wait = Every;
            if (!simKnown) { sim = Krash.SimActive; simKnown = true; }   // the scene's KRASH state doesn't change mid-flight
            if (sim) return;
            float heat = 0;
            foreach (Part p in vessel.parts)
            {
                if (p.maxTemp > 0) heat = Mathf.Max(heat, (float)(p.temperature / p.maxTemp));
                if (p.skinMaxTemp > 0) heat = Mathf.Max(heat, (float)(p.skinTemperature / p.skinMaxTemp));
            }
            foreach (Part p in vessel.parts)
            {
                PRSPart m = PRSPart.Of(p);
                if (m == null) continue;
                if (speed > m.peakSpeed) m.peakSpeed = speed;
                if (heat > m.peakHeat) m.peakHeat = heat;
                if (g > m.peakG) m.peakG = g;
            }
            speed = g = 0;
        }
    }
}
