using System.Collections;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for a story NPC's corpse (tools/tests/map_scenario_corpse.py): the chicken lady, killed by
    /// the client, must lie dead and be searchable in every game, and what she carries is taken once.
    /// </summary>
    public static class MapScenarioCorpse
    {
        private static Player P => Player.Instance;
        /// <summary>The story name (NPC.name) of the NPC the steps found alive, for its state once its corpse lost its NPC part.</summary>
        private static string _story;

        private static Flags.NPCState State(string name)
        {
            var c = Copies(name).FirstOrDefault(x => x.npc != null);
            if (c != null)
            {
                _story = c.npc.name;
            }
            Flags instance = Singleton<Flags>.Instance;
            if (instance != null)
            {
                string story = _story;
                if (story == null)
                {
                    story = name;
                }
                return instance.getNPCState(story);
            }
            return null;
        }

        public static void Register()
        {
            Add("sn-info", SnInfo, timeout: 10f);
            Add("sn-goto", SnGoto, timeout: 120f);
            Add("sn-kill", SnKill, timeout: 90f);
            Add("sn-corpse", SnCorpse, timeout: 30f);
            Add("sn-away", SnAway, timeout: 120f);
            Add("sn-spawn-own", SnSpawnOwn, timeout: 10f);
            Add("sn-loot", SnLoot, timeout: 30f);
            Add("sn-looted", SnLooted, timeout: 15f);
            Add("cw-dog", CwDog, timeout: 20f);
            Add("cw-open", CwOpen, timeout: 40f);
            Add("cw-break", CwBreak, timeout: 10f);
            Add("cw-remove", CwRemove, timeout: 10f);
            Add("cw-closed", CwClosed, timeout: 15f);
        }

        /// <summary>Every copy of the NPC called NAME (its NPC.name), on or off.</summary>
        private static Character[] Copies(string name) =>
            Resources.FindObjectsOfTypeAll<Character>()
                .Where(c => c != null && c.gameObject.scene.IsValid() && ((c.npc != null && c.npc.name == name) || c.name.StartsWith(name, System.StringComparison.OrdinalIgnoreCase)))
                .ToArray();

        private static string Props(Character c)
        {
            var sv = c.GetComponent<SaveableObject>();
            var loc = c.transform.GetLocation();
            var ne = Combat.NetEnemies.Instance;
            uint id = 0;
            bool net = ne != null && ne.NetIdOf(c, out id);
            string text = "-";
            if (c.npc != null)
            {
                string oneNote = "";
                if (c.npc.onlyOneInstance)
                {
                    oneNote = ", one instance";
                }
                text = c.npc.name + oneNote;
            }
            string head = $"{c.name} (npc {text}) at {Fmt(c.transform.position)} ({Fmt(c.transform.position - Centre)} from the centre), ";
            bool on = c.gameObject.activeInHierarchy;
            bool alive = c.alive;
            int saveId = -1;
            if (sv != null)
            {
                saveId = sv.uniqueId;
            }
            string place = "-";
            if (loc != null)
            {
                place = Core.getTrueLocationName(loc.name);
            }
            bool searchable = c.GetComponent<Item>() != null;
            int carried = -1;
            if (c.inventory != null)
            {
                carried = c.inventory.slots.Count(s => s != null && !InvItemClass.isNull(s.invItem));
            }
            string enemy = "no";
            if (net)
            {
                enemy = "#" + id;
            }
            return head +
                   $"on {on}, alive {alive}, save id {saveId}, in {place}, " +
                   $"searchable {searchable}, inventory {carried}, " +
                   $"host enemy {enemy}, frozen {Combat.NetEnemies.Frozen.Contains(c)}, hidden {Combat.NetEnemies.Hidden.Contains(c)}";
        }

        private static Character Nearest(string name) =>
            Copies(name).OrderBy(c => (c.transform.position - P.transform.position).sqrMagnitude).FirstOrDefault();

        /// <summary>"sn-info NAME": every copy of the NPC, and its story state.</summary>
        private static IEnumerator SnInfo(string[] a, Result r)
        {
            var st = State(a[0]);
            string text = "none";
            if (st != null)
            {
                text = $"dead {st.dead} deadID {st.deadID} attackedID {st.attackedID}";
            }
            r.Info = $"state {text}; " +
                     string.Join("; ", Copies(a[0]).Select(Props));
            yield break;
        }

        /// <summary>"sn-goto NAME [DIST]": our player walks to DIST (default 250) from the NPC.</summary>
        private static IEnumerator SnGoto(string[] a, Result r)
        {
            var c = Nearest(a[0]);
            if (c == null)
            {
                r.Fail($"no {a[0]} in this world");
                yield break;
            }
            float dist = 250f;
            if (a.Length > 1)
            {
                dist = F(a[1]);
            }
            var to = c.transform.position;
            yield return Walk(to, dist);
            yield return new WaitForSeconds(2f);
            r.Info = $"at {Fmt(P.transform.position)}; {Props(c)}";
        }

        /// <summary>"sn-kill NAME": our player hits the NPC until it's dead (a client's hits go to the host).</summary>
        private static IEnumerator SnKill(string[] a, Result r)
        {
            var c = Nearest(a[0]);
            if (c == null)
            {
                r.Fail($"no {a[0]} here");
                yield break;
            }
            int hits = 0;
            while (c != null && c.alive && hits < 60)
            {
                yield return Walk(c.transform.position, 80f);
                hits++;
                AsPlayer(() => c.getHit(60f, P.transform, false, true, true, true, false, false, false));
                yield return new WaitForSecondsRealtime(0.4f);
            }
            yield return new WaitForSecondsRealtime(4f); // a client settles the corpse 2 s after it dies
            if (c != null)
            {
                r.Info = $"{hits} hits: {Props(c)}";
            }
            else
            {
                r.Info = $"{hits} hits: gone";
            }
            if (c == null || c.alive)
            {
                r.Fail($"not dead: {r.Info}");
            }
        }

        /// <summary>
        /// "sn-corpse NAME": the NPC's corpse is here near our player: dead, switched on and searchable, and it has
        /// the loot its death put in it (Character.setDeathCollider: NPC.deathInventory).
        /// </summary>
        private static IEnumerator SnCorpse(string[] a, Result r)
        {
            Character c = null;
            for (float t = 0f; t < 8f; t += 0.25f)
            {
                c = Copies(a[0]).Where(x => !x.alive && x.gameObject.activeInHierarchy)
                    .OrderBy(x => (x.transform.position - P.transform.position).sqrMagnitude).FirstOrDefault();
                if (c != null && c.GetComponent<Item>() != null)
                {
                    break;
                }
                yield return new WaitForSeconds(0.25f);
            }
            var st = State(a[0]);
            string all = string.Join("; ", Copies(a[0]).Select(Props));
            if (c == null)
            {
                bool? dead = null;
                if (st != null)
                {
                    dead = st.dead;
                }
                int? deadID = null;
                if (st != null)
                {
                    deadID = st.deadID;
                }
                r.Fail($"no corpse shown here (state dead {dead} deadID {deadID}): {all}");
                yield break;
            }
            bool? dead2 = null;
            if (st != null)
            {
                dead2 = st.dead;
            }
            int? deadID2 = null;
            if (st != null)
            {
                deadID2 = st.deadID;
            }
            r.Info = $"state dead {dead2} deadID {deadID2}; {Props(c)}";
            if (c.GetComponent<Item>() == null)
            {
                r.Fail($"not searchable: {r.Info}");
            }
            else if (Combat.NetEnemies.Hidden.Contains(c))
            {
                r.Fail($"hidden (a frozen copy of ours, not shown as the host's): {r.Info}");
            }
            else if (Combat.NetEnemies.ClientMode)
            {
                Combat.NetEnemies enemies = Combat.NetEnemies.Instance;
                if (enemies == null || !enemies.NetIdOf(c, out _))
                {
                    r.Fail($"not the host's corpse (its loot wouldn't be the host's): {r.Info}");
                }
            }
        }

        /// <summary>"sn-away NAME DIST": our player walks DIST away from the NPC and back (its area switched off and on).</summary>
        private static IEnumerator SnAway(string[] a, Result r)
        {
            var c = Nearest(a[0]);
            if (c == null)
            {
                r.Fail($"no {a[0]} here");
                yield break;
            }
            var at = c.transform.position;
            var back = P.transform.position;
            yield return Walk(at + new Vector3(0f, 0f, -F(a[1])), 100f);
            yield return new WaitForSeconds(3f);
            yield return Walk(back, 60f);
            yield return new WaitForSeconds(3f);
            r.Info = $"back at {Fmt(P.transform.position)}";
        }

        /// <summary>
        /// "sn-spawn-own NAME DX DZ": the NPC prefab Characters/NPC/NAME at the centre plus (DX, DZ), made by this game
        /// alone, with a save id from its own counter (a client's counter is moved on first, so the ids differ): as a
        /// location each game loads for itself while playing together gives its NPCs ids of each game's own.
        /// </summary>
        private static IEnumerator SnSpawnOwn(string[] a, Result r)
        {
            var sm = Singleton<SaveManager>.Instance;
            if (Net.Session.Instance != null && !Net.Session.Instance.IsHost && sm != null)
            {
                sm.currentUniqueID += 5000;
            }
            var at = Centre + new Vector3(F(a[1]), 0f, F(a[2]));
            var go = Core.AddPrefab("Characters/NPC/" + a[0], at, Quaternion.Euler(90f, 0f, 0f), null, worldSpace: true);
            if (go == null)
            {
                r.Fail($"no prefab Characters/NPC/{a[0]}");
                yield break;
            }
            Core.addToSaveable(go, isDynamic: true, assignID: true);
            var c = go.GetComponent<Character>();
            if (c != null)
            {
                c.isActive = true;
            }
            yield return new WaitForSecondsRealtime(1f);
            if (c == null || c.npc == null)
            {
                r.Fail($"{a[0]} isn't an NPC");
                yield break;
            }
            _story = c.npc.name;
            r.Info = Props(c);
        }

        private static Character Corpse(string name) =>
            Copies(name).Where(x => !x.alive && x.gameObject.activeInHierarchy && x.GetComponent<Item>() != null && x.inventory != null)
                .OrderBy(x => (x.transform.position - P.transform.position).sqrMagnitude).FirstOrDefault();

        private static int Things(Inventory inv) => inv.slots.Count(s => s != null && !InvItemClass.isNull(s.invItem));

        /// <summary>"sn-loot NAME": our player takes everything from the NPC's corpse, as from any container.</summary>
        private static IEnumerator SnLoot(string[] a, Result r)
        {
            var c = Corpse(a[0]);
            if (c == null)
            {
                r.Fail($"no searchable corpse of {a[0]} here");
                yield break;
            }
            yield return Walk(c.transform.position, 80f);
            var inv = c.inventory;
            P.openedItemInventory = inv; // as if its window were open
            yield return new WaitForSeconds(0.6f);
            var took = new System.Collections.Generic.List<string>();
            foreach (var slot in inv.slots.Where(s => s != null && !InvItemClass.isNull(s.invItem)).ToList())
            {
                var item = new InvItemClass(slot.invItem);
                AsPlayer(() =>
                {
                    slot.removeItem();
                    P.Inventory.addItemTypeToPlayer(item.type, item.amount, dropIfNoRoom: true);
                });
                took.Add($"{item.type} x{item.amount}");
                yield return new WaitForSeconds(0.3f);
            }
            yield return new WaitForSeconds(0.6f);
            P.openedItemInventory = null;
            yield return new WaitForSeconds(1f);
            r.Info = $"took [{string.Join(", ", took)}] from {Props(c)}";
            if (took.Count == 0)
            {
                r.Fail($"nothing in it: {r.Info}");
            }
            else if (Things(inv) > 0)
            {
                r.Fail($"something is still in it: {r.Info}");
            }
        }

        /// <summary>"sn-looted NAME": the NPC's corpse here is searchable and empty (another player took what it had).</summary>
        private static IEnumerator SnLooted(string[] a, Result r)
        {
            Character c = null;
            for (float t = 0f; t < 6f; t += 0.25f)
            {
                c = Corpse(a[0]);
                if (c != null && Things(c.inventory) == 0)
                {
                    break;
                }
                yield return new WaitForSeconds(0.25f);
            }
            if (c == null)
            {
                r.Fail($"no searchable corpse of {a[0]} here");
                yield break;
            }
            r.Info = Props(c);
            if (Things(c.inventory) > 0)
            {
                r.Fail($"it still has things in it: {r.Info}");
            }
        }

        // ---- R03: a container's window when the container goes while it's open -------------------------------------

        private static Character _cwDog;      // host: the dog killed by cw-dog
        private static Inventory _cwInv;      // client: the corpse's inventory our player opened (cw-open)
        private static GameObject _cwWindow;  // client: its window (Inventory.thisUI, under the UI)

        /// <summary>"cw-dog" (host): a dog 250 from the client's player, killed by our player; its corpse is the
        /// client's to keep once it has lain a few seconds (NetEnemies.CorpseLinger).</summary>
        private static IEnumerator CwDog(string[] a, Result r)
        {
            var proxy = MapScenarioEnemies.ClientProxy();
            if (proxy == null)
            {
                r.Fail("no client player");
                yield break;
            }
            var c = MapScenarioFixChecks.SpawnAt(proxy.transform.position + new Vector3(250f, 0f, 0f));
            if (c == null)
            {
                r.Fail("couldn't spawn a dog");
                yield break;
            }
            yield return new WaitForSeconds(1.5f);
            yield return MapScenarioItems2.KillAsPlayer(c, r, 1.5f);
            _cwDog = c;
            r.Info = $"{c.name} #{MapScenarioFixChecks.IdOf(c)} at {Fmt(c.transform.position)}, alive {c.alive}";
        }

        /// <summary>The newest of the host's dead dogs here whose corpses the host told us to keep (the highest net
        /// id: cw-dog's; the story NPCs' kept corpses of the earlier steps aren't dogs).</summary>
        private static Character KeptCorpse() =>
            Object.FindObjectsOfType<Character>()
                .Where(c => c != null && !c.alive && c.name.StartsWith("Dog") && Combat.NetEnemies.Instance != null && Combat.NetEnemies.Instance.KeptCorpse(c)
                            && c.GetComponent<Item>() != null && c.GetComponentInChildren<Inventory>(true) != null)
                .OrderByDescending(c => MapScenarioFixChecks.IdOf(c)).FirstOrDefault();

        /// <summary>"cw-open" (client): the nearest kept corpse of the host's enemies, opened as the player opens it
        /// (Item.openInventory), and left open.</summary>
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
                     $"ours open {ReferenceEquals(P.openedItemInventory, inv)}, windows [{string.Join(", ", Windows())}]";
            if (!inv.open || inv.thisUI == null || !ReferenceEquals(P.openedItemInventory, inv))
            {
                r.Fail($"its window didn't open: {r.Info}");
            }
        }

        /// <summary>"cw-break" (client): the open corpse's Inventory destroyed directly, with no window closed first
        /// (the safety net's case, Containers.DropOrphanedWindows: a way of going that CloseIfOpen doesn't cover).</summary>
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

        /// <summary>"cw-remove" (host): the dog's corpse removed from the world (the client's copy goes too).</summary>
        private static IEnumerator CwRemove(string[] a, Result r)
        {
            if (_cwDog == null)
            {
                r.Fail("no dog of cw-dog's");
                yield break;
            }
            r.Info = $"{_cwDog.name} #{MapScenarioFixChecks.IdOf(_cwDog)} removed";
            Object.Destroy(_cwDog.gameObject);
            _cwDog = null;
            yield return new WaitForSeconds(1.5f);
        }

        /// <summary>The container windows under the UI (Inventory.show names them ItemInv and DeathDrop).</summary>
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

        /// <summary>"cw-closed" (client): the container our player had open is gone, its window with it, no
        /// container window is left under the UI, and the player's open container is a real null (not a destroyed
        /// object, which Player.closeInventory skips).</summary>
        private static IEnumerator CwClosed(string[] a, Result r)
        {
            bool ok = false;
            for (float t = 0f; t < 8f && !ok; t += 0.25f)
            {
                ok = _cwInv == null && _cwWindow == null && Windows().Count == 0
                     && ReferenceEquals(P.openedItemInventory, null) && ReferenceEquals(P.openedItemInventory2, null);
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
            string head = $"container gone {_cwInv == null}, its window gone {_cwWindow == null}, windows under the UI [{string.Join(", ", Windows())}], ";
            string second = "set";
            if (ReferenceEquals(P.openedItemInventory2, null))
            {
                second = "null";
            }
            r.Info = head + $"openedItemInventory {opened}, openedItemInventory2 {second}";
            if (!ok)
            {
                r.Fail($"a window was left: {r.Info}");
            }
            _cwInv = null;
            _cwWindow = null;
        }
    }
}
