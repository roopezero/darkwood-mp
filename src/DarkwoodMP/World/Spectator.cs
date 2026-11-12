using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Dying for good with others still playing. On Nightmare, and on Hard with no lives left, Darkwood's death
    /// (Player.onDeath) plays the permadeath screen (UI.PreparePermadeathVideo), which pauses the game and ends in
    /// the profile being deleted. With others in the world that would stop it for them too (the host's game runs
    /// the enemies and the clock), so instead the player who died watches the others:
    ///
    ///   the camera follows one of the players still playing (click for the next one, right-click for the one
    ///   before); the dead character goes along with it, out of sight and touching nothing, so the game shows
    ///   and runs the world around the player being watched;
    ///   the others don't see them (their snapshot says they're spectating) and enemies leave them be;
    ///   the profile is marked dead, as the game does, so leaving doesn't bring the character back;
    ///   the view is all around (see AllAround): the game's view cone stays where the character faced when it died.
    ///
    /// When nobody is left playing (the last player died, or left), the game's own permadeath screen follows.
    /// For a client that deletes only its copy of the host's world in the mod's cache (SaveRedirect).
    /// </summary>
    public static class Spectator
    {
        public static bool Active { get; private set; }

        /// <summary>Lets the game's permadeath screen through (when there's nobody left to watch).</summary>
        internal static bool AllowPermadeath;

        private static ulong _watching;
        private static readonly Dictionary<ulong, float> _lastAlive = new Dictionary<ulong, float>();
        private static Collider[] _colliders;
        private static Rigidbody _body;

        /// <summary>The view cone as it was when spectating began, put back when it ends (NaN: not changed).</summary>
        private static float _coneLight = float.NaN, _coneLogic, _currentFov;

        /// <summary>
        /// What the player sees is drawn through two lights on the player (Player.FOVLight, the visible cone, and
        /// FOVLogic, which shows and hides things in it) whose LightConeAngle Player.getFOVAngle sets every frame
        /// from currentFOV. A dead player's Update stops before that, so the cone stays at its last angle and
        /// facing, and a spectator saw only that slice around the player they watch. While spectating we open
        /// both to 360 degrees, as the game does in its cutscenes (currentFOV 370), and set currentFOV to match
        /// (Player.isInSight uses it). Called every frame while spectating; Restore puts the saved values back.
        /// </summary>
        internal static void AllAround(Player p)
        {
            if (p == null || p.FOVLogic == null) return;
            var light = Traverse.Create(p).Field<Light2D>("FOVLight").Value;
            var fov = Traverse.Create(p).Field<float>("currentFOV");
            if (float.IsNaN(_coneLight))
            {
                _coneLight = light != null ? light.LightConeAngle : p.FOVLogic.LightConeAngle;
                _coneLogic = p.FOVLogic.LightConeAngle;
                _currentFov = fov.Value;
            }
            if (light != null && light.LightConeAngle < 360f) light.LightConeAngle = 360f;
            if (p.FOVLogic.LightConeAngle < 360f) p.FOVLogic.LightConeAngle = 360f;
            fov.Value = 370f;
        }

        private static void RestoreCone()
        {
            if (float.IsNaN(_coneLight)) return;
            var p = Player.Instance;
            if (p != null && p.FOVLogic != null)
            {
                var light = Traverse.Create(p).Field<Light2D>("FOVLight").Value;
                if (light != null) light.LightConeAngle = _coneLight;
                p.FOVLogic.LightConeAngle = _coneLogic;
                Traverse.Create(p).Field<float>("currentFOV").Value = _currentFov;
            }
            _coneLight = float.NaN;
        }

        /// <summary>For the tests: the cone saved when spectating began (NaN light: none).</summary>
        internal static (float light, float logic, float current) SavedCone => (_coneLight, _coneLogic, _currentFov);

        /// <summary>For the tests: stop spectating as a world reload does.</summary>
        internal static void TestEnd() => End();

        /// <summary>For the tests: the visible cone's and the logic cone's angles, and currentFOV.</summary>
        internal static (float light, float logic, float current) ConeAngles()
        {
            var p = Player.Instance;
            if (p == null || p.FOVLogic == null) return (-1f, -1f, -1f);
            var light = Traverse.Create(p).Field<Light2D>("FOVLight").Value;
            return (light != null ? light.LightConeAngle : -1f, p.FOVLogic.LightConeAngle, Traverse.Create(p).Field<float>("currentFOV").Value);
        }

        /// <summary>
        /// Players who are still playing: alive, or not alive only for a moment (an ordinary death on Hard brings
        /// them back within seconds). A player who is spectating, or has been dead a while, isn't.
        /// </summary>
        private static List<RemotePlayer> Playing()
        {
            var list = new List<RemotePlayer>();
            var sync = PlayerSync.Instance;
            if (sync == null) return list;
            float now = Time.unscaledTime;
            foreach (var r in sync.Remotes.Values)
            {
                if (r.Owner == PlayerSync.DummyId) continue;
                if (r.Alive) _lastAlive[r.Owner] = now;
                if (r.Spectating) continue;
                if (r.Alive || (_lastAlive.TryGetValue(r.Owner, out float t) && now - t < 10f)) list.Add(r);
            }
            list.Sort((a, b) => a.Owner.CompareTo(b.Owner));
            return list;
        }

        /// <summary>Called instead of the permadeath screen: true if there's someone to watch, and we now do.</summary>
        internal static bool TryBegin()
        {
            var s = Session.Instance;
            if (AllowPermadeath || s == null || !s.Active || !s.Connected || !WorldShare.InWorld) return false;
            var playing = Playing();
            if (playing.Count == 0) return false;
            Begin(playing[0].Owner);
            return true;
        }

        /// <summary>
        /// Client: our character died for good in this world before (the host kept that, World.Characters): watch the
        /// others as soon as someone is playing (waiting up to 20 s for the others' players to come in), or, with
        /// nobody to watch, the game's permadeath screen.
        /// </summary>
        internal static void BeginDead()
        {
            if (Active) return;
            Plugin.Instance.StartCoroutine(WaitAndBegin());
        }

        private static IEnumerator WaitAndBegin()
        {
            var p = Player.Instance;
            if (p != null) p.alive = false;
            float until = Time.unscaledTime + 20f;
            while (Time.unscaledTime < until)
            {
                if (!WorldShare.InWorld || Active) yield break;
                var playing = Playing();
                if (playing.Count > 0) { Begin(playing[0].Owner); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Plugin.Log.LogInfo("Dead for good, and nobody is playing: permadeath");
            Permadeath();
        }

        private static void Begin(ulong first)
        {
            Active = true;
            _watching = first;
            if (Session.Instance.IsClient) Characters.Instance?.SendDead(); // the host keeps it with our character
            var player = Player.Instance;
            Plugin.Log.LogInfo($"Dead for good, with others still playing: watching {Session.Instance.NameOf(first)}");

            // As the game does at the start of the permadeath screen: this character is gone.
            if (Core.currentProfile != null)
            {
                Core.currentProfile.dead = true;
                try { Singleton<SaveManager>.Instance.saveGameProfiles(); } catch (System.Exception e) { Plugin.Log.LogWarning($"Couldn't mark the profile dead: {e.Message}"); }
            }

            // Undo what dying did to the screen and sound, and let the world go on.
            var ui = Singleton<UI>.Instance;
            ui.tweenBlackScreenTop(new Color(0f, 0f, 0f, 0f), 2f);
            Singleton<Controller>.Instance.fadeAudio(fadeOut: false, 3f, musicToo: true);
            if (Session.Instance.IsHost) Singleton<Controller>.Instance.DoUpdateTime = true;
            Core.forbidInputs = true;

            // The dead character follows the camera, touching nothing.
            if (player != null)
            {
                _colliders = player.GetComponentsInChildren<Collider>();
                foreach (var c in _colliders) if (c != null) c.enabled = false;
            }
            _body = player != null ? player.GetComponent<Rigidbody>() : null;
            Plugin.Notice("You died. Watching the others: click to switch.");
            Plugin.Instance.StartCoroutine(Run());
        }

        private static IEnumerator Run()
        {
            float nobodySince = -1f;
            while (Active)
            {
                var s = Session.Instance;
                bool inSession = s != null && s.Active && s.Connected && WorldShare.InWorld;
                var playing = inSession ? Playing() : new List<RemotePlayer>();
                if (playing.Count == 0)
                {
                    if (!WorldShare.InWorld) { End(); yield break; } // the world is being replaced (a reload): it decides again
                    if (!inSession) { Plugin.Log.LogInfo("The session ended while watching: permadeath"); End(); Permadeath(); yield break; }
                    if (nobodySince < 0f) nobodySince = Time.unscaledTime;
                    if (Time.unscaledTime - nobodySince > 3f) { Plugin.Log.LogInfo("Nobody is left playing: permadeath"); End(); Permadeath(); yield break; }
                    yield return null;
                    continue;
                }
                nobodySince = -1f;

                int i = playing.FindIndex(r => r.Owner == _watching);
                if (i < 0) i = 0;
                if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space)) i = (i + 1) % playing.Count;
                else if (Input.GetMouseButtonDown(1)) i = (i + playing.Count - 1) % playing.Count;
                _watching = playing[i].Owner;

                var target = playing[i].Root;
                var player = Player.Instance;
                var cam = Singleton<CamMain>.Instance;
                if (target != null && player != null)
                {
                    // Out of sight below the ground, as the game puts a dead player; the world loads around it.
                    var at = target.transform.position - new Vector3(0f, 10f, 0f);
                    player._transform.position = at;
                    if (_body != null) _body.position = at;
                    if (cam != null) cam.followTarget = target.transform;
                }
                AllAround(player);
                yield return null;
            }
        }

        private static void End()
        {
            Active = false;
            var cam = Singleton<CamMain>.Instance;
            if (cam != null) cam.followTarget = null;
            RestoreCone();
        }

        private static void Permadeath()
        {
            AllowPermadeath = true;
            var ui = Singleton<UI>.Instance;
            if (ui != null) ui.StartCoroutine(ui.PreparePermadeathVideo());
        }

        /// <summary>A new world: nothing to watch.</summary>
        public static void Reset()
        {
            _coneLight = float.NaN; // a living player's getFOVAngle sets the cone again every frame
            Active = false;
            AllowPermadeath = false;
            _lastAlive.Clear();
            _colliders = null;
            _body = null;
        }

        /// <summary>The spectating line at the bottom of the screen.</summary>
        public static void DrawGui()
        {
            if (!Active) return;
            string name = Session.Instance?.NameOf(_watching) ?? "?";
            var rect = new Rect(0, Screen.height - 70, Screen.width, 30);
            GUI.Label(rect, $"You're dead. Watching {name}. Click for the next player, right-click for the one before.", Plugin.NameTagStyle);
        }
    }

    /// <summary>The permadeath screen, unless there are others to watch (see Spectator).</summary>
    [HarmonyPatch(typeof(UI), nameof(UI.PreparePermadeathVideo))]
    static class PermadeathPatch
    {
        static bool Prefix(ref IEnumerator __result)
        {
            if (!Spectator.TryBegin()) return true;
            __result = Nothing();
            return false;
        }

        private static IEnumerator Nothing() { yield break; }
    }
}
