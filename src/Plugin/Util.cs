using System;
using System.IO;
using System.Reflection;
using PhilsReusabilitySuite.Core;

namespace PhilsReusabilitySuite
{
    static class Log
    {
        public static void Info(string m) { UnityEngine.Debug.Log("[PRS] " + m); }
        public static void Warn(string m) { UnityEngine.Debug.LogWarning("[PRS] " + m); }
        public static void Error(string m) { UnityEngine.Debug.LogError("[PRS] " + m); }
    }

    /// <summary>Converts between KSP's ConfigNode and the suite's plain Node.</summary>
    static class KspNodes
    {
        public static Node FromKsp(ConfigNode c)
        {
            var n = new Node(c.name);
            for (int i = 0; i < c.values.Count; i++) n.Add(c.values[i].name, c.values[i].value);
            for (int i = 0; i < c.nodes.Count; i++) n.Nodes.Add(FromKsp(c.nodes[i]));
            return n;
        }

        public static ConfigNode ToKsp(Node n, string name = null)
        {
            var c = new ConfigNode(name ?? n.Name);
            foreach (var kv in n.Values) c.AddValue(kv.Key, kv.Value);
            foreach (Node child in n.Nodes) c.AddNode(ToKsp(child));
            return c;
        }
    }

    /// <summary>Tells whether the current flight is a KRASH simulation, which the suite must ignore.</summary>
    static class Krash
    {
        public static bool SimActive
        {
            get
            {
                Game game = HighLogic.CurrentGame;
                if (game?.scenarios == null) return false;
                foreach (ProtoScenarioModule psm in game.scenarios)
                {
                    if (psm.moduleName != "KRASHPersistent" || psm.moduleRef == null) continue;
                    FieldInfo f = psm.moduleRef.GetType().GetField("shelterSimulationActive");
                    if (f != null && f.GetValue(psm.moduleRef) is bool b) return b;
                }
                return false;
            }
        }
    }

    static class SuiteFiles
    {
        /// <summary>The suite's folder inside the current save, created on demand.</summary>
        public static string SaveDir
        {
            get
            {
                string dir = Path.Combine(KSPUtil.ApplicationRootPath, "saves", HighLogic.SaveFolder, "PhilsReusabilitySuite");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>Writes a hangar ship as a .craft file the editor or a launch can load (every part noted as its
        /// hardware), and returns its path.</summary>
        public static string WriteCraft(HangarShip h)
        {
            ConfigNode c = KspNodes.ToKsp(h.Noted(), "");
            c.SetValue("ship", h.DisplayName, true);
            string path = Path.Combine(SaveDir, "hangar-" + h.Id + ".craft");
            c.Save(path);
            return path;
        }
    }
}
