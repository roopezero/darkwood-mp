using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DarkwoodMP.Net;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for what each player sees
    /// (tools/tests/map_scenario_view.py): players walking up to a spot at
    /// running speed, together and far behind each other, while every game
    /// watches its own view light (Player.FOVLight) every frame:
    ///
    ///   black: its renderer switched off (Light2D.LateUpdate's ray straight
    ///   down hit something, Sync.ViewWatch);
    ///   not its own: the cone's mesh doesn't match rays cast from where our
    ///   player stands now (a mesh drawn by another light, somewhere else, as a
    ///   puppet's copied lantern once did);
    ///   ghost blockers: a collider that cuts the view (the light's shadow
    ///   layers) inside the light's reach whose saved object is switched off
    ///   (SaveableObject.Active false), or one of the mod's own objects;
    ///   one side cut: how far the view reaches on the left and on the right of
    ///   where our player faces, and what stops it there.
    ///
    /// Steps: "vw-find PATTERN [N]", "vw-mark PATTERN INDEX", "vw-stage SPEC",
    /// "vw-approach LEN [SPEED]", "vw-check [DIR TAG]".
    /// </summary>
    public static partial class MapScenarioView
    {
        private static readonly
            HarmonyLib.AccessTools.FieldRef<Player, Light2D> FovLight =
            HarmonyLib.AccessTools.FieldRefAccess<Player, Light2D>("FOVLight");
        private static readonly
            HarmonyLib.AccessTools.FieldRef<Player, float> CurrentFov =
            HarmonyLib.AccessTools.FieldRefAccess<Player, float>("currentFOV");

        private static Vector3? _mark;
        private static string _markName = "";

        public static void Register()
        {
            // "vw-find PATTERN [N]": the N (8) objects whose name has PATTERN
            // nearest the centre, with what of them cuts the view.
            Add("vw-find", Find, timeout: 30f);
            // "vw-mark PATTERN INDEX": the INDEXth of those (0: the nearest) is
            // the spot the players walk to.
            Add("vw-mark", MarkStep, timeout: 30f);
            // "vw-stage SPEC": walk (quickly) to our start on the line from the
            // mark towards the centre.
            // SPEC "LEAD;host=GAP/SIDE;c1=GAP/SIDE;...": LEAD + GAP from the
            // mark, SIDE to the right of that line.
            Add("vw-stage", Stage, timeout: 180f);
            // "vw-approach LEN [SPEED]": walk LEN towards the mark at SPEED
            // (our player's run speed), watching the view.
            Add("vw-approach", Approach, timeout: 400f);
            // "vw-check [DIR TAG]": our view now, for 3 s (a screenshot
            // DIR/NAME-TAG.png with DIR).
            Add("vw-check", Check, timeout: 40f);
            // "vw-tree PATTERN [INDEX [self]]": the INDEXth spot's objects
            // (self: from the match itself, not its chunk), colliders and
            // scripted events, into the log.
            Add("vw-tree", Tree, timeout: 30f);
            // "vw-color NAME [NAME...]": the colour of each sprite named NAME
            // within 1500 of the mark (vw-mark first).
            Add("vw-color", Colors, timeout: 20f);
            // "vw-pace DIST TIMES": back and forth DIST towards and away from
            // the mark TIMES times at run speed.
            Add("vw-pace", Pace, timeout: 300f);
            // "vw-areas": every repeatable area trigger in this game: the steps
            // they run, counted by type (into the log).
            Add("vw-areas", Areas, timeout: 60f);
        }

        // ---- who we are -----------------------------------------------------

        /// <summary>
        /// "host", or "c1".."c4" for the client games (named Client1..Client4
        /// by the tests).
        /// </summary>
        private static string Me()
        {
            var s = Session.Instance;
            if (s == null || s.IsHost)
            {
                return "host";
            }

            string n = s.LocalName;
            if (n == null)
            {
                n = "";
            }

            if (n.StartsWith("Client"))
            {
                return "c" + n.Substring(6);
            }

            return n;
        }
    }
}
