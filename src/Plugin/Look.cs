using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// The suite's window look, shared with Phil's Iterative Parts (PIP), Phil's Part Hider and Phil's Builds:
    /// flat dark panels, PIP orange, Bahnschrift for names and numbers. Everything is drawn from textures made
    /// here. Adapted from PIP's PipSkin.
    /// </summary>
    static class Look
    {
        public static readonly Color Void = Hex(0x090c13), Panel = Hex(0x111725), Panel2 = Hex(0x171f31), Panel3 = Hex(0x1e2840),
            Rail = Hex(0x0f1421), Line = Hex(0x263049), Line2 = Hex(0x334063),
            Ink = Hex(0xe8ecf5), Ink2 = Hex(0x9ba7c0), Ink3 = Hex(0x7d88a3),
            Amber = Hex(0xffa62b), AmberInk = Hex(0x2a1700), Good = Hex(0x62d595), GoodInk = Hex(0x06210f), Bad = Hex(0xff7a6b), Warn = Hex(0xffcf5a);

        public static GUISkin Skin { get; private set; }
        public static Font Display, Body;

        public static GUIStyle Window, Header, Title, TitleMuted, Eyebrow, H2, H2Muted, Close,
            Tab, TabOn, Card, CardReuse, CardSel, Name, Meta, MetaGood, MetaWarn, MetaBad,
            ChipNew, ChipReuse, ChipGrey, ChipWarn, Price, PriceFree, PriceSub,
            RowKey, RowVal, RowValGood, RowValAmber, RowTotalKey, RowTotal,
            Seg, SegOn, SegSub, SegSubOn, Go, GoOff, Ghost, GhostOn, Link, Hint, Notice, NoticeGood, Footer, FooterText,
            Section, Empty, Bar;

        static readonly Dictionary<Color, Texture2D> solids = new Dictionary<Color, Texture2D>();

        public static void Init()
        {
            if (Skin != null) return;
            Display = OsFont("Bahnschrift") ?? HighLogic.Skin.font;
            Body = OsFont("Segoe UI") ?? HighLogic.Skin.font;

            Skin = UnityEngine.Object.Instantiate(HighLogic.Skin);
            Skin.hideFlags = HideFlags.HideAndDontSave;
            Skin.font = Body;
            Skin.verticalScrollbar = new GUIStyle { fixedWidth = 6, margin = new RectOffset(4, 2, 2, 2), normal = { background = Round(Panel2, Panel2, 3) }, border = new RectOffset(3, 3, 3, 3) };
            Skin.verticalScrollbarThumb = new GUIStyle { fixedWidth = 6, normal = { background = Round(Line2, Line2, 3) }, border = new RectOffset(3, 3, 3, 3) };
            Skin.verticalScrollbarUpButton = new GUIStyle();
            Skin.verticalScrollbarDownButton = new GUIStyle();
            Skin.horizontalScrollbar = new GUIStyle { fixedHeight = 0 };
            Skin.horizontalScrollbarThumb = new GUIStyle();
            Skin.horizontalScrollbarLeftButton = new GUIStyle();
            Skin.horizontalScrollbarRightButton = new GUIStyle();
            Skin.scrollView = new GUIStyle();

            Window = Box(Round(Panel, Line, 10), 11);
            Window.padding = new RectOffset(0, 0, 0, 0);
            Header = Box(Round(Panel2, Panel2, 10), 11);
            Header.padding = new RectOffset(14, 10, 0, 0);
            Title = Text(Display, 19, Ink, FontStyle.Bold);
            TitleMuted = Text(Display, 19, Ink3);
            Eyebrow = Text(Body, 11, Ink3);
            H2 = Text(Display, 22, Ink, FontStyle.Bold);
            H2Muted = Text(Display, 18, Ink2);
            Close = Button(null, Round(Panel3, Panel3, 6), 6, Text(Body, 18, Ink2, align: TextAnchor.MiddleCenter));
            Close.hover.textColor = Ink;

            Tab = Button(null, Round(Panel3, Panel3, 7), 7, Text(Body, 14, Ink2, align: TextAnchor.MiddleCenter));
            Tab.hover.textColor = Ink;
            Tab.padding = new RectOffset(12, 12, 0, 0);
            TabOn = Button(Round(Panel3, Line2, 7), null, 7, Text(Body, 14, Ink, FontStyle.Bold, TextAnchor.MiddleCenter));
            TabOn.padding = Tab.padding;

            Card = Box(Round(Rail, Line, 8), 9);
            Card.padding = new RectOffset(12, 12, 9, 9);
            CardReuse = Box(Round(Color.Lerp(Rail, Good, 0.06f), Color.Lerp(Rail, Good, 0.5f), 8), 9);
            CardReuse.padding = Card.padding;
            CardSel = Box(Round(Color.Lerp(Rail, Amber, 0.07f), Amber, 8), 9);
            CardSel.padding = Card.padding;
            Name = Text(Body, 14, Ink, FontStyle.Bold);
            Meta = Text(Body, 12, Ink2, wrap: true);
            MetaGood = Text(Body, 12, Good, wrap: true);
            MetaWarn = Text(Body, 12, Warn, wrap: true);
            MetaBad = Text(Body, 12, Bad, wrap: true);

            ChipNew = Box(Round(Amber, Amber, 4), 4, Text(Body, 10, AmberInk, FontStyle.Bold, TextAnchor.MiddleCenter));
            ChipReuse = Box(Round(Good, Good, 4), 4, Text(Body, 10, GoodInk, FontStyle.Bold, TextAnchor.MiddleCenter));
            ChipGrey = Box(Round(Line, Line, 4), 4, Text(Body, 10, Ink2, FontStyle.Bold, TextAnchor.MiddleCenter));
            ChipWarn = Box(Round(Color.Lerp(Panel2, Warn, 0.15f), Color.Lerp(Panel2, Warn, 0.5f), 4), 4, Text(Body, 10, Warn, FontStyle.Bold, TextAnchor.MiddleCenter));
            foreach (GUIStyle c in new[] { ChipNew, ChipReuse, ChipGrey, ChipWarn }) c.padding = new RectOffset(6, 6, 1, 1);

            Price = Text(Display, 17, Ink, FontStyle.Bold, TextAnchor.MiddleRight);
            PriceFree = Text(Display, 17, Good, FontStyle.Bold, TextAnchor.MiddleRight);
            PriceSub = Text(Body, 11, Ink2, align: TextAnchor.MiddleRight);

            RowKey = Text(Body, 13, Ink2);
            RowVal = Text(Display, 15, Ink, FontStyle.Bold, TextAnchor.MiddleRight);
            RowValGood = Text(Display, 15, Good, FontStyle.Bold, TextAnchor.MiddleRight);
            RowValAmber = Text(Display, 15, Amber, FontStyle.Bold, TextAnchor.MiddleRight);
            RowTotalKey = Text(Body, 14, Ink, FontStyle.Bold);
            RowTotal = Text(Display, 20, Ink, FontStyle.Bold, TextAnchor.MiddleRight);
            // Bahnschrift's comma hangs below the line, and at some sizes clipping cut its tail off, so "16,303"
            // read as "16.303". These are single-line, right-aligned numbers: let them draw past their box.
            foreach (GUIStyle n in new[] { Price, PriceFree, RowVal, RowValGood, RowValAmber, RowTotal }) n.clipping = TextClipping.Overflow;

            Seg = Button(Round(Rail, Line2, 8), Round(Rail, Color.Lerp(Line2, Amber, 0.6f), 8), 8, Text(Body, 13, Ink2, align: TextAnchor.MiddleCenter));
            SegOn = Button(Round(Color.Lerp(Rail, Amber, 0.12f), Amber, 8), null, 8, Text(Body, 13, Amber, FontStyle.Bold, TextAnchor.MiddleCenter));
            SegSub = Text(Body, 11, Ink3, align: TextAnchor.MiddleCenter);
            SegSubOn = Text(Body, 11, Color.Lerp(Amber, Color.white, 0.4f), align: TextAnchor.MiddleCenter);

            Go = Button(Round(Amber, Amber, 8), Round(Color.Lerp(Amber, Color.white, 0.12f), Color.Lerp(Amber, Color.white, 0.12f), 8), 8,
                Text(Display, 16, AmberInk, FontStyle.Bold, TextAnchor.MiddleCenter));
            Go.padding = new RectOffset(16, 16, 0, 0);
            GoOff = Button(Round(Panel3, Line2, 8), null, 8, Text(Display, 16, Ink3, FontStyle.Bold, TextAnchor.MiddleCenter));
            GoOff.padding = Go.padding;
            Ghost = Button(Round(Panel, Line2, 8), Round(Panel2, Line2, 8), 8, Text(Body, 13, Ink, align: TextAnchor.MiddleCenter));
            Ghost.padding = new RectOffset(12, 12, 0, 0);
            GhostOn = Button(Round(Color.Lerp(Panel, Amber, 0.1f), Amber, 8), null, 8, Text(Body, 13, Amber, align: TextAnchor.MiddleCenter));
            GhostOn.padding = Ghost.padding;
            Link = Text(Body, 12, Amber, align: TextAnchor.MiddleCenter);   // IMGUI has no underline, so links are amber
            Link.hover.textColor = Ink;
            Hint = Text(Body, 12, Ink3, wrap: true);
            Notice = Box(Round(Panel2, Line, 8), 9, Text(Body, 13, Ink2, wrap: true));
            Notice.padding = new RectOffset(12, 12, 8, 8);
            NoticeGood = Box(Round(Color.Lerp(Panel2, Good, 0.08f), Color.Lerp(Panel2, Good, 0.5f), 8), 9, Text(Body, 13, Ink, wrap: true));
            NoticeGood.padding = Notice.padding;
            Footer = Box(Round(Panel2, Panel2, 10), 11);
            Footer.padding = new RectOffset(14, 14, 10, 10);
            FooterText = Text(Body, 12, Ink2, wrap: true);
            FooterText.richText = true;
            Section = Text(Body, 11, Ink3);
            Empty = Text(Body, 13, Ink3, wrap: true, align: TextAnchor.MiddleCenter);
            Bar = new GUIStyle();
        }

        public static Texture2D Solid(Color c)
        {
            if (!solids.TryGetValue(c, out Texture2D t) || t == null) solids[c] = t = Make(1, 1, (x, y) => c);
            return t;
        }

        public static void Fill(Rect r, Color c)
        {
            if (Event.current.type == EventType.Repaint) GUI.DrawTexture(r, Solid(c));
        }

        /// <summary>A progress bar drawn in the last layout rect's space.</summary>
        public static void ProgressBar(float fraction, float width, float height = 7)
        {
            Rect r = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
            r.y += 3;
            Fill(r, Panel3);
            Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fraction), r.height), Amber);
        }

        public static void Chip(string text, GUIStyle style)
        {
            GUILayout.Label(text.ToUpperInvariant(), style, GUILayout.Height(17), GUILayout.ExpandWidth(false));
        }

        static Font OsFont(string name)
        {
            try
            {
                if (!Font.GetOSInstalledFontNames().Contains(name)) return null;
                Font f = Font.CreateDynamicFontFromOSFont(name, 14);
                if (f != null) f.hideFlags = HideFlags.HideAndDontSave;
                return f;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PRS] Font " + name + " unavailable: " + e.Message);
                return null;
            }
        }

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        static GUIStyle Text(Font font, int size, Color color, FontStyle style = FontStyle.Normal, TextAnchor align = TextAnchor.MiddleLeft, bool wrap = false)
        {
            // A little larger than PIP's sizes, with room above and below each line so GUILayout doesn't pack
            // lines on top of each other.
            var s = new GUIStyle
            {
                font = font, fontSize = size + 1, fontStyle = style, alignment = align, wordWrap = wrap, clipping = TextClipping.Clip, richText = true,
                padding = new RectOffset(0, 0, 3, 5), margin = new RectOffset(0, 0, 1, 1),
            };
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = color;
            return s;
        }

        static GUIStyle Box(Texture2D bg, int border, GUIStyle text = null)
        {
            var s = new GUIStyle(text ?? new GUIStyle()) { border = new RectOffset(border, border, border, border) };
            s.normal.background = bg;
            return s;
        }

        static GUIStyle Button(Texture2D normal, Texture2D hover, int border, GUIStyle text)
        {
            var s = new GUIStyle(text) { border = new RectOffset(border, border, border, border) };
            s.normal.background = normal;
            s.hover.background = hover ?? normal;
            s.active.background = hover ?? normal;
            return s;
        }

        static Texture2D Make(int w, int h, Func<int, int, Color> pixel)
        {
            var t = new Texture2D(w, h, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) px[y * w + x] = pixel(x, y);
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        static float RoundDistance(int x, int y, int size, float radius)
        {
            float half = size / 2f;
            float qx = Mathf.Abs(x + 0.5f - half) - (half - radius), qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
            float outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0) - radius;
        }

        /// <summary>A rounded rectangle with a 1-pixel border, for nine-slicing (border = radius + 1).</summary>
        public static Texture2D Round(Color fill, Color border, int radius)
        {
            int size = 2 * radius + 3;
            return Make(size, size, (x, y) =>
            {
                float d = RoundDistance(x, y, size, radius);
                Color c = Color.Lerp(fill, border, Mathf.Clamp01(d + 1.5f));
                c.a *= Mathf.Clamp01(0.5f - d);
                return c;
            });
        }

        /// <summary>The toolbar icon: a hangar outline in PIP orange.</summary>
        public static Texture2D Icon()
        {
            return Make(38, 38, (x, y) =>
            {
                bool wall = (x >= 7 && x <= 9 || x >= 28 && x <= 30) && y >= 6 && y <= 20;
                bool floor = y >= 6 && y <= 8 && x >= 7 && x <= 30;
                int roofY = 20 + (x < 19 ? x - 7 : 30 - x) * 9 / 12;
                bool roof = x >= 7 && x <= 30 && y >= roofY - 1 && y <= roofY + 1;
                bool door = (x >= 14 && x <= 15 || x >= 22 && x <= 23) && y >= 6 && y <= 15 || y >= 14 && y <= 15 && x >= 14 && x <= 23;
                return wall || floor || roof || door ? Amber : new Color(0, 0, 0, 0);
            });
        }
    }
}
