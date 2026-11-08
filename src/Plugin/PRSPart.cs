using System;
using System.Collections.Generic;
using System.Linq;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// On every part (added by Patches/PRSPart.cfg): the part's serial number and how many flights it has made.
    /// A copy of a part (symmetry, alt-click) is a new part with a new serial.
    /// </summary>
    public class PRSPart : PartModule
    {
        [KSPField(isPersistant = true)] public string serial = "";
        [KSPField(isPersistant = true)] public int flights = 0;
        /// <summary>The hangar ship this part belongs to, "Name|Number" (empty for a new or loose part).</summary>
        [KSPField(isPersistant = true)] public string hull = "";
        /// <summary>The Phil's Builds design this part was placed from (its stable id), and its slot in that
        /// design's file. Empty and -1 for a part added by hand.</summary>
        [KSPField(isPersistant = true)] public string design = "";
        [KSPField(isPersistant = true)] public int slot = -1;

        public static string NewSerial() => Guid.NewGuid().ToString("N");

        /// <summary>One of this module's saved values on a part that isn't loaded (serial, hull, design...).</summary>
        public static string ValueIn(ProtoPartSnapshot ps, string key) =>
            ps?.modules.FirstOrDefault(x => x.moduleName == Core.Craft.ModuleName)?.moduleValues?.GetValue(key);

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (string.IsNullOrEmpty(serial)) serial = NewSerial();
        }

        public override void OnCopy(PartModule fromModule)
        {
            base.OnCopy(fromModule);
            MakeNew();
            // A copy made by hand isn't part of any design.
            design = "";
            slot = -1;
        }

        public void MakeNew()
        {
            serial = NewSerial();
            flights = 0;
            hull = "";
        }

        public override string GetInfo() { return ""; }

        public static PRSPart Of(Part p) { return p == null ? null : p.FindModuleImplementing<PRSPart>(); }

        public static string SerialOf(Part p)
        {
            PRSPart m = Of(p);
            return m == null || string.IsNullOrEmpty(m.serial) ? null : m.serial;
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
    }
}
