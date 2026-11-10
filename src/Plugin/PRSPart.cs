using System;
using System.Collections.Generic;
using System.Linq;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// On every part (added by Patches/PRSPart.cfg): which Phil's Builds design and slot it was placed from, this
    /// flight's hardest moments, and, in the VAB only, a note that it's a real, built ship's hardware. Nothing else
    /// about a ship is kept on its parts: ships, launches and the bin are the suite's own records (round 4).
    /// A copy of a part (symmetry, alt-click) is a new part.
    /// </summary>
    public class PRSPart : PartModule
    {
        /// <summary>The Phil's Builds design this part was placed from (its stable id), and its slot in that
        /// design's file. Empty and -1 for a part added by hand.</summary>
        [KSPField(isPersistant = true)] public string design = "";
        [KSPField(isPersistant = true)] public int slot = -1;
        /// <summary>
        /// Set only by the suite's own loads: this part is a stored ship's hardware ("craftId:index", Core.Hardware).
        /// Checked against the hangar whenever it's used, cleared when anything else brings parts in, and gone at launch.
        /// </summary>
        [KSPField(isPersistant = true)] public string hw = "";
        /// <summary>This flight's hardest moments so far (surface speed, hottest part as a share of its limit, G),
        /// kept on every part so a piece that comes off takes its own record home. Cleared at launch.</summary>
        [KSPField(isPersistant = true)] public float peakSpeed = 0, peakHeat = 0, peakG = 0;

        /// <summary>One of this module's saved values on a part that isn't loaded (design, peaks...).</summary>
        public static string ValueIn(ProtoPartSnapshot ps, string key) =>
            ps?.modules.FirstOrDefault(x => x.moduleName == Core.Craft.ModuleName)?.moduleValues?.GetValue(key);

        public static bool Has(ProtoPartSnapshot ps) => ps != null && ps.modules.Any(x => x.moduleName == Core.Craft.ModuleName);

        public override void OnCopy(PartModule fromModule)
        {
            base.OnCopy(fromModule);
            // A copy made by hand is a new part, and isn't part of any design.
            hw = "";
            design = "";
            slot = -1;
            ClearPeaks();
        }

        public void ClearPeaks() { peakSpeed = peakHeat = peakG = 0; }

        public override string GetInfo() { return ""; }

        public static PRSPart Of(Part p) { return p == null ? null : p.FindModuleImplementing<PRSPart>(); }

        /// <summary>A live part's hardware note, or null.</summary>
        public static string NoteOf(Part p)
        {
            PRSPart m = Of(p);
            return m == null || string.IsNullOrEmpty(m.hw) ? null : m.hw;
        }

        /// <summary>Starts the in-game self-test (used by the unattended test harness only).</summary>
        [KSPEvent(guiActive = false, guiActiveEditor = false)]
        public void PRSSelfTest() { SelfTest.Begin(); }

        /// <summary>Starts the in-game self-test of the build queue (test harness only).</summary>
        [KSPEvent(guiActive = false, guiActiveEditor = false)]
        public void PRSSelfTestBuild() { SelfTestBuild.Begin(); }

        /// <summary>Starts the in-game self-test of stage recovery (test harness only).</summary>
        [KSPEvent(guiActive = false, guiActiveEditor = false)]
        public void PRSSelfTestStages() { SelfTestStages.Begin(); }

        /// <summary>Starts the in-game self-test of the Phil's Builds link (test harness only).</summary>
        [KSPEvent(guiActive = false, guiActiveEditor = false)]
        public void PRSSelfTestLink() { SelfTestLink.Begin(); }

        /// <summary>Starts the in-game self-test of a powered landing (test harness only).</summary>
        [KSPEvent(guiActive = false, guiActiveEditor = false)]
        public void PRSSelfTestPowered() { SelfTestPowered.Begin(); }

        /// <summary>Starts the in-game self-test of the whole reuse loop (test harness only).</summary>
        [KSPEvent(guiActive = false, guiActiveEditor = false)]
        public void PRSSelfTestReuse() { SelfTestReuse.Begin(); }

        /// <summary>Starts the in-game check with the real Phil's Iterative Parts (test harness only).</summary>
        [KSPEvent(guiActive = false, guiActiveEditor = false)]
        public void PRSSelfTestPip() { SelfTestPip.Begin(); }

        /// <summary>Starts the in-game check that KSP's launch and part ids survive docking and saving (test harness only).</summary>
        [KSPEvent(guiActive = false, guiActiveEditor = false)]
        public void PRSSelfTestIds() { SelfTestIds.Begin(); }
    }
}
