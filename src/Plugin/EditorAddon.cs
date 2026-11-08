using System.Collections.Generic;
using System.Linq;
using KSP.UI.Screens;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// In the VAB and SPH: keeps part serials honest, and (for now) shows a plain prototype list of hangar ships
    /// that can be loaded. The real windows from the approved mockup come in later build steps.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class EditorAddon : MonoBehaviour
    {
        ApplicationLauncherButton button;
        bool show;
        Rect rect = new Rect(260, 120, 420, 120);
        Vector2 scroll;

        void Start()
        {
            PRSPart.EditorTrusted.Clear();
            GameEvents.onEditorShipModified.Add(OnShipModified);
            GameEvents.onEditorLoad.Add(OnEditorLoad);
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            if (ApplicationLauncher.Ready) AddButton();
        }

        void OnDestroy()
        {
            GameEvents.onEditorShipModified.Remove(OnShipModified);
            GameEvents.onEditorLoad.Remove(OnEditorLoad);
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            if (button != null && ApplicationLauncher.Instance != null) ApplicationLauncher.Instance.RemoveModApplication(button);
        }

        void OnEditorLoad(ShipConstruct ship, CraftBrowserDialog.LoadType type) { Check(ship); }
        void OnShipModified(ShipConstruct ship) { Check(ship); }

        /// <summary>
        /// Designs never carry specific hardware: any serial the editor didn't give out or load from the hangar
        /// (a saved craft or subassembly) gets replaced, and so does a duplicate.
        /// </summary>
        static void Check(ShipConstruct ship)
        {
            if (ship == null) return;
            var seen = new HashSet<string>();
            int renewed = 0;
            foreach (Part p in ship.parts)
            {
                PRSPart m = PRSPart.Of(p);
                if (m == null) continue;
                bool trusted = !string.IsNullOrEmpty(m.serial) && PRSPart.EditorTrusted.Contains(m.serial);
                if (!trusted || !seen.Add(m.serial))
                {
                    m.MakeNew();
                    seen.Add(m.serial);
                    renewed++;
                }
                else PRSPart.EditorTrusted.Add(m.serial);
            }
            if (renewed > 0) Log.Info("Gave " + renewed + " parts new serial numbers (new parts, not hangar hardware)");
        }

        /// <summary>Loads a hangar ship into the editor, keeping its parts' serials.</summary>
        public static void LoadHangarShip(HangarShip h)
        {
            string path = SuiteFiles.WriteCraft(h);
            // Trusted before loading, so it doesn't matter when the editor's load events fire.
            foreach (string s in h.Placed) PRSPart.EditorTrusted.Add(s);
            EditorLogic.LoadShipFromFile(path);
            Log.Info("Loaded " + h.DisplayName + " from the hangar (" + h.Placed.Count + " parts)");
        }

        // ---- Prototype list (temporary) ----

        void AddButton()
        {
            if (button != null || ApplicationLauncher.Instance == null) return;
            button = ApplicationLauncher.Instance.AddModApplication(() => show = true, () => show = false, null, null, null, null,
                ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH, Icon());
        }

        static Texture2D Icon()
        {
            var t = new Texture2D(38, 38, TextureFormat.ARGB32, false);
            var clear = new Color(0, 0, 0, 0);
            var orange = new Color(1f, 0.65f, 0.17f, 1f);
            for (int y = 0; y < 38; y++)
                for (int x = 0; x < 38; x++)
                {
                    // A simple hangar outline: walls, a pitched roof and a door.
                    bool wall = (x >= 7 && x <= 9 || x >= 28 && x <= 30) && y >= 6 && y <= 20;
                    bool floor = y >= 6 && y <= 8 && x >= 7 && x <= 30;
                    int roofY = 20 + (x < 19 ? x - 7 : 30 - x) * 9 / 12;
                    bool roof = x >= 7 && x <= 30 && y >= roofY - 1 && y <= roofY + 1;
                    bool door = (x >= 14 && x <= 15 || x >= 22 && x <= 23) && y >= 6 && y <= 15 || y >= 14 && y <= 15 && x >= 14 && x <= 23;
                    t.SetPixel(x, y, wall || floor || roof || door ? orange : clear);
                }
            t.Apply();
            return t;
        }

        void OnGUI()
        {
            if (!show) return;
            GUI.skin = HighLogic.Skin;
            rect = GUILayout.Window(GetInstanceID(), rect, Draw, "Hangar (prototype)", GUILayout.Width(420));
        }

        void Draw(int id)
        {
            PRSScenario sc = PRSScenario.Instance;
            if (sc == null) { GUILayout.Label("The suite's save data isn't loaded."); GUI.DragWindow(); return; }
            GUILayout.Label(sc.Hangar.Count + " ships in the hangar, " + sc.Bin.Count + " loose parts in the bin");
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Min(60 + sc.Hangar.Count * 34, 360)));
            foreach (HangarShip h in sc.Hangar.ToList())
            {
                GUILayout.BeginHorizontal();
                string extra = h.Missing.Count > 0 ? ", " + h.Missing.Count + " missing" : "";
                GUILayout.Label(h.DisplayName + "\n" + h.Placed.Count + " parts, " + h.Flights + " flights, fuel " + Mathf.RoundToInt((float)h.Fuel * 100) + "%" + extra, GUILayout.Width(300));
                if (GUILayout.Button("Load", GUILayout.Width(70))) LoadHangarShip(h);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUI.DragWindow();
        }
    }
}
