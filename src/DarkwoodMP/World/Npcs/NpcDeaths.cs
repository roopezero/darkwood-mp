using System.Collections.Generic;
using DarkwoodMP.World.Npcs;
using UnityEngine;
using Action = DarkwoodMP.World.WorldActions.Action;

namespace DarkwoodMP.World
{
    /// <summary>
    /// An NPC (a trader...) died. NPCs live in each player's game; whoever's
    /// game it died in tells the others (NpcDied), and it dies there too.
    /// (The game marks some deaths only with a story flag, which FlagsSync
    /// shares as well.) A client's NPCs can only be killed by its own player
    /// or the story, so its NPC deaths are always news. The NPC a death
    /// names, and the host's check of a client's, are NpcDeathRules'.
    /// </summary>
    internal sealed class NpcDeaths
    {
        private readonly CoopSession _coop;

        public NpcDeaths(CoopSession coop)
        {
            _coop = coop;
        }

        /// <summary>
        /// Host: whether a client's player could have killed the NPC this
        /// NpcDied names (WorldActions.Allowed). Refuses the request if not;
        /// on a yes FlagsSync is told the death is allowed.
        /// </summary>
        public bool MayDie(ObjRef obj, string name, Vec2 at)
        {
            var npc = Dying(obj, name, at);
            var facts = new NpcDeathFacts { Found = npc != null };
            if (npc != null)
            {
                facts = facts with
                {
                    Requester = _coop.Authority.ReachOf(
                        npc.transform.position, npc.name),
                };
            }

            var verdict = NpcDeathRules.MayDie(facts);
            _coop.Authority.Refuse(verdict);
            if (verdict.Refused)
            {
                return false;
            }

            var instance = _coop.Story;
            if (instance != null)
            {
                string named;
                if (npc.npc != null)
                {
                    named = npc.npc.name;
                }
                else
                {
                    named = npc.name;
                }

                instance.DeathAllowed(named);
            }

            return true;
        }

        /// <summary>
        /// An NpcDied from another game, applied here: the NPC it names
        /// dies. A copy of an NPC the host runs as an enemy dies with the
        /// host's state (Combat.NetEnemies).
        /// </summary>
        public void Apply(Action a, ObjRef obj, WorldActionBody body)
        {
            var at = new Vec2(body.X, body.Y);
            var npc = WorldActions.Logged(a, obj, Dying(obj, body.Text, at));
            bool puppet = npc != null
                && Combat.NetEnemies.Instance != null
                && Combat.NetEnemies.Instance.IsPuppet(npc, out _);
            if (npc != null && npc.alive && !puppet)
            {
                npc.die();
            }
        }

        /// <summary>
        /// The NPC an NpcDied names (NpcDeathRules.Pick): by its ref, else
        /// (an NPC each game placed itself, or one that walked off its named
        /// spot) the living NPC of that name nearest to where it died in the
        /// killer's game.
        /// </summary>
        private static Character Dying(ObjRef obj, string name, Vec2 at)
        {
            var c = ObjRef.Resolve<Character>(obj);
            var death = new DyingFacts
            {
                RefIsNpc = c != null && c.npc != null,
                Name = name,
                At = at,
            };

            if (death.RefIsNpc)
            {
                death = death with { RefName = c.npc.name };
            }

            var npcs = new List<Character>();
            var candidates = new List<NpcCandidate>();
            bool named = !string.IsNullOrEmpty(name);
            if (named && !(death.RefIsNpc && death.RefName == name))
            {
                CandidatesOf(npcs, candidates);
            }

            int pick = NpcDeathRules.Pick(death, candidates);
            if (pick == NpcDeathRules.ByRef)
            {
                return c;
            }

            return npcs[pick];
        }

        /// <summary>
        /// The NPCs in this world that a death may name, each with its
        /// Character (null when it has none): the only code that reads
        /// them for the rule.
        /// </summary>
        private static void CandidatesOf(
            List<Character> npcs, List<NpcCandidate> candidates)
        {
            foreach (var n in UnityEngine.Object.FindObjectsOfType<NPC>())
            {
                if (n == null)
                {
                    continue;
                }

                var ch = n.GetComponent<Character>();
                var candidate = new NpcCandidate
                {
                    Name = n.name,
                    HasCharacter = ch != null,
                };

                if (ch != null)
                {
                    var p = ch.transform.position;
                    candidate = candidate with
                    {
                        Alive = ch.alive,
                        At = new Vec2(p.x, p.z),
                    };
                }

                npcs.Add(ch);
                candidates.Add(candidate);
            }
        }

        // ---- what changed ---------------------------------------------------

        /// <summary>
        /// Character.die starts: one deeper in patched calls. Says whether
        /// it was alive.
        /// </summary>
        public bool BeforeDie(Character c)
        {
            _coop.Guards.Depth++;
            return c.alive;
        }

        /// <summary>
        /// Character.die returned: a dead NPC's corpse is noted
        /// (HostileNpcs.Died, a replayed death too), and at the outermost
        /// call, while sharing, the death is sent with the NPC's name and
        /// where it stood: one each game puts in place itself (the morning
        /// trader, the wolf) has its own save id in each, and one that
        /// walked can be off its named spot.
        /// </summary>
        public void AfterDie(Character c, bool wasAlive)
        {
            if (wasAlive && c != null && c.npc != null && !c.alive)
            {
                Combat.HostileNpcs.Died(c);
            }

            var facts = new DeathSentFacts
            {
                Outermost = _coop.Guards.Outermost(),
                Sharing = _coop.Guards.Sharing,
                HasNpc = c != null && c.npc != null,
                WasAlive = wasAlive,
            };

            if (!NpcDeathRules.SendsDeath(facts))
            {
                return;
            }

            var p = c.transform.position;
            WorldActions.Send(
                Action.NpcDied, c.gameObject, x: p.x, y: p.z,
                str: c.npc.name, byPlayerKnown: true);
        }

        /// <summary>
        /// Player.special_onKillNightTrader is about to run: false skips it
        /// when the death is replayed from another game, whose killer's
        /// dream it is.
        /// </summary>
        public bool RunsNightTraderDream()
        {
            if (!_coop.Guards.Replaying)
            {
                return true;
            }

            Plugin.Log.LogInfo(
                "[world] the night trader was killed in another game: the "
                    + "killer's dream isn't ours");
            return false;
        }
    }
}
