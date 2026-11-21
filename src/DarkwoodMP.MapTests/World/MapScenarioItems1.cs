using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Net;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for items used on the world: a trap placed the game's own
    /// way (Player.progressBarCompleted) and the host's checks on what a client
    /// places, doors opened with a key (the host checks the journal), and the
    /// porter whistle (the host calls the porter in every game). The list is
    /// tools/tests/map_scenario_items1.py.
    /// </summary>
    public static partial class MapScenarioItems1
    {
        private static Player P => Player.Instance;
        private static Session S => Session.Instance;
        private static readonly System.Reflection.MethodInfo ShowProxyItem =
            AccessTools.Method(typeof(Player), "showProxyItem");

        public static void Register()
        {
            Add("trap-place-far", TrapPlaceFar, timeout: 30f);
            Add("place-forged", PlaceForged);
            Add("door-lock-key", DoorLockKey);
            // the walk back from trap-place-far is 1400 long, then up to 8 s
            // for the refusal
            Add("door-key", DoorKey, timeout: 40f);
            Add("door-locked", DoorLocked);
            Add("door-block", DoorBlock);
            Add("door-story-unblock", DoorStoryUnblock);
            Add("door-blocked", DoorBlocked);
            Add("door-use", DoorUse, timeout: 30f);
            Add("door-opened", DoorOpened);
            Add("porter-ready", PorterReady);
            Add("whistle", Whistle, timeout: 40f);
            Add("porter-came", PorterCame, timeout: 40f);
            Add("porter-forged", PorterForged);
        }

        // ---- placing --------------------------------------------------------

        /// <summary>
        /// The item type whose placed prefab (InvItem.item) is named so
        /// ("beartrap"), or null.
        /// </summary>
        private static string PlaceableType(string prefab)
        {
            var db = Singleton<ItemsDatabase>.Instance;
            foreach (var kv in db.itemsDict.OrderBy(
                k => k.Key, StringComparer.Ordinal))
            {
                GameObject gameObject = Resources.Load(kv.Value) as GameObject;
                InvItem item = null;
                if (gameObject != null)
                {
                    item = gameObject.GetComponent<InvItem>();
                }

                if (item != null && item.canBePlaced && item.item != null
                    && item.item.name == prefab)
                {
                    return kv.Key;
                }
            }

            return null;
        }

        /// <summary>
        /// TYPE in a hotbar slot, that slot selected and in our hands (as
        /// MapScenarioIssues' "hold").
        /// </summary>
        private static IEnumerator HoldItem(string type, Result r)
        {
            var p = P;
            int slot = p.Hotbar.slots.FindIndex(
                s => !InvItemClass.isNull(s.invItem) && s.invItem.type == type);
            if (slot < 0)
            {
                slot = p.Hotbar.slots.FindIndex(
                    s => InvItemClass.isNull(s.invItem));
                if (slot < 0)
                {
                    slot = 0;
                    p.Hotbar.slots[0].removeItem();
                }

                p.Hotbar.slots[slot].inventory = p.Hotbar;
                p.Hotbar.slots[slot].createItem(type, 1);
            }

            if (p.Hotbar.slots[slot].selected)
            {
                p.selectCurrentHotbarSlot();
            }
            else
            {
                p.Hotbar.selectSlot(slot, noiseless: true, force: true);
            }

            float giveUp = Time.realtimeSinceStartup + 8f;
            while ((p.switchingItem || InvItemClass.isNull(p.currentItem)
                    || p.currentItem.type != type)
                && Time.realtimeSinceStartup < giveUp)
            {
                yield return null;
            }

            if (InvItemClass.isNull(p.currentItem)
                || p.currentItem.type != type)
            {
                string holding;
                if (InvItemClass.isNull(p.currentItem))
                {
                    holding = "nothing";
                }
                else
                {
                    holding = p.currentItem.type;
                }

                r.Fail(
                    $"couldn't take {type} in our hands (holding {holding})");
            }
        }

        /// <summary>
        /// Our player places PREFAB at `at` the game's own way: the item in our
        /// hands, its proxy (Player.showProxyItem) moved there, and
        /// Player.progressBarCompleted with placingItem, as the placing
        /// progress bar ends. made gets what was placed and shared (a new
        /// ObjRef.Spawned entry), or null.
        /// </summary>
        internal static IEnumerator PlaceReal(
            string prefab, Vector3 at, Result r, Action<GameObject> made)
        {
            string type = PlaceableType(prefab);
            if (type == null)
            {
                r.Fail($"no item places {prefab}");
                yield break;
            }

            yield return HoldItem(type, r);
            if (r.Failure != null)
            {
                yield break;
            }

            var p = P;
            var before = new HashSet<long>(ObjRef.Spawned.Keys);
            AsPlayer(() =>
            {
                ShowProxyItem.Invoke(
                    p, new object[] { p.currentItem.baseClass.item });
                p.proxyItem.transform.localPosition = at;
                p.placingItem = true;
                try
                {
                    p.progressBarCompleted();
                }
                finally
                {
                    p.placingItem = false;
                }
            });

            var id =
                ObjRef.Spawned.Keys.FirstOrDefault(k => !before.Contains(k));
            GameObject obj = null;
            if (id != 0 && ObjRef.Spawned.TryGetValue(id, out var go))
            {
                obj = go;
            }

            made(obj);
        }

        /// <summary>
        /// "trap-place-far PREFAB" (client): place one the game's way 1500 from
        /// our player. The host refuses it (out of reach): ours goes again and
        /// the item comes back.
        /// </summary>
        private static IEnumerator TrapPlaceFar(string[] a, Result r)
        {
            string prefab = "beartrap";
            if (a.Length > 0)
            {
                prefab = a[0];
            }

            string type = PlaceableType(prefab);
            if (type == null)
            {
                r.Fail($"no item places {prefab}");
                yield break;
            }

            yield return HoldItem(type, r);
            if (r.Failure != null)
            {
                yield break;
            }

            int had = MapScenarioWorld.CarriedOf(type),
                refused = Authority.RolledBack;
            var at = P.transform.position + new Vector3(1500f, 0f, 0f);
            GameObject go = null;
            yield return PlaceReal(prefab, at, r, g => go = g);
            if (r.Failure != null)
            {
                yield break;
            }

            if (go == null)
            {
                r.Fail("placed, but it isn't shared (no ObjRef.Spawned entry)");
                yield break;
            }

            int after = MapScenarioWorld.CarriedOf(type);
            float giveUp = Time.realtimeSinceStartup + 8f;
            while (Authority.RolledBack == refused
                && Time.realtimeSinceStartup < giveUp)
            {
                yield return null;
            }

            yield return new WaitForSeconds(0.5f);
            int back = MapScenarioWorld.CarriedOf(type);
            string spot = Fmt(at);
            string ours = "still there";
            if (go == null)
            {
                ours = "removed";
            }

            r.Info =
                $"{prefab} at {spot}: carried {had} -> {after} -> {back}, ours "
                    + $"{ours}";
            if (Authority.RolledBack == refused)
            {
                r.Fail($"{r.Info}: the host didn't refuse it");
            }
            else if (go != null)
            {
                r.Fail($"{r.Info}: refused, but ours is still there");
            }
            else if (back != had)
            {
                r.Fail($"{r.Info}: the item didn't come back");
            }
        }

        /// <summary>
        /// "place-forged PREFAB DX DZ [--none]" (client): send the host a
        /// placed ObjSpawn our game didn't make (centre + offset): one out of
        /// reach, or a prefab no player can place. --none: in reach, but our
        /// player carries nothing that places it (all of it taken away, and our
        /// character sent, as placing does first). The host must refuse it.
        /// </summary>
        private static IEnumerator PlaceForged(string[] a, Result r)
        {
            if (S == null || !S.IsClient)
            {
                r.Fail("a client's step");
                yield break;
            }

            if (a.Contains("--none"))
            {
                string path = a[0];
                var types = Containers.PlacedWith(path);
                if (types == null)
                {
                    r.Fail($"no item places {path}");
                    yield break;
                }

                foreach (var type in types)
                {
                    int n = MapScenarioWorld.CarriedOf(type);
                    if (n > 0)
                    {
                        P.Inventory.removeItemAmountFromPlayer(type, n);
                    }

                    for (int i = 0; i < P.Hotbar.slots.Count; i++)
                    {
                        if (!InvItemClass.isNull(P.Hotbar.slots[i].invItem)
                            && P.Hotbar.slots[i].invItem.type == type)
                        {
                            P.Hotbar.slots[i].removeItem();
                        }
                    }
                }

                yield return null;
                int left = types.Sum(t => MapScenarioWorld.CarriedOf(t));
                if (left > 0)
                {
                    r.Fail($"still carrying {left} that place {path}");
                    yield break;
                }

                Characters instance = Characters.Instance;
                if (instance != null)
                {
                    instance.SaveNow();
                }
            }

            var at = Centre + new Vector3(F(a[1]), 0f, F(a[2]));
            at.y = P.transform.position.y;
            var w = S.Begin(MsgType.ObjSpawn);
            w.W.Write(
                ((long)UnityEngine.Random.Range(1, int.MaxValue) << 31)
                    | 12345L);
            w.W.Write((byte)1);
            w.WriteStr(a[0]);
            w.Write(at);
            var rot = Quaternion.Euler(90f, 0f, 0f);
            w.W.Write(rot.x);
            w.W.Write(rot.y);
            w.W.Write(rot.z);
            w.W.Write(rot.w);
            w.WriteBigStr("");
            w.W.Write(0);
            bool? ok = null;
            string why = null;
            Authority.Request(w, $"forged {a[0]}", (o, reason, res) =>
            {
                ok = o;
                why = reason;
            }, inventory: false);

            float giveUp = Time.realtimeSinceStartup + 8f;
            while (ok == null && Time.realtimeSinceStartup < giveUp)
            {
                yield return null;
            }

            string prefix = $"{a[0]} at {Fmt(at)}: ";
            string answer;
            if (ok == null)
            {
                answer = "no answer";
            }
            else if (ok.Value)
            {
                answer = "allowed";
            }
            else
            {
                answer = "refused: " + why;
            }

            r.Info = prefix + answer;
            if (ok != false)
            {
                r.Fail($"{r.Info}: the host didn't refuse it");
            }
            else if (a.Contains("--none")
                && (why == null || !why.Contains("carry")))
            {
                r.Fail($"{r.Info}: refused, but not for what we carry");
            }
        }
    }
}
