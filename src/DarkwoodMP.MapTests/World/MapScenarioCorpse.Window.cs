using System.Collections;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioCorpse
    {
        // ---- R03: a container's window when it goes while it's open ---------
        // When the container goes while it's open.

        // host: the dog killed by cw-dog
        private static Character _cwDog;
        // client: the corpse's inventory our player opened (cw-open)
        private static Inventory _cwInv;
        // client: its window (Inventory.thisUI, under the UI)
        private static GameObject _cwWindow;

        /// <summary>"cw-dog" (host): a dog 250 from the client's player, killed
        /// by our player; its corpse is the client's to keep once it has lain a
        /// few seconds (NetEnemies.CorpseLinger).</summary>
        private static IEnumerator CwDog(string[] a, Result r)
        {
            var proxy = MapScenarioEnemies.ClientProxy();
            if (proxy == null)
            {
                r.Fail("no client player");
                yield break;
            }

            var c = MapScenarioFixChecks.SpawnAt(
                proxy.transform.position + new Vector3(250f, 0f, 0f));
            if (c == null)
            {
                r.Fail("couldn't spawn a dog");
                yield break;
            }

            yield return new WaitForSeconds(1.5f);
            yield return MapScenarioItems2.KillAsPlayer(c, r, 1.5f);
            _cwDog = c;
            r.Info =
                $"{c.name} #{MapScenarioFixChecks.IdOf(c)} at "
                    + $"{Fmt(c.transform.position)}, alive {c.alive}";
        }

        /// <summary>The newest of the host's dead dogs here whose corpses the
        /// host told us to keep (the highest net id: cw-dog's; the story NPCs'
        /// kept corpses of the earlier steps aren't dogs).</summary>
        private static Character KeptCorpse() =>
            Object.FindObjectsOfType<Character>()
                .Where(
                    c => c != null && !c.alive && c.name.StartsWith("Dog")
                        && Coop.Current != null
                        && Coop.Current.Enemies.KeptCorpse(c)
                        && c.GetComponent<Item>() != null
                        && c.GetComponentInChildren<Inventory>(true) != null)
                .OrderByDescending(c => MapScenarioFixChecks.IdOf(c))
                .FirstOrDefault();

        /// <summary>"cw-open" (client): the nearest kept corpse of the host's
        /// enemies, opened as the player opens it (Item.openInventory), and
        /// left open.</summary>
        private static IEnumerator CwOpen(string[] a, Result r)
        {
            Character c = null;
            for (float t = 0f; t < 20f && c == null; t += 0.5f)
            {
                c = KeptCorpse();
                if (c == null)
                {
                    yield return new WaitForSeconds(0.5f);
                }
            }

            if (c == null)
            {
                r.Fail("no kept corpse of the host's enemies here");
                yield break;
            }

            yield return Walk(c.transform.position, 80f);
            var item = c.GetComponent<Item>();
            var inv = c.GetComponentInChildren<Inventory>(true);
            AsPlayer(() => item.openInventory());
            yield return new WaitForSeconds(1.5f);
            _cwInv = inv;
            _cwWindow = inv.thisUI;
            string name = c.name;
            string spot = Fmt(c.transform.position);
            bool open = inv.open;
            string window = "none";
            if (inv.thisUI != null)
            {
                window = inv.thisUI.name;
            }

            r.Info = $"{name} at {spot}: open {open}, window {window}, " +
                     "ours open "
                + $"{ReferenceEquals(P.openedItemInventory, inv)}, windows "
                + $"[{string.Join(", ", Windows())}]";
            if (!inv.open || inv.thisUI == null
                || !ReferenceEquals(P.openedItemInventory, inv))
            {
                r.Fail($"its window didn't open: {r.Info}");
            }
        }

        /// <summary>"cw-break" (client): the open corpse's Inventory destroyed
        /// directly, with no window closed first (the safety net's case,
        /// Containers.DropOrphanedWindows: a way of going that CloseIfOpen
        /// doesn't cover).</summary>
        private static IEnumerator CwBreak(string[] a, Result r)
        {
            if (_cwInv == null || !_cwInv.open)
            {
                r.Fail("no open corpse here");
                yield break;
            }

            Object.Destroy(_cwInv);
            yield return new WaitForSeconds(0.5f);
            r.Info = "destroyed its Inventory";
        }

        /// <summary>
        /// "cw-remove" (host): the dog's corpse removed from the world (the
        /// client's copy goes too).
        /// </summary>
        private static IEnumerator CwRemove(string[] a, Result r)
        {
            if (_cwDog == null)
            {
                r.Fail("no dog of cw-dog's");
                yield break;
            }

            r.Info =
                $"{_cwDog.name} #{MapScenarioFixChecks.IdOf(_cwDog)} removed";
            Object.Destroy(_cwDog.gameObject);
            _cwDog = null;
            yield return new WaitForSeconds(1.5f);
        }

        /// <summary>
        /// The container windows under the UI (Inventory.show names them
        /// ItemInv and DeathDrop).
        /// </summary>
        private static System.Collections.Generic.List<string> Windows()
        {
            var list = new System.Collections.Generic.List<string>();
            var ui = Singleton<UI>.Instance;
            if (ui == null)
            {
                return list;
            }

            foreach (Transform t in ui.transform)
            {
                if (t != null && (t.name == "ItemInv" || t.name == "DeathDrop"))
                {
                    list.Add(t.name);
                }
            }

            return list;
        }

        /// <summary>"cw-closed" (client): the container our player had open is
        /// gone, its window with it, no container window is left under the UI,
        /// and the player's open container is a real null (not a destroyed
        /// object, which Player.closeInventory skips).</summary>
        private static IEnumerator CwClosed(string[] a, Result r)
        {
            bool ok = false;
            for (float t = 0f; t < 8f && !ok; t += 0.25f)
            {
                ok = _cwInv == null && _cwWindow == null && Windows().Count == 0
                    && ReferenceEquals(P.openedItemInventory, null)
                    && ReferenceEquals(P.openedItemInventory2, null);
                if (!ok)
                {
                    yield return new WaitForSeconds(0.25f);
                }
            }

            string opened;
            if (ReferenceEquals(P.openedItemInventory, null))
            {
                opened = "null";
            }
            else if (P.openedItemInventory == null)
            {
                opened = "a destroyed object";
            }
            else
            {
                opened = P.openedItemInventory.name;
            }

            string head =
                $"container gone {_cwInv == null}, its window gone "
                    + $"{_cwWindow == null}, windows under the UI "
                    + $"[{string.Join(", ", Windows())}], ";
            string second = "set";
            if (ReferenceEquals(P.openedItemInventory2, null))
            {
                second = "null";
            }

            r.Info = head
                + $"openedItemInventory {opened}, openedItemInventory2 "
                + $"{second}";
            if (!ok)
            {
                r.Fail($"a window was left: {r.Info}");
            }

            _cwInv = null;
            _cwWindow = null;
        }
    }
}
