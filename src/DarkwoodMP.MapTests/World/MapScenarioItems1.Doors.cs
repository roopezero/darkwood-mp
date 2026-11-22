using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Keys, locked doors and doors the story blocks.</summary>
    public static partial class MapScenarioItems1
    {
        // ---- keys -----------------------------------------------------------

        /// <summary>
        /// "door-lock-key N" (host): lock door N with a key our journal hasn't
        /// got (the first by name).
        /// </summary>
        private static IEnumerator DoorLockKey(string[] a, Result r)
        {
            var d = MapScenarioWorld.Nth<Door>(a[0]);
            if (d == null)
            {
                r.Fail($"no door {a[0]}");
                yield break;
            }

            var j = Singleton<UI>.Instance.journal;
            string key =
                Singleton<JournalDatabase>.Instance.keysDict.Keys.Where(
                    k => !string.IsNullOrEmpty(k) && !j.keysDict.ContainsKey(k))
                    .OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();
            if (key == null)
            {
                r.Fail("no key our journal hasn't got");
                yield break;
            }

            if (d.opened)
            {
                d.close(P.transform);
                yield return new WaitForSeconds(0.5f);
            }

            d.lockMe(key);
            yield return new WaitForSeconds(0.5f);
            var l = d.GetComponent<Locked>();
            r.Info =
                $"{d.name} {MapScenarioWorld.Ref(d)} "
                    + $"locked={l != null && l.locked} key {key}";
            if (l == null || !l.locked)
            {
                r.Fail($"{r.Info}: not locked");
            }
        }

        /// <summary>
        /// "door-key N [--forged]" (client): open door N's lock with its key,
        /// as the player does: the key picked up (into the journal) and
        /// Locked.tryToOpenWithKey in the same frame. --forged: without the
        /// key, a key unlock sent anyway (LockShare.Send), which the host must
        /// refuse.
        /// </summary>
        private static IEnumerator DoorKey(string[] a, Result r)
        {
            var d = MapScenarioWorld.Nth<Door>(a[0]);
            Locked l = null;
            if (d != null)
            {
                l = d.GetComponent<Locked>();
            }

            if (l == null || !l.locked)
            {
                r.Fail($"door {a[0]} isn't locked here");
                yield break;
            }

            yield return Walk(d.transform.position);
            var j = Singleton<UI>.Instance.journal;
            int refused = Coop.Current.Authority.RolledBack;
            if (a.Contains("--forged"))
            {
                if (j.keysDict.ContainsKey(l.keyType))
                {
                    r.Fail($"our journal has {l.keyType} already");
                    yield break;
                }

                AsPlayer(() => Coop.Current.Locks.Send(l, 1f));
                float giveUp = Time.realtimeSinceStartup + 8f;
                while (Coop.Current.Authority.RolledBack == refused
                    && Time.realtimeSinceStartup < giveUp)
                {
                    yield return null;
                }

                yield return new WaitForSeconds(0.5f);
                r.Info =
                    $"{d.name} {MapScenarioWorld.Ref(d)} key {l.keyType}: "
                        + $"locked={l.locked}";
                if (Coop.Current.Authority.RolledBack == refused)
                {
                    r.Fail($"{r.Info}: the host didn't refuse it");
                }
                else if (!l.locked)
                {
                    r.Fail($"{r.Info}: refused, but unlocked here");
                }

                yield break;
            }

            // as KeyReference.pickup
            j.keysDict[l.keyType] = new Journal.Key { type = l.keyType };
            bool opened = false;
            AsPlayer(() => opened = l.tryToOpenWithKey());
            // a refusal would lock it again
            yield return new WaitForSeconds(2f);
            r.Info =
                $"{d.name} {MapScenarioWorld.Ref(d)} key {l.keyType}: "
                    + $"opened={opened} locked={l.locked}";
            if (!opened || l.locked)
            {
                r.Fail($"{r.Info}: still locked");
            }
            else if (Coop.Current.Authority.RolledBack != refused)
            {
                r.Fail($"{r.Info}: the host refused it");
            }
        }

        /// <summary>
        /// "door-locked N yes|no": door N's lock as expected here.
        /// </summary>
        private static IEnumerator DoorLocked(string[] a, Result r)
        {
            var d = MapScenarioWorld.Nth<Door>(a[0]);
            Locked l = null;
            if (d != null)
            {
                l = d.GetComponent<Locked>();
            }

            bool want = a.Length > 1 && a[1] == "yes";
            float giveUp = Time.realtimeSinceStartup + 5f;
            while ((l != null && l.locked) != want
                && Time.realtimeSinceStartup < giveUp)
            {
                yield return null;
            }

            if (d == null)
            {
                r.Info = "no such door";
            }
            else
            {
                r.Info =
                    $"{d.name} {MapScenarioWorld.Ref(d)} "
                        + $"locked={l != null && l.locked}";
            }

            if ((l != null && l.locked) != want)
            {
                string text = "open";
                if (want)
                {
                    text = "locked";
                }

                r.Fail($"{r.Info}, not {text}");
            }
        }

        // ---- doors the story blocks -----------------------------------------

        /// <summary>
        /// "door-block N": door N closed and blocked here, as a story door is
        /// in the save until the story clears it (Piotrek's house). Only the
        /// field is set, so nothing is sent: run it in each game.
        /// </summary>
        private static IEnumerator DoorBlock(string[] a, Result r)
        {
            var d = MapScenarioWorld.Nth<Door>(a[0]);
            if (d == null)
            {
                r.Fail($"no door {a[0]}");
                yield break;
            }

            var l = d.GetComponent<Locked>();
            if (d.destroyed || d.barricaded || (l != null && l.locked))
            {
                r.Fail(
                    $"door {a[0]} ({d.name}) is broken, barricaded or locked");
                yield break;
            }

            if (d.opened)
            {
                d.close(P.transform);
                yield return new WaitForSeconds(0.5f);
            }

            d.blocked = true;
            r.Info =
                $"{d.name} {MapScenarioWorld.Ref(d)} blocked, open={d.opened}";
            if (d.opened)
            {
                r.Fail($"{r.Info}: didn't close");
            }
        }

        /// <summary>
        /// "door-story-unblock N" (host): the story clears door N's block the
        /// way a scripted event does (GameEvent modifyDoor unblock, the host's
        /// step, as in an event set off by a death).
        /// </summary>
        private static IEnumerator DoorStoryUnblock(string[] a, Result r)
        {
            var d = MapScenarioWorld.Nth<Door>(a[0]);
            if (d == null)
            {
                r.Fail($"no door {a[0]}");
                yield break;
            }

            if (!d.blocked)
            {
                r.Fail($"door {a[0]} isn't blocked here");
                yield break;
            }

            var ge = new GameEvent
            {
                type = GameEvent.Type.modifyDoor,
                doorModifyType = GameEvent.DoorModify.unblock,
                targetGameObjects = new List<GameObject> { d.gameObject },
            };

            Plugin.Instance.StartCoroutine(ge.fire(d.gameObject));
            yield return new WaitForSeconds(1f);
            r.Info = $"{d.name} {MapScenarioWorld.Ref(d)} blocked={d.blocked}";
            if (d.blocked)
            {
                r.Fail($"{r.Info}: the event didn't unblock it");
            }
        }

        /// <summary>
        /// "door-blocked N yes|no": door N blocked here as expected (waiting up
        /// to 5 s).
        /// </summary>
        private static IEnumerator DoorBlocked(string[] a, Result r)
        {
            var d = MapScenarioWorld.Nth<Door>(a[0]);
            bool want = a.Length > 1 && a[1] == "yes";
            float giveUp = Time.realtimeSinceStartup + 5f;
            while (d != null && d.blocked != want
                && Time.realtimeSinceStartup < giveUp)
            {
                yield return null;
            }

            if (d == null)
            {
                r.Info = "no such door";
            }
            else
            {
                r.Info =
                    $"{d.name} {MapScenarioWorld.Ref(d)} blocked={d.blocked}";
            }

            if (d == null || d.blocked != want)
            {
                string text = "clear";
                if (want)
                {
                    text = "blocked";
                }

                r.Fail($"{r.Info}, not {text}");
            }
        }

        /// <summary>
        /// "door-use N": our player walks to door N and opens it as the player
        /// does (Player.openCloseDoor, which the game reaches only for a door
        /// that isn't blocked: Player.itemDefaultAction). A client's open is a
        /// request, and the step fails if the host refused it.
        /// </summary>
        private static IEnumerator DoorUse(string[] a, Result r)
        {
            var d = MapScenarioWorld.Nth<Door>(a[0]);
            if (d == null)
            {
                r.Fail($"no door {a[0]}");
                yield break;
            }

            yield return Walk(d.transform.position);
            if (d.blocked)
            {
                r.Fail(
                    $"{d.name} {MapScenarioWorld.Ref(d)} is blocked here: the "
                        + "player can't open it");
                yield break;
            }

            if (d.opened)
            {
                r.Fail($"{d.name} is open already");
                yield break;
            }

            int refused = Coop.Current.Authority.RolledBack;
            AsPlayer(() => P.openCloseDoor(d, false));
            // a refusal would close it again
            yield return new WaitForSeconds(2f);
            r.Info = $"{d.name} {MapScenarioWorld.Ref(d)} open={d.opened}";
            if (Coop.Current.Authority.RolledBack != refused)
            {
                r.Fail($"{r.Info}: the host refused it");
            }
            else if (!d.opened)
            {
                r.Fail($"{r.Info}: didn't open");
            }
        }

        /// <summary>
        /// "door-opened N yes|no": door N open here as expected (waiting up to
        /// 5 s).
        /// </summary>
        private static IEnumerator DoorOpened(string[] a, Result r)
        {
            var d = MapScenarioWorld.Nth<Door>(a[0]);
            bool want = a.Length > 1 && a[1] == "yes";
            float giveUp = Time.realtimeSinceStartup + 5f;
            while (d != null && d.opened != want
                && Time.realtimeSinceStartup < giveUp)
            {
                yield return null;
            }

            if (d == null)
            {
                r.Info = "no such door";
            }
            else
            {
                r.Info = $"{d.name} {MapScenarioWorld.Ref(d)} open={d.opened}";
            }

            if (d == null || d.opened != want)
            {
                string text = "closed";
                if (want)
                {
                    text = "open";
                }

                r.Fail($"{r.Info}, not {text}");
            }
        }
    }
}
