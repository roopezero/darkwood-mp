using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using DarkwoodMP.World.Scripted;

namespace DarkwoodMP.World
{
    /// <summary>An NPC recruited to follow a player.</summary>
    public static partial class MapScenarioNpcs
    {
        /// <summary>
        /// "follow-go DX DZ" (client): our player walks to centre + (DX, DZ).
        /// </summary>
        private static IEnumerator FollowGo(string[] a, Result r)
        {
            yield return Walk(Centre + new Vector3(F(a[0]), 0f, F(a[1])), 80f);
            r.Info = $"at {Fmt(Player.Instance.transform.position)}";
        }

        /// <summary>
        /// "follow-recruit": the NPC made a follower, its aggressiveness and
        /// behaviour before.
        /// </summary>
        private static (Character c, Aggressiveness ag,
            Character.Behaviour be, Vector3 was) _follower;

        /// <summary>"follow-recruit NAME" (host): our NPC of that name nearest
        /// the centre is made a follower by a scripted step (GameEvent
        /// modifyCharacter, aggressiveness follower) run as the joiner's event
        /// (as an EventFired from them runs it: EventParts.By). It is
        /// recorded as theirs and goes after the joiner's stand-in as its
        /// target, not us (waiting up to 10 s). Whether it walks after them is
        /// follow-check's: the test world's night trader never starts walking
        /// (it stays idle), so that step needs an NPC that walks.</summary>
        private static IEnumerator FollowRecruit(string[] a, Result r)
        {
            var npc = Shared(a[0], Centre);
            Character c = null;
            if (npc != null)
            {
                c = npc.GetComponent<Character>();
            }

            var proxy = MapScenarioNight.ClientProxy();
            if (c == null)
            {
                r.Fail($"no {a[0]} here that every game has");
                yield break;
            }

            if (proxy == null)
            {
                r.Fail("no joiner");
                yield break;
            }

            _follower =
                (c, c.aggressiveness, c.behaviour, c.transform.position);
            var ge = new GameEvent
            {
                type = GameEvent.Type.modifyCharacter,
                characterModifyType = GameEvent.CharacterModify.aggressiveness,
                destAggressiveness = Aggressiveness.follower,
                targetGameObjects = new List<GameObject> { npc.gameObject },
            };

            Plugin.Instance.Host.Parts.RunAs(EventPart.HostAndShared,
                replaying: true, () =>
            {
                var was = Plugin.Instance.Host.Parts.By;
                Plugin.Instance.Host.Parts.By = proxy.Owner;
                try
                {
                    Plugin.Instance.StartCoroutine(ge.fire(npc.gameObject));
                }
                finally
                {
                    Plugin.Instance.Host.Parts.By = was;
                }
            });

            float until = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < until
                && c.target != proxy.transform)
            {
                yield return new WaitForSecondsRealtime(0.5f);
            }

            r.Info = FollowInfo(c, proxy);
            if (Coop.Current.Followers.LeaderOf(c) != proxy)
            {
                r.Fail(
                    $"not recorded as {proxy.OwnerName}'s follower: {r.Info}");
            }
            else if (c.target != proxy.transform)
            {
                r.Fail(r.Info);
            }
        }

        private static string FollowInfo(
            Character c, Combat.NetPlayerProxy proxy)
        {
            string name = c.name;
            Aggressiveness aggressiveness = c.aggressiveness;
            Character.Behaviour behaviour = c.behaviour;
            string target;
            if (c.target == null)
            {
                target = "none";
            }
            else if (c.target == proxy.transform)
            {
                target = proxy.OwnerName;
            }
            else
            {
                target = c.target.name;
            }

            float fromProxy = StateDump.Flat(
                c.transform.position - proxy.transform.position);
            float fromUs = StateDump.Flat(
                c.transform.position - Player.Instance.transform.position);
            return $"{name}: {aggressiveness}, {behaviour}, target {target}, "
                + $"{fromProxy:0} from {proxy.OwnerName}, {fromUs:0} from us";
        }

        /// <summary>"follow-check" (host, after the joiner walked off): the
        /// follower went after them, not us (within 500 of their stand-in,
        /// waiting up to 20 s).</summary>
        private static IEnumerator FollowCheck(string[] a, Result r)
        {
            var c = _follower.c;
            var proxy = MapScenarioNight.ClientProxy();
            if (c == null || proxy == null)
            {
                r.Fail("no follower or no joiner");
                yield break;
            }

            float until = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < until
                && StateDump.Flat(
                    c.transform.position - proxy.transform.position) >= 500f)
            {
                yield return new WaitForSecondsRealtime(0.5f);
            }

            r.Info = FollowInfo(c, proxy);
            if (StateDump.Flat(c.transform.position
                    - proxy.transform.position) >= 500f
                || c.target != proxy.transform)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>"follow-near NAME" (client): our copy of that NPC comes to
        /// us (within 500), as the host's follows our player there (NpcSync;
        /// waiting up to 20 s).</summary>
        private static IEnumerator FollowNear(string[] a, Result r)
        {
            float until = Time.realtimeSinceStartup + 20f, d = -1f;
            NPC npc = null;
            while (Time.realtimeSinceStartup < until)
            {
                npc = Shared(a[0], Player.Instance.transform.position);
                if (npc != null)
                {
                    d = StateDump.Flat(
                        npc.transform.position
                            - Player.Instance.transform.position);
                }
                else
                {
                    d = -1f;
                }

                if (npc != null && d < 500f)
                {
                    break;
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            if (npc != null)
            {
                r.Info =
                    $"{npc.name} at {Fmt(npc.transform.position)}, {d:0} from "
                        + $"us at {Fmt(Player.Instance.transform.position)}";
            }
            else
            {
                r.Info = $"no {a[0]} here";
            }

            if (npc == null || d >= 500f)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>
        /// "follow-end" (host): the follower is as it was, where it was.
        /// </summary>
        private static IEnumerator FollowEnd(string[] a, Result r)
        {
            var c = _follower.c;
            if (c == null)
            {
                r.Fail("no follower");
                yield break;
            }

            c.aggressiveness = _follower.ag;
            c.target = null;
            c.setBehaviour(_follower.be, force: true);
            c.transform.position = _follower.was;
            _follower = default;
            yield return new WaitForSeconds(1f);
            r.Info =
                $"{c.name} back at {Fmt(c.transform.position)}, "
                    + $"{c.aggressiveness}, {c.behaviour}";
        }
    }
}
