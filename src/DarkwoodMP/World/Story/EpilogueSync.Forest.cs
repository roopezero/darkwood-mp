using System;
using System.Collections;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Story
{
    /// <summary>
    /// The epilogue's burning forest: the crawl to death, the pan over the
    /// forest, the outcomes, and the fire that chooses that ending.
    /// </summary>
    internal sealed partial class EpilogueSync
    {
        /// <summary>
        /// Events.fireWorldEvent: the game's own pan over the burning forest
        /// (16 s after its player crawled to their death) waits in co-op until
        /// the host says (EpilogueStep 1). False holds it back.
        /// </summary>
        internal bool WorldEventFired(string type)
        {
            return PanMayRun(type);
        }

        /// <summary>
        /// Player.special_petDog, the flat's dog petted: our player stands
        /// still until the body's PetDog clip ends, so it starts only if the
        /// body has that clip (EpilogueRules.MayPetDog). False skips it.
        /// </summary>
        internal bool MayPetDog(Player p)
        {
            bool hasClip = false;
            if (p != null && p.torsoAnimator != null
                && p.torsoAnimator.Library != null)
            {
                hasClip = p.torsoAnimator.Library.GetClipByName(
                    EpilogueRules.PetDogClip) != null;
            }

            bool pets = EpilogueRules.MayPetDog(S != null && S.Active, hasClip);
            if (!pets)
            {
                Plugin.Log.LogInfo(
                    "[epilogue] our player's body has no "
                        + $"{EpilogueRules.PetDogClip} clip: not petting the "
                        + "dog, which would hold them still for good");
            }

            return pets;
        }

        private bool PanMayRun(string type)
        {
            var facts = new PanEventFacts
            {
                Playing = Playing,
                IsPan = type == PanEvent,
                InSession = S != null && S.Active,
                Released = _panGo,
                PanRan = _ownPan,
            };

            switch (EpilogueRules.WorldEvent(facts))
            {
                case PanEventStep.Held:
                    // once: ours (RunPan) or the game's own, whichever comes
                    // first
                    return false;
                case PanEventStep.RunsFirst:
                    _ownPan = true;
                    return true;
                case PanEventStep.Waits:
                    Plugin.Log.LogInfo(
                        "[epilogue] our pan over the burning forest waits "
                            + "for the others");
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// Player.die has run: if that was the second death of the crawl in the
        /// burning forest, tell the host.
        /// </summary>
        internal void AfterDie(Player p)
        {
            var s = S;
            if (!EpilogueRules.CountsDeath(DeathOf(p)))
            {
                return;
            }

            _ownDeath = true;
            _deathAt = p.transform.position;
            Plugin.Log.LogInfo(
                "[epilogue] our player crawled to their death in the burning "
                    + "forest");
            Plugin.Instance.StartCoroutine(WatchSoon());
            if (s.IsHost)
            {
                Crawled(s.LocalId);
                return;
            }

            var w = s.Begin(MsgType.EpilogueStep);
            EpilogueStepMessage.WriteCrawled(w);
            s.SendToHost(w, Channel.Control, reliable: true);
        }

        /// <summary>
        /// The only code reading the game's Player for the death rule.
        /// </summary>
        private DeathFacts DeathOf(Player p)
        {
            bool has = p != null;
            return new DeathFacts
            {
                Playing = Playing,
                Counted = _ownDeath,
                InSession = S != null && S.Active,
                HasPlayer = has,
                InEpilogue = has && p.inEpilogue,
                Crawling = has && p.crawling,
                Dying = has && p.dying,
            };
        }

        /// <summary>
        /// After the game's own 8 s on the body, watch the players still
        /// crawling, until the pan.
        /// </summary>
        private IEnumerator WatchSoon()
        {
            yield return new WaitForSeconds(8.5f);
            Spectating.Spectator spectator = _coop.Spectator;
            if (EpilogueRules.WatchesSoon(Playing, _panGo, spectator.Active))
            {
                spectator.BeginWatch();
            }
        }

        /// <summary>Host: this player crawled to their death.</summary>
        private void Crawled(ulong who)
        {
            CrawlStep step = _crawled.Crawled(who);
            if (step == CrawlStep.Repeat)
            {
                return;
            }

            if (step == CrawlStep.First)
            {
                Plugin.Instance.StartCoroutine(PanTimeout());
            }

            CheckPan();
        }

        private IEnumerator PanTimeout()
        {
            yield return new WaitForSecondsRealtime(EpilogueRules.PanWait);
            if (EpilogueRules.PanTimesOut(Playing, _panGo))
            {
                Plugin.Log.LogInfo(
                    $"[epilogue] {EpilogueRules.PanWait:0} s since the first "
                        + "death in the burning forest: the pan runs for "
                        + "everyone");
                ReleasePan();
            }
        }

        /// <summary>
        /// Host: once every player has crawled to their death, the pan, in
        /// every game.
        /// </summary>
        private void CheckPan()
        {
            var s = S;
            if (s == null)
            {
                return;
            }

            var facts = new PanFacts
            {
                Released = _panGo,
                Playing = Playing,
                IsHost = s.IsHost,
                Host = s.LocalId,
                Peers = s.Peers,
            };

            if (!_crawled.ReleasesPan(facts))
            {
                return;
            }

            Plugin.Log.LogInfo(
                "[epilogue] every player has crawled to their death: the pan "
                    + "runs for everyone");
            ReleasePan();
        }

        private void ReleasePan()
        {
            var s = S;
            var w = s.Begin(MsgType.EpilogueStep);
            EpilogueStepMessage.WritePan(w);
            s.Broadcast(w, Channel.Control, reliable: true);
            // the pan belongs to the burned forest: its outcomes follow
            SendOutcomes(true);
            RunPan();
        }

        /// <summary>
        /// The game's pan, as Player.die does it after its first 8 s: the
        /// camera drifts away from where our player died over 30 s, and 8 s in,
        /// the world event shows the burning forest and then the outcomes.
        /// </summary>
        private void RunPan()
        {
            if (_panGo)
            {
                return;
            }

            _panGo = true;
            _coop.Spectator.EndWatch();

            var p = Player.Instance;
            if (p == null || !p.inEpilogue)
            {
                return;
            }

            Core.forbidInputs = true;
            Vector3 from;
            if (_ownDeath)
            {
                from = _deathAt;
            }
            else
            {
                from = p.transform.position;
            }

            if (_ownDeath)
            {
                // where the game keeps a dead player
                p._transform.position = from - new Vector3(0f, 10f, 0f);
            }

            Plugin.Instance.StartCoroutine(Pan(from));
        }

        private IEnumerator Pan(Vector3 from)
        {
            var follow = new GameObject("camera follow");
            follow.transform.position = from;
            var cam = Singleton<CamMain>.Instance;
            if (cam != null)
            {
                cam.followTarget = follow.transform;
            }

            float t = 0f;
            bool fired = false;
            while (t < 30f && follow != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / 30f);
                // ease in and out
                follow.transform.position = from - new Vector3(
                    4000f * (0.5f - 0.5f * Mathf.Cos(k * Mathf.PI)), 0f, 0f);
                if (!fired && t >= 8f)
                {
                    fired = true;
                    if (!_ownPan)
                    {
                        try
                        {
                            Singleton<Events>.Instance.fireWorldEvent(PanEvent);
                        }
                        catch (Exception e)
                        {
                            Plugin.Log.LogWarning(
                                "[epilogue] couldn't run the pan: "
                                    + $"{e.Message}");
                        }
                    }
                }

                yield return null;
            }
        }

        /// <summary>
        /// Host: our outcomes (the forest burned or not) go to the joiners,
        /// whose screens use them.
        /// </summary>
        internal void SendOutcomes(bool burned)
        {
            var s = S;
            if (s == null
                || !EpilogueRules.SendsOutcomes(
                    Playing, s.IsHost, s.Peers.Count))
            {
                return;
            }

            var w = s.Begin(MsgType.EpilogueStep);
            EpilogueStepMessage.WriteOutcomes(w, burned);
            s.Broadcast(w, Channel.Control, reliable: true);
        }

        /// <summary>
        /// EpilogueOutcomes.Start: the host sends its outcomes; a joiner's
        /// screen takes the host's. New joiners are refused from now on.
        /// </summary>
        internal void OutcomesStart(EpilogueOutcomes o)
        {
            _atEnd = true;
            switch (EpilogueRules.OutcomesStart(OutcomesOf(o)))
            {
                case OutcomeStep.Send:
                    SendOutcomes(o.burnedForest);
                    return;
                case OutcomeStep.TakeHost:
                    bool hosts = _hostBurned == HostBurned.Yes;
                    Plugin.Log.LogWarning(
                        "[epilogue] our outcomes screen had burnedForest "
                            + $"{o.burnedForest}, the host's {hosts}: using "
                            + "the host's");
                    o.burnedForest = hosts;
                    return;
                default:
                    return;
            }
        }

        /// <summary>
        /// The only code reading the game's EpilogueOutcomes for the rules.
        /// </summary>
        private OutcomeFacts OutcomesOf(EpilogueOutcomes o)
        {
            var s = S;
            bool session = s != null && s.Active;
            return new OutcomeFacts
            {
                Exists = o != null,
                Playing = Playing,
                InSession = session,
                IsHost = session && s.IsHost,
                Host = _hostBurned,
                Burned = o != null && o.burnedForest,
            };
        }

        // ---- the ending's choice --------------------------------------------

        /// <summary>
        /// GameEvents.fire: the crater's events that set the Being on fire ran
        /// in our game (our player burned it). That chooses the burned-forest
        /// ending for everyone: the others' games run the same events, so
        /// they're shut in the fire too. (The other ending, going back to the
        /// room and lying down to sleep, is a dream's end and moves everyone on
        /// through the shared dream.)
        /// </summary>
        internal void EventsFired(GameEvents ge)
        {
            var s = S;
            string name = null;
            if (ge != null)
            {
                name = ge.name;
            }

            var facts = new BurnShareFacts
            {
                Playing = Playing,
                Shared = _burnShared,
                Name = name,
                Replaying = _coop.Guards.Replaying,
                InSession = s != null && s.Active,
            };

            if (!EpilogueRules.SharesBurn(facts))
            {
                return;
            }

            _burnShared = true;
            Plugin.Log.LogInfo(
                "[epilogue] our player set the Being on fire: the forest burns "
                    + "for everyone");
            var w = s.Begin(MsgType.EpilogueStep);
            if (s.IsHost)
            {
                EpilogueStepMessage.WriteBurnBy(w, s.LocalId);
                s.Broadcast(w, Channel.Control, reliable: true);
            }
            else
            {
                EpilogueStepMessage.WriteBurn(w);
                s.SendToHost(w, Channel.Control, reliable: true);
            }
        }

        /// <summary>
        /// Someone else set the Being on fire: run the same events here (the
        /// host passes it on first).
        /// </summary>
        private void Burned(ulong who)
        {
            var s = S;
            if (s == null)
            {
                return;
            }

            GameEvents found = BurnEventsHere();
            BurnStep step = EpilogueRules.Burned(BurnOf(found));
            if (step == BurnStep.Ignored)
            {
                return;
            }

            _burnShared = true;
            if (s.IsHost)
            {
                var w = s.Begin(MsgType.EpilogueStep);
                EpilogueStepMessage.WriteBurnBy(w, who);
                s.Broadcast(w, Channel.Control, reliable: true, except: who);
            }

            Plugin.Notice(
                $"{s.NameOf(who)} set the Being on fire. The forest burns.");
            if (step == BurnStep.Missing)
            {
                Plugin.Log.LogWarning(
                    "[epilogue] the Being burns in another game, but our part "
                        + "has no such events");
                return;
            }

            if (step == BurnStep.AlreadyFired)
            {
                return;
            }

            Plugin.Log.LogInfo(
                $"[epilogue] {s.NameOf(who)} set the Being on fire: running "
                    + $"{BurnEvents} here too");
            _coop.Guards.Replaying = true;
            try
            {
                found.fire();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"[epilogue] couldn't run {BurnEvents}: {e.Message}");
            }
            finally
            {
                _coop.Guards.Replaying = false;
            }
        }

        /// <summary>
        /// The crater's events in our part (null: none).
        /// </summary>
        private GameEvents BurnEventsHere()
        {
            foreach (var ge in UnityEngine.Object
                .FindObjectsOfType<GameEvents>(true))
            {
                if (ge != null && ge.name == BurnEvents)
                {
                    return ge;
                }
            }

            return null;
        }

        /// <summary>
        /// The only code reading the crater's GameEvents for the fire rule.
        /// </summary>
        private BurnFacts BurnOf(GameEvents found)
        {
            bool has = found != null;
            return new BurnFacts
            {
                Shared = _burnShared,
                Found = has,
                Fired = has && found.fired,
                Multiple = has && found.multipleFire,
            };
        }
    }
}
