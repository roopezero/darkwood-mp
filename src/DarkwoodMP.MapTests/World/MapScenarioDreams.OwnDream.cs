using System.Collections;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioDreams
    {
        /// <summary>
        /// "own-dream NAME" (client): go into the dream NAME by ourselves (not
        /// shared), as a story dream does.
        /// </summary>
        private static IEnumerator OwnDream(string[] a, Result r)
        {
            string name = "dream_church_ruins_01";
            if (a.Length > 0)
            {
                name = a[0];
            }

            if (DreamSync.Shared != null)
            {
                r.Fail($"already in the shared dream {DreamSync.Shared}");
                yield break;
            }

            DreamSync.TestOwnDream(name);
            float giveUp = Time.realtimeSinceStartup + 60f;
            while (!D.dreaming && Time.realtimeSinceStartup < giveUp)
            {
                yield return new WaitForSeconds(0.5f);
            }

            // the host's stand-in for us goes Away with our next snapshots
            yield return new WaitForSeconds(3f);
            DreamPreset preset = D.preset;
            string presetName = null;
            if (preset != null)
            {
                presetName = preset.name;
            }

            string shared = DreamSync.Shared;
            if (shared == null)
            {
                shared = "none";
            }

            r.Info =
                $"dreaming {presetName} (shared {shared}) at "
                    + $"{Locations.Current()}; host runs here "
                    + $"{Locations.HostRunsHere()}";
            if (!D.dreaming || DreamSync.Shared != null
                || Locations.HostRunsHere())
            {
                r.Fail($"not in a dream of our own: {r.Info}");
            }
        }

        /// <summary>
        /// "own-dream-use" (client, in our own church dream): the host hasn't
        /// got this dream, so what our player does in it is ours alone. We open
        /// and close the burnt wardrobe (a container, and the trigger that lets
        /// its chomper out), open a door if the dream has one, and send the
        /// host a request about the wardrobe as an old game would. Nothing may
        /// be refused or rolled back, the chomper comes out and runs in our
        /// game, the wardrobe and the door are still there, and the host takes
        /// the request without destroying our wardrobe.
        /// </summary>
        private static IEnumerator OwnDreamUse(string[] a, Result r)
        {
            var loc = D.dreamLocation;
            EventTriggers et = null;
            if (loc != null)
            {
                et = loc.GetComponentsInChildren<EventTriggers>(true)
                    .FirstOrDefault(
                        x => x.name.StartsWith("Wardrobe_Big_1_Burned"));
            }

            if (et == null)
            {
                string name = null;
                if (loc != null)
                {
                    name = loc.name;
                }

                r.Fail($"no burnt wardrobe in {name}");
                yield break;
            }

            Item item = et.GetComponent<Item>();
            if (item == null)
            {
                item = et.GetComponentInParent<Item>();
            }

            int rolledBefore = Coop.Current.Authority.RolledBack;
            yield return MoveNear(et.transform);

            string opened = "no container";
            if (item != null && item.GetComponent<Inventory>() != null)
            {
                try
                {
                    AsPlayer(() => item.openInventory());
                    opened = $"opened (searched {item.searched})";
                }
                catch (System.Exception e)
                {
                    opened = "couldn't open: " + e.Message;
                }

                // Containers.Poll looks at it
                yield return new WaitForSeconds(1f);
                var inv = item.GetComponent<Inventory>();
                if (inv != null && inv.open)
                {
                    AsPlayer(() => inv.hide());
                }

                yield return new WaitForSeconds(0.5f);
            }

            AsPlayer(
                () => et.fireEventTrigger(
                    global::EventTrigger.Type.onCloseContainer));

            var ne = Combat.NetEnemies.Instance;
            float until = Time.realtimeSinceStartup + 15f;
            Character ours = null;
            while (ours == null && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSeconds(0.5f);
                ours = DreamCharacters().FirstOrDefault(
                    c => c.name.StartsWith("ChomperBlack")
                        && c.gameObject.activeInHierarchy
                        && !ne.IsPuppet(c, out _));
            }

            var door = loc.GetComponentsInChildren<Door>(true).FirstOrDefault(
                d => d != null && d.gameObject.activeInHierarchy
                    && !d.destroyed && !d.opened && !d.blocked
                    && (d.GetComponent<Locked>() == null
                        || !d.GetComponent<Locked>().locked));
            string doorInfo = "no door to open";
            if (door != null)
            {
                yield return MoveNear(door.transform);
                AsPlayer(
                    () => door.open(P.transform.position, P.transform, 0f));
                yield return new WaitForSeconds(1f);
                if (door != null)
                {
                    doorInfo = $"{door.name} open {door.opened}";
                }
                else
                {
                    doorInfo = "the door is gone";
                }
            }

            // As a game without the fix sent it: a request about our wardrobe,
            // which the host hasn't got.
            bool? ok = null;
            string why = null;
            if (item != null)
            {
                var w = Net.Session.Instance.Begin(Net.MsgType.WorldAction);
                WorldActions.WriteAction(
                    w, WorldActions.Action.ItemSearched,
                    ObjRef.Of(item.gameObject), 0f, 0f, false, false, "");
                Coop.Current.Authority.Request(w,
                    $"forged ItemSearched {item.name} in our own dream",
                    (o, reason, res) =>
                {
                    ok = o;
                    why = reason;
                }, inventory: false);

                float giveUp = Time.realtimeSinceStartup + 8f;
                while (ok == null && Time.realtimeSinceStartup < giveUp)
                {
                    yield return null;
                }

                // an ObjState after it, if any
                yield return new WaitForSeconds(1f);
            }

            int refused = Coop.Current.Authority.RolledBack - rolledBefore;
            string text = "gone";
            if (et != null && item != null)
            {
                text = "still there";
            }

            string locationName = null;
            if (loc != null)
            {
                locationName = loc.name;
            }

            string chomper = "not out";
            if (ours != null)
            {
                string place = Fmt(ours.transform.position);
                string state = " running";
                if (Combat.NetEnemies.Frozen.Contains(ours))
                {
                    state = " (frozen)";
                }

                chomper = place + state;
            }

            string answer;
            if (ok == null)
            {
                answer = "no answer";
            }
            else if (ok.Value)
            {
                answer = "accepted";
            }
            else
            {
                answer = "refused: " + why;
            }

            r.Info =
                $"in {locationName}: wardrobe {opened}, {text}; our chomper "
                    + $"{chomper}; {doorInfo}; " +
                     $"forged request {answer}; refused {refused}";
            if (refused > 0)
            {
                r.Fail(
                    $"the host refused what we did in our own dream: {r.Info}");
            }
            else if (et == null || (item == null && opened != "no container"))
            {
                r.Fail($"our wardrobe was destroyed: {r.Info}");
            }
            else if (ours == null || Combat.NetEnemies.Frozen.Contains(ours))
            {
                r.Fail($"the wardrobe's event didn't run here: {r.Info}");
            }
            else if (door != null && !door.opened)
            {
                r.Fail($"the door didn't stay open: {r.Info}");
            }
            else if (item != null && ok != true)
            {
                r.Fail(
                    "the host didn't take a request from our own dream: "
                        + $"{r.Info}");
            }
        }

        /// <summary>
        /// Our player next to t, on a spot with no loose thing our player would
        /// land on and push.
        /// </summary>
        private static IEnumerator MoveNear(Transform t)
        {
            var p = P;
            var away = p.transform.position - t.position;
            away.y = 0f;
            Vector3 dir;
            if (away.sqrMagnitude > 1f)
            {
                dir = away.normalized;
            }
            else
            {
                dir = Vector3.right;
            }

            var at = t.position + dir * 250f;
            for (int i = 0; i < 16; i++)
            {
                var spot = t.position
                    + Quaternion.Euler(0f, 22.5f * i, 0f) * dir * 250f;
                spot.y = p.transform.position.y;
                if (Physics.OverlapSphere(spot, 100f).Any(
                    col => col.attachedRigidbody != null
                        && !col.attachedRigidbody.isKinematic
                        && col.GetComponentInParent<Player>() == null))
                {
                    continue;
                }

                at = spot;
                break;
            }

            p.transform.position =
                new Vector3(at.x, p.transform.position.y, at.z);
            var rb = p.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = p.transform.position;
            }

            yield return new WaitForSeconds(0.5f);
        }
    }
}
