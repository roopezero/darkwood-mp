using System.Collections;
using System.Collections.Generic;
using DarkwoodMP.World.Joining;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// A client that leaves in a shared dream (World.KeptCharacters'
    /// Characters.Dream): its kept character must be the one from before the
    /// dream, so it comes back, once the host's dream is over, with its own
    /// bags, out of any dream, where it began dreaming.
    /// </summary>
    public static partial class MapScenarioDreams
    {
        private static List<string> _bagsBefore;
        private static Vector3 _placeBefore;

        internal static void RegisterRejoin()
        {
            Add("dream-bags", DreamBags, timeout: 10f);
            Add("dream-leave", DreamLeave, timeout: 60f);
            Add("dream-join", DreamJoin, timeout: 10f);
            Add("dream-waiting", DreamWaiting, timeout: 30f);
            Add("dream-joined", DreamJoined, timeout: 400f);
        }

        /// Our bags' items (type x amount), bag then hotbar.
        private static List<string> Bags()
        {
            var list = new List<string>();
            foreach (var bag in new[] { P.Inventory, P.Hotbar })
            {
                foreach (var slot in bag.slots)
                {
                    var item = slot.invItem;
                    if (!InvItemClass.isNull(item))
                    {
                        list.Add(item.type + "x" + item.amount);
                    }
                }
            }

            list.Sort();
            return list;
        }

        private static bool HasCharacter()
        {
            CoopSession coop = Coop.Current;
            return coop != null && coop.Characters.HasCharacter;
        }

        /// <summary>
        /// "dream-bags" (client): note our bags and place, before a dream.
        /// </summary>
        private static IEnumerator DreamBags(string[] a, Result r)
        {
            yield return null;
            _bagsBefore = Bags();
            _placeBefore = P.transform.position;
            r.Info =
                $"{_bagsBefore.Count} items at {Fmt(_placeBefore)}: "
                    + string.Join(", ", _bagsBefore);
            if (_bagsBefore.Count == 0)
            {
                r.Fail("no items to lose: the check would prove nothing");
            }
        }

        /// <summary>
        /// "dream-leave" (client): in the host's dream with the dream's bags,
        /// stay past one character save, then leave the session.
        /// </summary>
        private static IEnumerator DreamLeave(string[] a, Result r)
        {
            if (!D.dreaming || Coop.Current.Dreaming.Shared == null)
            {
                r.Fail("not in the host's dream");
                yield break;
            }

            var inDream = Bags();
            // a character save (every 15 s) is sent from the dream
            yield return new WaitForSecondsRealtime(17f);
            Plugin.Instance.Host.Session.Backend.Leave();
            float giveUp = Time.realtimeSinceStartup + 30f;
            while (Coop.Current != null
                && Time.realtimeSinceStartup < giveUp)
            {
                yield return new WaitForSecondsRealtime(0.5f);
            }

            r.Info =
                $"left in the dream with {inDream.Count} items: "
                    + string.Join(", ", inDream)
                    + $"; session gone {Coop.Current == null}";
            if (string.Join(",", inDream) == string.Join(",", _bagsBefore))
            {
                r.Fail("the dream's bags are our own: " + r.Info);
            }
        }

        /// <summary>
        /// "dream-join" (client): join the host again.
        /// </summary>
        private static IEnumerator DreamJoin(string[] a, Result r)
        {
            Plugin.Instance.Host.Session.Backend.Rejoin();
            yield return new WaitForSecondsRealtime(1f);
            r.Info = "joining again";
        }

        /// <summary>
        /// "dream-waiting" (client): while the host dreams we aren't let in.
        /// </summary>
        private static IEnumerator DreamWaiting(string[] a, Result r)
        {
            yield return new WaitForSecondsRealtime(10f);
            r.Info =
                $"in world {WorldShare.InWorld}, client mode "
                    + $"{Combat.NetEnemies.ClientMode}, have "
                    + $"{HasCharacter()}";
            if (HasCharacter())
            {
                r.Fail("let in while the host dreams: " + r.Info);
            }
        }

        /// <summary>
        /// "dream-joined" (client): in again with our character: our own bags
        /// from before the dream, out of any dream, where we began dreaming.
        /// </summary>
        private static IEnumerator DreamJoined(string[] a, Result r)
        {
            float giveUp = Time.realtimeSinceStartup + 380f;
            while (!(WorldShare.WorldReady && Combat.NetEnemies.ClientMode
                    && HasCharacter()))
            {
                if (Time.realtimeSinceStartup > giveUp)
                {
                    r.Fail("not in with our character after 380 s");
                    yield break;
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            yield return new WaitForSecondsRealtime(3f);
            var now = Bags();
            float off = Vector3.Distance(
                Flat(P.transform.position), Flat(_placeBefore));
            string shared = Coop.Current.Dreaming.Shared;
            if (shared == null)
            {
                shared = "none";
            }

            r.Info =
                $"{now.Count} items at {Fmt(P.transform.position)} "
                    + $"({off:F1} from before the dream), dreaming "
                    + $"{D.dreaming}, shared dream {shared}: "
                    + string.Join(", ", now);
            if (string.Join(",", now) != string.Join(",", _bagsBefore))
            {
                r.Fail(
                    "not our bags from before the dream ("
                        + string.Join(", ", _bagsBefore) + "): " + r.Info);
            }
            else if (InDream || Coop.Current.Dreaming.Shared != null)
            {
                r.Fail("still in a dream: " + r.Info);
            }
            else if (off > 10f)
            {
                r.Fail("not where we began dreaming: " + r.Info);
            }
        }

        private static Vector3 Flat(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }
    }
}
