using System;
using System.Collections.Generic;

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

        /// <summary>Serials the editor may keep this session: ones it gave out, and hangar parts it loaded.</summary>
        public static readonly HashSet<string> EditorTrusted = new HashSet<string>();

        public static string NewSerial()
        {
            string s = Guid.NewGuid().ToString("N");
            if (HighLogic.LoadedSceneIsEditor) EditorTrusted.Add(s);
            return s;
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (string.IsNullOrEmpty(serial)) serial = NewSerial();
        }

        public override void OnCopy(PartModule fromModule)
        {
            base.OnCopy(fromModule);
            MakeNew();
        }

        public void MakeNew()
        {
            serial = NewSerial();
            flights = 0;
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
    }
}
