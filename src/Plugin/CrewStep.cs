using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PhilsReusabilitySuite
{
    /// <summary>
    /// The crew step before a hangar ship launches (Phil, play-test 2026-11-09 #3: "implement who the crew is going to
    /// be before launch"). It starts with the crew KSP's own launch-pad dialog would pick, and is laid out like KSP's
    /// crew panel: the crew by part on one side, the kerbals at home on the other, with Fill, Clear and Reset. KSP's
    /// own panel only lives inside its launch-pad dialog (which lists every craft file) and the VAB, so the suite
    /// draws its own. A ship with no seats launches straight away.
    /// </summary>
    public partial class SuiteWindow
    {
        HangarShip crewShip;
        VesselCrewManifest crewList;
        Vector2 scrollCrew, scrollAvail;

        /// <summary>The ship whose crew is being picked, and the crew so far (for the self-tests).</summary>
        public HangarShip CrewShip => crewShip;
        public VesselCrewManifest CrewList => crewList;

        /// <summary>
        /// Starts a launch: the checks first (nothing is charged), then the crew step for a ship with seats. A ship with
        /// no seats launches at once. False, with the reason shown, if it can't go.
        /// </summary>
        public bool StartLaunch(HangarShip h)
        {
            PRSScenario sc = PRSScenario.Instance;
            crewShip = null;
            crewList = null;
            if (sc == null || h == null) return false;
            if (!sc.CanLaunch(h, out string err, out _)) { error = err; return false; }
            VesselCrewManifest m = sc.DefaultCrew(h);
            error = null;
            if (!m.GetCrewableParts().Any(p => p.AnySeats())) return LaunchWith(sc, h, m);
            crewShip = h;
            crewList = m;
            scrollCrew = scrollAvail = Vector2.zero;
            Show = true;
            return true;
        }

        /// <summary>The crew step's Launch: goes with the crew as picked.</summary>
        public bool ConfirmCrew()
        {
            PRSScenario sc = PRSScenario.Instance;
            if (sc == null || crewShip == null || crewList == null) return false;
            if (!LaunchWith(sc, crewShip, crewList)) return false;
            crewShip = null;
            crewList = null;
            Show = false;
            return true;
        }

        bool LaunchWith(PRSScenario sc, HangarShip h, VesselCrewManifest m)
        {
            if (!sc.LaunchReady(h, out string err, m)) { error = err; return false; }
            error = null;
            return true;
        }

        // ---- What the buttons do (KSP's crew panel has the same three: Fill, Clear, Reset) ----

        /// <summary>Kerbals at home who can fly: available, and not already in a seat here.</summary>
        public static List<ProtoCrewMember> AtHome(VesselCrewManifest m) =>
            HighLogic.CurrentGame.CrewRoster.Crew
                .Where(c => c.rosterStatus == ProtoCrewMember.RosterStatus.Available && !m.Contains(c))
                .OrderBy(c => TraitOf(c)).ThenByDescending(c => c.experienceLevel).ThenBy(c => c.name)
                .ToList();

        /// <summary>Puts a kerbal in the first empty seat. False if every seat is taken.</summary>
        public static bool Seat(VesselCrewManifest m, ProtoCrewMember c)
        {
            foreach (PartCrewManifest pcm in m.GetCrewableParts())
            {
                ProtoCrewMember[] seats = pcm.GetPartCrew();
                for (int i = 0; i < seats.Length; i++)
                    if (seats[i] == null) { pcm.AddCrewToSeat(c, i); return true; }
            }
            return false;
        }

        public static void Fill(VesselCrewManifest m)
        {
            foreach (ProtoCrewMember c in AtHome(m)) if (!Seat(m, c)) break;
        }

        public static void Clear(VesselCrewManifest m)
        {
            foreach (PartCrewManifest pcm in m.GetCrewableParts())
            {
                ProtoCrewMember[] seats = pcm.GetPartCrew();
                for (int i = 0; i < seats.Length; i++) if (seats[i] != null) pcm.RemoveCrewFromSeat(i);
            }
        }

        static bool AnyEmptySeat(VesselCrewManifest m) => m.GetCrewableParts().Any(p => p.GetPartCrew().Any(c => c == null));

        static string TraitOf(ProtoCrewMember c) => c.experienceTrait?.Title ?? c.trait ?? "";

        static string TraitLine(ProtoCrewMember c) =>
            TraitOf(c) + (c.experienceLevel > 0 ? "  <color=#ffa62b>" + new string('★', Mathf.Clamp(c.experienceLevel, 0, 5)) + "</color>" : " · level 0");

        // ---- Drawing ----

        void CrewStep(PRSScenario sc)
        {
            HangarShip h = crewShip;
            if (!sc.Hangar.Contains(h) || crewList == null) { crewShip = null; crewList = null; return; }
            GUILayout.BeginVertical();
            GUILayout.Space(10);
            Indent(() =>
            {
                GUILayout.Label((h.Ready ? "Ready to fly" : "Stored") + " · " + (Suite.FacilityOf(h.Craft) == "SPH" ? "Runway" : "Launch pad"), Look.Eyebrow);
                GUILayout.Label(h.DisplayName, Look.H2);
                string under = BuildsLink.Under(h);
                int seats = crewList.GetCrewableParts().Sum(p => p.GetPartCrew().Length);
                GUILayout.Label((under != null ? "on " + under + " · " : "") + seats + (seats == 1 ? " seat" : " seats") + " · "
                                + (h.Flights == 0 ? "never flown" : h.Flights + (h.Flights == 1 ? " flight" : " flights"))
                                + (h.Flights > 0 || h.Wear >= 0.005 ? " · wear " + Core.Wear.Percent(h.Wear) : ""), Look.Meta);
            });
            GUILayout.Space(6);
            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            GUILayout.Space(16);

            // The crew, by part, seat by seat.
            GUILayout.BeginVertical(Look.Notice, GUILayout.Width(380), GUILayout.ExpandHeight(true));
            GUILayout.Label("CREW", Look.Section);
            scrollCrew = GUILayout.BeginScrollView(scrollCrew, GUILayout.ExpandHeight(true));
            foreach (PartCrewManifest pcm in crewList.GetCrewableParts())
            {
                ProtoCrewMember[] seats = pcm.GetPartCrew();
                if (seats.Length == 0) continue;
                GUILayout.Label("<color=#ffa62b>" + (pcm.PartInfo != null ? pcm.PartInfo.title : "Crewed part") + "</color>", Look.Meta);
                for (int i = 0; i < seats.Length; i++)
                {
                    int seat = i;
                    if (seats[i] != null) KerbalRow(seats[i], "×", () => pcm.RemoveCrewFromSeat(seat));
                    else
                    {
                        GUILayout.BeginHorizontal(Look.Card, GUILayout.Height(44));
                        GUILayout.Label("Empty seat", Look.Hint);
                        GUILayout.EndHorizontal();
                        GUILayout.Space(4);
                    }
                }
                GUILayout.Space(4);
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Fill", Look.Ghost, GUILayout.Height(28), GUILayout.Width(70))) { Fill(crewList); GUIUtility.ExitGUI(); }
            GUILayout.Space(6);
            if (GUILayout.Button("Clear", Look.Ghost, GUILayout.Height(28), GUILayout.Width(70))) { Clear(crewList); GUIUtility.ExitGUI(); }
            GUILayout.Space(6);
            if (GUILayout.Button("Reset", Look.Ghost, GUILayout.Height(28), GUILayout.Width(70))) { crewList = sc.DefaultCrew(h); GUIUtility.ExitGUI(); }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.Space(10);

            // The kerbals at home.
            GUILayout.BeginVertical(Look.Notice, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.Label("AVAILABLE", Look.Section);
            List<ProtoCrewMember> home = AtHome(crewList);
            bool room = AnyEmptySeat(crewList);
            scrollAvail = GUILayout.BeginScrollView(scrollAvail, GUILayout.ExpandHeight(true));
            if (home.Count == 0) GUILayout.Label("Nobody else is free to fly. Hire more at the Astronaut Complex.", Look.Hint);
            foreach (ProtoCrewMember c in home) KerbalRow(c, room ? "Add" : null, () => Seat(crewList, c));
            GUILayout.EndScrollView();
            if (!room && home.Count > 0) GUILayout.Label("Every seat is taken: take someone out to swap.", Look.Hint);
            GUILayout.EndVertical();
            GUILayout.Space(12);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.BeginHorizontal(Look.Footer);
            int going = crewList.GetAllCrew(false).Count;
            GUILayout.Label(going == 0 ? "Nobody aboard: it flies uncrewed." : "Starts with KSP's own picks. Change anyone, then launch.", Look.FooterText);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", Look.Ghost, GUILayout.Height(38))) { crewShip = null; crewList = null; error = null; GUIUtility.ExitGUI(); }
            GUILayout.Space(8);
            string label = h.Ready ? "Launch · free" : "Refuel and launch · " + Core.Pricing.Funds(sc.RefuelPrice(h));
            if (GUILayout.Button(label, Look.Go, GUILayout.Height(38), GUILayout.MinWidth(160))) { ConfirmCrew(); GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        void KerbalRow(ProtoCrewMember c, string action, Action onClick)
        {
            GUILayout.BeginHorizontal(Look.Card, GUILayout.Height(44));
            GUILayout.BeginVertical();
            GUILayout.Label(c.name, Look.Name);
            GUILayout.Label(TraitLine(c), Look.Meta);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (action != null && GUILayout.Button(action, Look.Ghost, GUILayout.Height(26), GUILayout.Width(action == "×" ? 34 : 60))) { onClick(); GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }
    }
}
