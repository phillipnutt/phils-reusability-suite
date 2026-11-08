using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSP.UI.Screens;
using PhilsReusabilitySuite.Core;
using UnityEngine;

[assembly: KSPAssemblyDependency("0Harmony", 0, 0)]

namespace PhilsReusabilitySuite
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class SuiteHarmony : MonoBehaviour
    {
        void Awake()
        {
            try { new Harmony("PhilsReusabilitySuite").PatchAll(typeof(SuiteHarmony).Assembly); Log.Info("Recover buttons hooked"); }
            catch (Exception ex) { Log.Error("Couldn't hook the Recover buttons: " + ex); }
        }
    }

    public enum RecoverChoice { Hangar, Sell }

    /// <summary>
    /// The Recover buttons ask first. Each of KSP's three ways to recover (the flight Recover button, the
    /// Space Center's vessel markers, the Tracking Station) shows the suite's prompt instead, and recovers only
    /// if the player picks To the hangar or Sell for scrap. Cancel leaves the ship where it is.
    /// </summary>
    static class RecoverGate
    {
        public static bool Bypass;
        /// <summary>What the player picked for each vessel being recovered, by persistent id.</summary>
        public static readonly Dictionary<uint, RecoverChoice> Choices = new Dictionary<uint, RecoverChoice>();

        public static bool ShouldAsk(Vessel v) => !Bypass && v != null && PRSScenario.Instance != null && !Krash.SimActive;

        public static void Proceed(Action original)
        {
            Bypass = true;
            try { original(); }
            catch (Exception ex) { Log.Error("Recovering failed: " + ex); }
            finally { Bypass = false; }
        }
    }

    [HarmonyPatch(typeof(AltimeterSliderButtons), "recoverVessel")]
    static class PatchFlightRecover
    {
        static bool Prefix(AltimeterSliderButtons __instance)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (!RecoverGate.ShouldAsk(v)) return true;
            RecoverPrompt.Show(v, choice =>
            {
                RecoverGate.Choices[v.persistentId] = choice;
                RecoverGate.Proceed(() => AccessTools.Method(typeof(AltimeterSliderButtons), "recoverVessel").Invoke(__instance, null));
            });
            return false;
        }
    }

    [HarmonyPatch(typeof(KSCVesselMarkers), "RecoverVessel")]
    static class PatchKscRecover
    {
        static bool Prefix(KSCVesselMarkers __instance, Vessel v)
        {
            if (!RecoverGate.ShouldAsk(v)) return true;
            RecoverPrompt.Show(v, choice =>
            {
                RecoverGate.Choices[v.persistentId] = choice;
                RecoverGate.Proceed(() => AccessTools.Method(typeof(KSCVesselMarkers), "RecoverVessel").Invoke(__instance, new object[] { v }));
            });
            return false;
        }
    }

    [HarmonyPatch(typeof(SpaceTracking), "BtnOnclick_RecoverSelectedVessel")]
    static class PatchTrackingRecover
    {
        static bool Prefix(SpaceTracking __instance)
        {
            Vessel v = __instance.SelectedVessel;
            if (!RecoverGate.ShouldAsk(v)) return true;
            RecoverPrompt.Show(v, choice =>
            {
                RecoverGate.Choices[v.persistentId] = choice;
                // Straight to KSP's own "recover confirmed" step: the suite's prompt was the confirmation.
                RecoverGate.Proceed(() => AccessTools.Method(typeof(SpaceTracking), "OnRecoverConfirm").Invoke(__instance, null));
            });
            return false;
        }
    }

    /// <summary>
    /// Settles recovery money against exactly what KSP paid: the funds just before and just after KSP's own
    /// recovery refund (Funding.onVesselRecoveryProcessing).
    /// </summary>
    [HarmonyPatch(typeof(Funding), "onVesselRecoveryProcessing")]
    static class PatchRecoveryRefund
    {
        static void Prefix(out double __state) { __state = Funding.Instance != null ? Funding.Instance.Funds : 0; }

        static void Postfix(ProtoVessel __0, float __2, double __state)
        {
            if (Funding.Instance == null || PRSScenario.Instance == null) return;
            PRSScenario.Instance.SettleRecovery(__0, Funding.Instance.Funds - __state, __2);
        }
    }

    /// <summary>What recovering a ship is worth, the way KSP works it out.</summary>
    static class RecoveryValue
    {
        /// <summary>Price of the ship's parts and resources, as KSP's recovery refund counts it.</summary>
        public static double Value(ProtoVessel pv)
        {
            double total = 0;
            foreach (ProtoPartSnapshot ps in pv.protoPartSnapshots)
            {
                AvailablePart ap = ps.partInfo ?? PartLoader.getPartInfoByName(ps.partName);
                if (ap == null || ap.name == "kerbalEVA") continue;
                ShipConstruction.GetPartCosts(ps, ap, out float dry, out float fuel);
                total += dry + fuel;
            }
            return total;
        }

        /// <summary>Stock's distance rule: 98% at KSC down to 10% on the far side of Kerbin, 100% on the pad
        /// or runway. (KSP's real figure, which the suite uses for money, can also include strategy effects.)</summary>
        public static double Factor(ProtoVessel pv)
        {
            string at = pv.landedAt ?? "";
            if (at.Contains("LaunchPad") || at.Contains("Runway") || at.Contains("KSC")) return 1;
            if (SpaceCenter.Instance == null || SpaceCenter.Instance.cb == null) return 0;
            CelestialBody cb = SpaceCenter.Instance.cb;
            double d = SpaceCenter.Instance.GreatCircleDistance(cb.GetRelSurfaceNVector(pv.latitude, pv.longitude));
            return Mathf.Lerp(0.98f, 0.1f, (float)(d / (Math.PI * cb.Radius)));
        }

        public static double DistanceKm(ProtoVessel pv)
        {
            if (SpaceCenter.Instance == null || SpaceCenter.Instance.cb == null) return 0;
            CelestialBody cb = SpaceCenter.Instance.cb;
            return SpaceCenter.Instance.GreatCircleDistance(cb.GetRelSurfaceNVector(pv.latitude, pv.longitude)) / 1000.0;
        }

        /// <summary>A piece that can fly again: it has an engine, or a pod or probe core.</summary>
        public static bool CanFly(IEnumerable<string> partNames)
        {
            foreach (string n in partNames)
            {
                AvailablePart ap = PartLoader.getPartInfoByName(n);
                Part p = ap != null ? ap.partPrefab : null;
                if (p == null) continue;
                if (p.FindModuleImplementing<ModuleEngines>() != null || p.FindModuleImplementing<ModuleCommand>() != null) return true;
            }
            return false;
        }
    }

    /// <summary>The Recover prompt: To the hangar (for the transport cost) or Sell for scrap, with the money shown.</summary>
    public class RecoverPrompt : MonoBehaviour
    {
        static RecoverPrompt open;

        Vessel vessel;
        ProtoVessel pv;
        Action<RecoverChoice> onChoose;
        RecoverChoice pick = RecoverChoice.Hangar;
        double value, factor, km;
        bool canFly;
        Rect rect = new Rect(0, 0, 520, 100);

        static Core.Settings S => Suite.Settings;

        public static void Show(Vessel v, Action<RecoverChoice> onChoose)
        {
            if (open != null) Destroy(open.gameObject);
            Look.Init();
            var go = new GameObject("PRSRecoverPrompt");
            open = go.AddComponent<RecoverPrompt>();
            open.vessel = v;
            open.pv = v.protoVessel ?? v.BackupVessel();
            if (v.loaded) open.pv = v.BackupVessel();
            open.onChoose = onChoose;
            open.value = RecoveryValue.Value(open.pv);
            open.factor = RecoveryValue.Factor(open.pv);
            open.km = RecoveryValue.DistanceKm(open.pv);
            open.canFly = RecoveryValue.CanFly(open.pv.protoPartSnapshots.Select(p => p.partName));
            open.rect.x = (Screen.width - open.rect.width) / 2;
            open.rect.y = Screen.height / 4;
        }

        /// <summary>The prompt on screen, if any (the self-test reads it).</summary>
        public static RecoverPrompt Current => open;
        public Vessel Vessel => vessel;
        public bool CanFlyPiece => canFly;

        /// <summary>Presses a button as the player would: a choice, or null for Cancel (self-test only).</summary>
        public void TestChoose(RecoverChoice? c)
        {
            Action<RecoverChoice> done = onChoose;
            Destroy(gameObject);
            if (c.HasValue) done(c.Value);
        }

        double Transport => value * (1 - factor);
        string TransportText => Transport < 0.5 ? "free" : "−" + Pricing.Funds(Transport);
        double Scrap => value * factor * S.ScrapValue;

        void OnGUI()
        {
            GUI.skin = Look.Skin;
            rect = GUILayout.Window(GetInstanceID(), rect, Draw, GUIContent.none, Look.Window, GUILayout.Width(520));
            InputLockManager.SetControlLock(ControlTypes.All & ~ControlTypes.CAMERACONTROLS, "PRSRecoverPrompt");
        }

        void OnDestroy()
        {
            InputLockManager.RemoveControlLock("PRSRecoverPrompt");
            if (open == this) open = null;
        }

        void Draw(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.Space(16);
            GUILayout.BeginHorizontal(); GUILayout.Space(18); GUILayout.BeginVertical();
            GUILayout.Label("RECOVER", Look.Section);
            GUILayout.Label(vessel.vesselName, Look.H2);
            string where = km < 2 ? "At the Space Center" : "Landed " + Math.Round(km).ToString("N0") + " km from KSC";
            GUILayout.Label(where + (factor < 1 ? " · stock would give back " + Math.Round(factor * 100) + "%" : ""), Look.Meta);
            GUILayout.Space(10);
            string home = canFly ? "To the hangar" : "To the parts bin";
            string homeSub = canFly ? "Keep the hardware. It flies again after a refit." : "No engine or probe core, so its parts go to the parts bin.";
            Option(RecoverChoice.Hangar, home, homeSub, Suite.Career ? TransportText : "");
            if (Suite.Career) Option(RecoverChoice.Sell, "Sell for scrap", "Half its price, less the haul from where it landed.", "+" + Pricing.Funds(Scrap));
            GUILayout.Space(6);
            GUILayout.Label("Either way the crew come home and their science is credited. Not worth either? Cancel, and it stays where it is: recover it later from the Tracking Station.", Look.Hint);
            GUILayout.EndVertical(); GUILayout.Space(18); GUILayout.EndHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginHorizontal(Look.Footer);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", Look.Ghost, GUILayout.Height(38))) Destroy(gameObject);
            GUILayout.Space(8);
            string go = pick == RecoverChoice.Hangar ? (canFly ? "Bring it home" : "Bring the parts home") + (Suite.Career ? " · " + TransportText : "") : "Sell it · +" + Pricing.Funds(Scrap);
            if (GUILayout.Button(go, Look.Go, GUILayout.Height(38), GUILayout.MinWidth(200)))
            {
                Action<RecoverChoice> done = onChoose;
                RecoverChoice choice = pick;
                Destroy(gameObject);
                done(choice);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        void Option(RecoverChoice c, string title, string sub, string amount)
        {
            bool on = pick == c;
            GUILayout.BeginHorizontal(on ? Look.CardSel : Look.Card);
            GUILayout.BeginVertical();
            GUILayout.Label(title, Look.Name);
            GUILayout.Label(sub, Look.Meta);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.Label(amount, c == RecoverChoice.Hangar ? Look.RowValAmber : Look.RowValGood, GUILayout.Width(110));
            GUILayout.EndHorizontal();
            if (Event.current.type == EventType.MouseUp && GUILayoutUtility.GetLastRect().Contains(Event.current.mousePosition)) { pick = c; Event.current.Use(); }
            GUILayout.Space(6);
        }
    }
}
