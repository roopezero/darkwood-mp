using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Dreaming
{
    /// DreamSync on a client: following the host into its dream, and the
    /// tests' own dreams. The host follows a joiner into a story dream the
    /// same way (DreamSync.Story).
    internal sealed partial class DreamSync
    {
        private void OnStart(ulong from, PacketReader r)
        {
            string name = DreamStart.Read(r);
            if (S.IsHost)
            {
                OnStoryAsked(from, name);
                return;
            }

            var d = Singleton<Dreams>.Instance;
            var share = ShareOf() with
            {
                FromHost = from == S.HostId,
                SameDream = PresetOf(d) == name,
            };

            StartStep step = DreamRules.Start(DreamOf(d), share);
            if (step == StartStep.Drop)
            {
                return;
            }

            if (step == StartStep.Adopt)
            {
                Adopt(name);
                return;
            }

            if (step == StartStep.AlreadyIn)
            {
                Plugin.Log.LogInfo(
                    $"[dream] the host's level dream {name}: we're in a dream "
                        + "already");
                return;
            }

            _pendingStart = name;
            _pendingEnd = null;
            _startBy = DreamRules.StartBy(Now);
            _startAt = new Seconds(0f);
            Plugin.Log.LogInfo(
                $"[dream] the host's level dream {name}: we go in");
        }

        private void CheckInputs(Dreams d)
        {
            var facts = new InputFacts
            {
                Now = Now,
                From = _inputsFrom,
                InWorld = WorldShare.InWorld,
                ForbidInputs = Core.forbidInputs,
            };

            bool checks = _inputsFrom.Value > 0f && facts.InWorld;
            if (checks && facts.ForbidInputs)
            {
                facts = facts with { HeldByNothing = InputsHeldByNothing(d) };
            }

            InputStep step = DreamRules.Inputs(facts);
            if (step == InputStep.NotYet || step == InputStep.Wait)
            {
                return;
            }

            _inputsFrom = new Seconds(0f);
            if (step == InputStep.Stop)
            {
                return;
            }

            Core.forbidInputs = false;
            Plugin.Log.LogInfo(
                "[dream] the shared dream's end left our player held: let go");
        }

        /// <summary>
        /// Nothing of the game's own holds the player now (Core.forbidInputs
        /// is left over, if set).
        /// </summary>
        internal static bool InputsHeldByNothing(Dreams d)
        {
            return DreamRules.HeldByNothing(HoldOf(d));
        }

        private static HoldFacts HoldOf(Dreams d)
        {
            var ctl = Singleton<Controller>.Instance;
            var o = Singleton<OutsideLocations>.Instance;
            var p = Player.Instance;
            return new HoldFacts
            {
                HasPlayer = p != null,
                Locked = Core.cantChangeForbidInputs,
                LoadingGame = Core.loadingGame,
                Cutscene = ctl != null && ctl.playingCutscene,
                OutsideLoading = o != null && o.loading,
                Switching = d != null && d.switchingDream,
                EnteringDream = Core.EnteringDream,
                InDialogue = p != null && p.inDialogue,
            };
        }

        /// <summary>
        /// Call every frame: a client starts the host's dream once its own
        /// menus let it, and ends it if the host's already did.
        /// </summary>
        public void Update()
        {
            var d = Singleton<Dreams>.Instance;
            CheckInputs(d);
            DreamFacts dream = DreamOf(d);
            PendingEnd end =
                DreamRules.OnPendingEnd(dream, _pendingEnd != null, _ended);
            if (end != PendingEnd.Keep)
            {
                string o = _pendingEnd;
                _pendingEnd = null;
                if (end == PendingEnd.End)
                {
                    Plugin.Log.LogInfo(
                        $"[dream] the host's dream had ended ({o}): ours ends");
                    EndHere(d, o);
                }
            }

            if (_pendingStart == null)
            {
                return;
            }

            Join(d);
        }

        private void Join(Dreams d)
        {
            JoinStep step = DreamRules.Join(JoinOf(d));
            _startAt = step.StartAt;
            if (step.Action == JoinAction.Drop)
            {
                _pendingStart = null;
                _hostFollows = false;
                return;
            }

            if (step.Action == JoinAction.GiveUp)
            {
                Plugin.Log.LogWarning(
                    "[dream] couldn't go into the shared dream "
                        + $"{_pendingStart} (busy)");
                _pendingStart = null;
                _hostFollows = false;
                return;
            }

            if (step.Action == JoinAction.Stove)
            {
                var menu = Singleton<UI>.Instance.LevelingMenu;
                if (menu.openedAndTweened)
                {
                    menu.hide(forceHide: true);
                }

                return;
            }

            if (step.Action == JoinAction.Go)
            {
                Go(d);
            }
        }

        private JoinFacts JoinOf(Dreams d)
        {
            var p = Player.Instance;
            var facts = new JoinFacts
            {
                ClientMode = NetEnemies.ClientMode,
                HostFollows = _hostFollows,
                InWorld = WorldShare.InWorld,
                HasDreams = d != null,
                HasPlayer = p != null,
                Now = Now,
                StartBy = _startBy,
                StartAt = _startAt,
            };

            bool follows = facts.ClientMode || facts.HostFollows;
            if (!follows || !facts.InWorld || d == null || p == null)
            {
                return facts;
            }

            facts = facts with { InDream = DreamRules.InDream(DreamOf(d)) };
            if (facts.InDream || facts.Now > facts.StartBy)
            {
                return facts;
            }

            var skills = Singleton<SkillsMenu>.Instance;
            facts = facts with
            {
                SkillsMenuOpen = skills != null && skills.opened,
                InDialogue = p.inDialogue,
            };

            if (facts.SkillsMenuOpen || facts.InDialogue)
            {
                return facts;
            }

            var menu = Singleton<UI>.Instance.LevelingMenu;
            return facts with { AtStove = p.inLevelingMenu && menu != null };
        }

        private void Go(Dreams d)
        {
            string name = _pendingStart;
            _pendingStart = null;
            _hostFollows = false;
            try
            {
                // as the host's draw took it
                d.presetList.RemoveAll(x => x != null && x.name == name);
                Shared = name;
                _ended = false;
                GoInto(d, name);
            }
            catch (System.Exception e)
            {
                Shared = null;
                Plugin.Log.LogError($"[dream] couldn't go into {name}: {e}");
            }
        }

        /// <summary>
        /// Into the dream through the game's transition, as
        /// SkillsMenu.confirmSkills goes into a level dream.
        /// </summary>
        private static void GoInto(Dreams d, string name)
        {
            d.wantToDream = true;
            d.startTransition.dreamToTransitionTo = name;
            Core.forbidInputs = true;
            if (Singleton<CamMain>.Instance != null)
            {
                Singleton<CamMain>.Instance.gameObject.SetActive(true);
            }

            d.startTransition.transition();
        }

        /// <summary>
        /// For tests (client): the host's dream we go into once our menus let
        /// us (null: none).
        /// </summary>
        internal string Pending => _pendingStart;

        /// <summary>
        /// For tests (client): go into a dream of our own (as a story step's
        /// startDream does), not shared.
        /// </summary>
        internal static void TestOwnDream(string name)
        {
            GoInto(Singleton<Dreams>.Instance, name);
        }

        /// <summary>
        /// For tests (host): start a level dream as SkillsMenu.confirmSkills
        /// does after a level.
        /// </summary>
        internal void TestLevelDream(string name)
        {
            var d = Singleton<Dreams>.Instance;
            GoInto(d, name);
            _hostLevelDream = true;
            DrawNow(d);
        }
    }
}
