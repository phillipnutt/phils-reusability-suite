using System;
using System.Collections.Generic;
using System.Text;

namespace PhilsReusabilitySuite.Core
{
    /// <summary>
    /// A plain copy of KSP's ConfigNode (name, ordered values, child nodes), so the craft logic can run and be
    /// tested without the game. In game it is converted to and from ConfigNode; for tests it parses .craft text.
    /// </summary>
    public sealed class Node
    {
        public string Name;
        public readonly List<KeyValuePair<string, string>> Values = new List<KeyValuePair<string, string>>();
        public readonly List<Node> Nodes = new List<Node>();

        public Node(string name = "") { Name = name ?? ""; }

        public string Get(string key)
        {
            foreach (KeyValuePair<string, string> kv in Values)
                if (kv.Key == key) return kv.Value;
            return null;
        }

        public IEnumerable<string> GetAll(string key)
        {
            foreach (KeyValuePair<string, string> kv in Values)
                if (kv.Key == key) yield return kv.Value;
        }

        public void Add(string key, string value) { Values.Add(new KeyValuePair<string, string>(key, value ?? "")); }

        /// <summary>Replaces the first value with this key, or adds one.</summary>
        public void Set(string key, string value)
        {
            for (int i = 0; i < Values.Count; i++)
                if (Values[i].Key == key) { Values[i] = new KeyValuePair<string, string>(key, value ?? ""); return; }
            Add(key, value);
        }

        public Node Child(string name)
        {
            foreach (Node n in Nodes)
                if (n.Name == name) return n;
            return null;
        }

        public IEnumerable<Node> Children(string name)
        {
            foreach (Node n in Nodes)
                if (n.Name == name) yield return n;
        }

        public Node AddNode(string name)
        {
            var n = new Node(name);
            Nodes.Add(n);
            return n;
        }

        public Node Clone()
        {
            var c = new Node(Name);
            c.Values.AddRange(Values);
            foreach (Node n in Nodes) c.Nodes.Add(n.Clone());
            return c;
        }

        /// <summary>
        /// Parses KSP config text (a .craft or .cfg file). The result is a nameless node holding the top-level
        /// values and nodes. "//" starts a comment to the end of the line, as in KSP.
        /// </summary>
        public static Node Parse(string text)
        {
            var root = new Node();
            var stack = new Stack<Node>();
            stack.Push(root);
            string pendingName = null;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw;
                int c = line.IndexOf("//", StringComparison.Ordinal);
                if (c >= 0) line = line.Substring(0, c);
                line = line.Trim();
                while (line.Length > 0)
                {
                    if (line[0] == '{')
                    {
                        var n = new Node(pendingName ?? "");
                        stack.Peek().Nodes.Add(n);
                        stack.Push(n);
                        pendingName = null;
                        line = line.Substring(1).Trim();
                        continue;
                    }
                    if (line[0] == '}')
                    {
                        if (stack.Count > 1) stack.Pop();
                        line = line.Substring(1).Trim();
                        continue;
                    }
                    int eq = line.IndexOf('=');
                    int br = line.IndexOfAny(new[] { '{', '}' });
                    if (eq >= 0 && (br < 0 || eq < br))
                    {
                        string key = line.Substring(0, eq).Trim();
                        string value = (br < 0 ? line.Substring(eq + 1) : line.Substring(eq + 1, br - eq - 1)).Trim();
                        stack.Peek().Add(key, value);
                        line = br < 0 ? "" : line.Substring(br);
                        continue;
                    }
                    pendingName = (br < 0 ? line : line.Substring(0, br)).Trim();
                    line = br < 0 ? "" : line.Substring(br);
                }
            }
            return root;
        }

        /// <summary>Writes the node's contents (not its own name) as KSP config text, tab-indented.</summary>
        public string WriteContents()
        {
            var sb = new StringBuilder();
            WriteBody(sb, 0);
            return sb.ToString();
        }

        void WriteBody(StringBuilder sb, int depth)
        {
            string ind = new string('\t', depth);
            foreach (KeyValuePair<string, string> kv in Values) sb.Append(ind).Append(kv.Key).Append(" = ").Append(kv.Value).Append('\n');
            foreach (Node n in Nodes)
            {
                sb.Append(ind).Append(n.Name).Append('\n').Append(ind).Append("{\n");
                n.WriteBody(sb, depth + 1);
                sb.Append(ind).Append("}\n");
            }
        }
    }
}
