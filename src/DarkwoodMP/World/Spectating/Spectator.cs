using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.Menus;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using DarkwoodMP.Sync.Players;
using HarmonyLib;
using UnityEngine;

namespace DarkwoodMP.World.Spectating
{
    /// <summary>
    /// Dying for good with others still playing. On Nightmare, and on Hard with
    /// no lives left, Darkwood's death (Player.onDeath) plays the permadeath
    /// screen (UI.PreparePermadeathVideo), which pauses the game and ends in
    /// the profile being deleted. With others in the world that would stop it
    /// for them too (the host's game runs the enemies and the clock), so
    /// instead the player who died watches the others:
    ///
    ///   the camera follows one of the players still playing (click for the
    ///   next one, right-click for the one before); the dead character goes
    ///   along with it, out of sight and touching nothing, so the game shows
    ///   and runs the world around the player being watched;
    ///   the others don't see them (their snapshot says they're spectating) and
    ///   enemies leave them be;
    ///   the profile is marked dead, as the game does, so leaving doesn't bring
    ///   the character back;
    ///   the view is all around (see AllAround): the game's view cone stays
    ///   where the character faced when it died.
    ///
    /// When nobody is left playing (the last player died, or left), the game's
    /// own permadeath screen follows.
    ///
    /// The epilogue's burning forest uses the watching alone (BeginWatch): a
    /// player who crawled to their death there watches the ones still crawling
    /// until the camera pans over the forest (World.EpilogueSync). Nothing is
    /// marked dead and no permadeath follows; with nobody left to watch the
    /// camera stays on the body.
    /// For a client that deletes only its copy of the host's world in the mod's
    /// cache (SaveRedirect).
    /// </summary>
    internal sealed class Spectator
    {
        private readonly CoopSession _coop;

        public Spectator(CoopSession coop)
        {
            _coop = coop;
            // the session's end ends the epilogue's watching, as
            // EpilogueSync's cleanup did
            coop.Hook(EndWatch);
        }

        public bool Active { get; private set; }

        /// <summary>
        /// Watching in the epilogue (BeginWatch): no permadeath, the profile
        /// isn't touched.
        /// </summary>
        public bool Epilogue { get; private set; }

        /// <summary>
        /// Lets the game's permadeath screen through (when there's nobody left
        /// to watch).
        /// </summary>
        private bool _allowPermadeath;

        private ulong _watching;
        private readonly Roster _roster = new Roster();
        private Rigidbody _body;

        private static readonly AccessTools.FieldRef<Player, Light2D> FovLight =
            AccessTools.FieldRefAccess<Player, Light2D>("FOVLight");
        private static readonly AccessTools.FieldRef<Player, float> CurrentFov =
            AccessTools.FieldRefAccess<Player, float>("currentFOV");

        /// <summary>
        /// The view cone as it was when spectating began, put back when it ends
        /// (NaN: not changed).
        /// </summary>
        private float _coneLight = float.NaN, _coneLogic, _currentFov;

        /// <summary>
        /// What the player sees is drawn through two lights on the player
        /// (Player.FOVLight, the visible cone, and FOVLogic, which shows and
        /// hides things in it) whose LightConeAngle Player.getFOVAngle sets
        /// every frame from currentFOV. A dead player's Update stops before
        /// that, so the cone stays at its last angle and facing, and a
        /// spectator saw only that slice around the player they watch. While
        /// spectating we open both to 360 degrees, as the game does in its
        /// cutscenes (currentFOV 370), and set currentFOV to match
        /// (Player.isInSight uses it). Called every frame while spectating;
        /// Restore puts the saved values back.
        /// </summary>
        internal void AllAround(Player p)
        {
            if (p == null || p.FOVLogic == null)
            {
                return;
            }

            var light = FovLight(p);
            if (float.IsNaN(_coneLight))
            {
                if (light != null)
                {
                    _coneLight = light.LightConeAngle;
                }
                else
                {
                    _coneLight = p.FOVLogic.LightConeAngle;
                }

                _coneLogic = p.FOVLogic.LightConeAngle;
                _currentFov = CurrentFov(p);
            }

            if (light != null && light.LightConeAngle < 360f)
            {
                light.LightConeAngle = 360f;
            }

            if (p.FOVLogic.LightConeAngle < 360f)
            {
                p.FOVLogic.LightConeAngle = 360f;
            }

            CurrentFov(p) = 370f;
        }

        private void RestoreCone()
        {
            if (float.IsNaN(_coneLight))
            {
                return;
            }

            var p = Player.Instance;
            if (p != null && p.FOVLogic != null)
            {
                var light = FovLight(p);
                if (light != null)
                {
                    light.LightConeAngle = _coneLight;
                }

                p.FOVLogic.LightConeAngle = _coneLogic;
                CurrentFov(p) = _currentFov;
            }

            _coneLight = float.NaN;
        }

        /// <summary>
        /// For the tests: the cone saved when spectating began (NaN light:
        /// none).
        /// </summary>
        internal (float light, float logic, float current) SavedCone =>
            (_coneLight, _coneLogic, _currentFov);

        /// <summary>
        /// For the tests: stop spectating as a world reload does.
        /// </summary>
        internal void TestEnd() => End();

        /// <summary>
        /// For the tests: the visible cone's and the logic cone's angles, and
        /// currentFOV.
        /// </summary>
        internal (float light, float logic, float current) ConeAngles()
        {
            var p = Player.Instance;
            if (p == null || p.FOVLogic == null)
            {
                return (-1f, -1f, -1f);
            }

            var light = FovLight(p);
            float lightConeAngle = -1f;
            if (light != null)
            {
                lightConeAngle = light.LightConeAngle;
            }

            return (lightConeAngle, p.FOVLogic.LightConeAngle, CurrentFov(p));
        }

        /// <summary>
        /// Players who are still playing: alive, or not alive only for a moment
        /// (an ordinary death on Hard brings them back within seconds). A
        /// player who is spectating, or has been dead a while, isn't.
        /// </summary>
        private List<RemotePlayer> Playing()
        {
            var list = new List<RemotePlayer>();
            var sync = Plugin.Instance.Host.Players;
            if (sync == null)
            {
                return list;
            }

            var remotes = new List<Watched>();
            foreach (var r in sync.Remotes.Values)
            {
                remotes.Add(WatchedOf(r));
            }

            var now = new Seconds(Time.unscaledTime);
            foreach (ulong owner in _roster.Playing(remotes, now))
            {
                list.Add(sync.Remotes[owner]);
            }

            return list;
        }

        /// The only code reading a remote player for the rules.
        private Watched WatchedOf(RemotePlayer r)
        {
            return new Watched
            {
                Owner = r.Owner,
                Dummy = r.Owner == PlayerSync.DummyId,
                Alive = r.Alive,
                Spectating = r.Spectating,
            };
        }

        /// <summary>
        /// Called instead of the permadeath screen: true if there's someone to
        /// watch, and we now do.
        /// </summary>
        internal bool TryBegin()
        {
            var s = _coop.Net;
            bool inSession = s != null && s.Active && s.Connected;
            var playing = new List<RemotePlayer>();
            if (!_allowPermadeath && inSession && WorldShare.InWorld)
            {
                playing = Playing();
            }

            var facts = new BeginFacts
            {
                AllowPermadeath = _allowPermadeath,
                InSession = inSession,
                InWorld = WorldShare.InWorld,
                Playing = playing.Count,
            };

            if (!SpectatorRules.MayBegin(facts))
            {
                return false;
            }

            Begin(playing[0].Owner);
            return true;
        }

        /// <summary>
        /// Client: our character died for good in this world before (the host
        /// kept that, World.Characters): watch the others as soon as someone is
        /// playing (waiting up to 20 s for the others' players to come in), or,
        /// with nobody to watch, the game's permadeath screen.
        /// </summary>
        internal void BeginDead()
        {
            if (Active)
            {
                return;
            }

            Plugin.Instance.StartCoroutine(WaitAndBegin());
        }

        private IEnumerator WaitAndBegin()
        {
            var p = Player.Instance;
            if (p != null)
            {
                p.alive = false;
            }

            var until =
                new Seconds(Time.unscaledTime + SpectatorRules.DeadWait);
            while (true)
            {
                var playing = new List<RemotePlayer>();
                var now = new Seconds(Time.unscaledTime);
                if (now < until && WorldShare.InWorld && !Active)
                {
                    playing = Playing();
                }

                var facts = new DeadWaitFacts
                {
                    Now = now,
                    Until = until,
                    InWorld = WorldShare.InWorld,
                    Active = Active,
                    Playing = playing.Count,
                };

                DeadWaitStep step = SpectatorRules.DeadWaits(facts);
                if (step == DeadWaitStep.Permadeath)
                {
                    break;
                }

                if (step == DeadWaitStep.Stops)
                {
                    yield break;
                }

                if (step == DeadWaitStep.Begins)
                {
                    Begin(playing[0].Owner);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            Plugin.Log.LogInfo(
                "Dead for good, and nobody is playing: permadeath");
            Permadeath();
        }

        /// <summary>
        /// The epilogue: watch the players still playing, if any (true),
        /// without dying for good.
        /// </summary>
        internal bool BeginWatch()
        {
            var s = _coop.Net;
            bool session = s != null && s.Active;
            var playing = new List<RemotePlayer>();
            if (!Active && session)
            {
                playing = Playing();
            }

            if (!SpectatorRules.MayWatch(Active, session, playing.Count))
            {
                return false;
            }

            Epilogue = true;
            Active = true;
            _watching = playing[0].Owner;
            Plugin.Log.LogInfo(
                $"[epilogue] watching {s.NameOf(_watching)} until the others "
                    + "have crawled too");
            var player = Player.Instance;
            if (player != null)
            {
                foreach (var c in player.GetComponentsInChildren<Collider>())
                {
                    if (c != null)
                    {
                        c.enabled = false;
                    }
                }
            }

            if (player != null)
            {
                _body = player.GetComponent<Rigidbody>();
            }
            else
            {
                _body = null;
            }

            Plugin.Instance.StartCoroutine(Run());
            return true;
        }

        /// <summary>
        /// The epilogue's watching ends (the pan): the camera is the caller's
        /// again.
        /// </summary>
        internal void EndWatch()
        {
            if (!Epilogue)
            {
                return;
            }

            End();
        }

        private void Begin(ulong first)
        {
            Epilogue = false;
            Active = true;
            _watching = first;
            if (_coop.Net.IsClient)
            {
                // the host keeps it with our character
                Characters instance = Characters.Instance;
                if (instance != null)
                {
                    instance.SendDead();
                }
            }

            var player = Player.Instance;
            Plugin.Log.LogInfo(
                "Dead for good, with others still playing: watching "
                    + $"{_coop.Net.NameOf(first)}");

            // As the game does at the start of the permadeath screen: this
            // character is gone.
            if (Core.currentProfile != null)
            {
                Core.currentProfile.dead = true;
                try
                {
                    Singleton<SaveManager>.Instance.saveGameProfiles();
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't mark the profile dead: {e.Message}");
                }
            }

            // Undo what dying did to the screen and sound, and let the world go
            // on.
            var ui = Singleton<UI>.Instance;
            ui.tweenBlackScreenTop(new Color(0f, 0f, 0f, 0f), 2f);
            Singleton<Controller>.Instance.fadeAudio(
                fadeOut: false, 3f, musicToo: true);
            if (_coop.Net.IsHost)
            {
                Singleton<Controller>.Instance.DoUpdateTime = true;
            }

            Core.forbidInputs = true;

            // The dead character follows the camera, touching nothing.
            if (player != null)
            {
                foreach (var c in player.GetComponentsInChildren<Collider>())
                {
                    if (c != null)
                    {
                        c.enabled = false;
                    }
                }
            }

            if (player != null)
            {
                _body = player.GetComponent<Rigidbody>();
            }
            else
            {
                _body = null;
            }

            Plugin.Notice("You died. Watching the others: click to switch.");
            Plugin.Instance.StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            bool nobody = false;
            var nobodySince = new Seconds(0f);
            while (Active)
            {
                var s = _coop.Net;
                bool inSession =
                    s != null && s.Active && s.Connected && WorldShare.InWorld;
                List<RemotePlayer> playing;
                if (inSession)
                {
                    playing = Playing();
                }
                else
                {
                    playing = new List<RemotePlayer>();
                }

                var facts = new WatchFacts
                {
                    Epilogue = Epilogue,
                    InSession = inSession,
                    InWorld = WorldShare.InWorld,
                    Playing = playing.Count,
                    Nobody = nobody,
                    NobodySince = nobodySince,
                    Now = new Seconds(Time.unscaledTime),
                };

                WatchStep step = SpectatorRules.Watch(facts);
                nobody = step.Nobody;
                nobodySince = step.NobodySince;
                if (step.Action == WatchAction.Stay
                    || step.Action == WatchAction.Waits)
                {
                    // the epilogue: nobody else crawling, stay put until the
                    // pan (EndWatch); else nobody for now
                    yield return null;
                    continue;
                }

                if (step.Action == WatchAction.Ended)
                {
                    // the world is being replaced (a reload): it decides again
                    End();
                    yield break;
                }

                if (step.Action == WatchAction.SessionLost)
                {
                    Plugin.Log.LogInfo(
                        "The session ended while watching: permadeath");
                    End();
                    Permadeath();
                    yield break;
                }

                if (step.Action == WatchAction.NobodyLeft)
                {
                    Plugin.Log.LogInfo("Nobody is left playing: permadeath");
                    End();
                    Permadeath();
                    yield break;
                }

                int found = playing.FindIndex(r => r.Owner == _watching);
                int i = SpectatorRules.Pick(found, playing.Count, ChoiceOf());
                _watching = playing[i].Owner;

                var target = playing[i].Root;
                var player = Player.Instance;
                var cam = Singleton<CamMain>.Instance;
                if (target != null && player != null)
                {
                    // Out of sight below the ground, as the game puts a dead
                    // player; the world loads around it.
                    var at =
                        target.transform.position - new Vector3(0f, 10f, 0f);
                    player._transform.position = at;
                    if (_body != null)
                    {
                        _body.position = at;
                    }

                    if (cam != null)
                    {
                        cam.followTarget = target.transform;
                    }
                }

                AllAround(player);
                yield return null;
            }
        }

        /// The only code reading the player's input while watching.
        private Choice ChoiceOf()
        {
            if (Input.GetMouseButtonDown(0)
                || Input.GetKeyDown(KeyCode.Space))
            {
                return Choice.Next;
            }

            if (Input.GetMouseButtonDown(1))
            {
                return Choice.Previous;
            }

            return Choice.None;
        }

        private void End()
        {
            Active = false;
            Epilogue = false;
            var cam = Singleton<CamMain>.Instance;
            if (cam != null)
            {
                cam.followTarget = null;
            }

            RestoreCone();
        }

        private void Permadeath()
        {
            _allowPermadeath = true;
            var ui = Singleton<UI>.Instance;
            if (ui != null)
            {
                ui.StartCoroutine(ui.PreparePermadeathVideo());
            }
        }

        /// <summary>A new world: nothing to watch.</summary>
        public void Reset()
        {
            // a living player's getFOVAngle sets the cone again every frame
            _coneLight = float.NaN;
            Active = false;
            _allowPermadeath = false;
            Epilogue = false;
            _roster.Reset();
            _body = null;
        }

        /// <summary>The spectating line at the bottom of the screen.</summary>
        public void DrawGui()
        {
            if (!Active || UiState.MenuOver)
            {
                return; // not over the pause menu or the mod's windows
            }

            Session instance = _coop.Net;
            string name = null;
            if (instance != null)
            {
                name = instance.NameOf(_watching);
            }

            if (name == null)
            {
                name = "?";
            }

            var rect = new Rect(0, Screen.height - 70, Screen.width, 30);
            if (Epilogue)
            {
                GUI.Label(
                    rect, $"Watching {name}. Click for the next player.",
                    Plugin.NameTagStyle);
                return;
            }

            GUI.Label(
                rect,
                $"You're dead. Watching {name}. Click for the next player, "
                    + "right-click for the one before.",
                Plugin.NameTagStyle);
        }
    }
}
