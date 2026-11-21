using System.Collections;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Map test steps for a story NPC's corpse
    /// (tools/tests/map_scenario_corpse.py): the chicken lady, killed by the
    /// client, must lie dead and be searchable in every game, and what she
    /// carries is taken once.
    /// </summary>
    public static partial class MapScenarioCorpse
    {
        private static Player P => Player.Instance;
        /// <summary>
        /// The story name (NPC.name) of the NPC the steps found alive, for its
        /// state once its corpse lost its NPC part.
        /// </summary>
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

        /// <summary>
        /// Every copy of the NPC called NAME (its NPC.name), on or off.
        /// </summary>
        private static Character[] Copies(string name) =>
            Resources.FindObjectsOfTypeAll<Character>()
                .Where(
                    c => c != null && c.gameObject.scene.IsValid()
                        && ((c.npc != null && c.npc.name == name)
                            || c.name.StartsWith(
                                name,
                                System.StringComparison.OrdinalIgnoreCase)))
                .ToArray();

        private static string Props(Character c)
        {
            var sv = c.GetComponent<SaveableObject>();
            var loc = c.transform.GetLocation();
            Combat.NetEnemies ne = null;
            if (Coop.Current != null)
            {
                ne = Coop.Current.Enemies;
            }

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

            string head =
                $"{c.name} (npc {text}) at {Fmt(c.transform.position)} "
                    + $"({Fmt(c.transform.position - Centre)} from the "
                    + "centre), ";
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
                carried = c.inventory.slots.Count(
                    s => s != null && !InvItemClass.isNull(s.invItem));
            }

            string enemy = "no";
            if (net)
            {
                enemy = "#" + id;
            }

            return head +
                   $"on {on}, alive {alive}, save id {saveId}, in {place}, " +
                   $"searchable {searchable}, inventory {carried}, " +
                   $"host enemy {enemy}, frozen "
                + $"{Coop.Current.Enemies.Frozen.Contains(c)}, hidden "
                + $"{Coop.Current.Enemies.Hidden.Contains(c)}";
        }

        private static Character Nearest(string name) =>
            Copies(name)
                .OrderBy(
                    c => (c.transform.position
                            - P.transform.position).sqrMagnitude)
                .FirstOrDefault();

        /// <summary>
        /// "sn-info NAME": every copy of the NPC, and its story state.
        /// </summary>
        private static IEnumerator SnInfo(string[] a, Result r)
        {
            var st = State(a[0]);
            string text = "none";
            if (st != null)
            {
                text =
                    $"dead {st.dead} deadID {st.deadID} attackedID "
                        + $"{st.attackedID}";
            }

            r.Info = $"state {text}; " +
                     string.Join("; ", Copies(a[0]).Select(Props));
            yield break;
        }

        /// <summary>
        /// "sn-goto NAME [DIST]": our player walks to DIST (default 250) from
        /// the NPC.
        /// </summary>
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

        /// <summary>
        /// "sn-kill NAME": our player hits the NPC until it's dead (a client's
        /// hits go to the host).
        /// </summary>
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
                AsPlayer(
                    () => c.getHit(
                        60f, P.transform, false, true, true, true, false, false,
                        false));
                yield return new WaitForSecondsRealtime(0.4f);
            }

            // a client settles the corpse 2 s after it dies
            yield return new WaitForSecondsRealtime(4f);
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
        /// "sn-corpse NAME": the NPC's corpse is here near our player: dead,
        /// switched on and searchable, and it has the loot its death put in it
        /// (Character.setDeathCollider: NPC.deathInventory).
        /// </summary>
        private static IEnumerator SnCorpse(string[] a, Result r)
        {
            Character c = null;
            for (float t = 0f; t < 8f; t += 0.25f)
            {
                c = Copies(a[0]).Where(
                    x => !x.alive && x.gameObject.activeInHierarchy)
                    .OrderBy(
                        x => (x.transform.position
                                - P.transform.position).sqrMagnitude)
                    .FirstOrDefault();
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

                r.Fail(
                    $"no corpse shown here (state dead {dead} deadID "
                        + $"{deadID}): {all}");
                yield break;
            }

            bool? stateDead = null;
            if (st != null)
            {
                stateDead = st.dead;
            }

            int? stateDeadId = null;
            if (st != null)
            {
                stateDeadId = st.deadID;
            }

            r.Info = $"state dead {stateDead} deadID {stateDeadId}; {Props(c)}";
            if (c.GetComponent<Item>() == null)
            {
                r.Fail($"not searchable: {r.Info}");
            }
            else if (Coop.Current.Enemies.Hidden.Contains(c))
            {
                r.Fail(
                    "hidden (a frozen copy of ours, not shown as the host's): "
                        + $"{r.Info}");
            }
            else if (Combat.NetEnemies.ClientMode)
            {
                Combat.NetEnemies enemies = null;
                if (Coop.Current != null)
                {
                    enemies = Coop.Current.Enemies;
                }

                if (enemies == null || !enemies.NetIdOf(c, out _))
                {
                    r.Fail(
                        "not the host's corpse (its loot wouldn't be the "
                            + $"host's): {r.Info}");
                }
            }
        }

        /// <summary>
        /// "sn-away NAME DIST": our player walks DIST away from the NPC and
        /// back (its area switched off and on).
        /// </summary>
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
        /// "sn-spawn-own NAME DX DZ": the NPC prefab Characters/NPC/NAME at the
        /// centre plus (DX, DZ), made by this game alone, with a save id from
        /// its own counter (a client's counter is moved on first, so the ids
        /// differ): as a location each game loads for itself while playing
        /// together gives its NPCs ids of each game's own.
        /// </summary>
        private static IEnumerator SnSpawnOwn(string[] a, Result r)
        {
            var sm = Singleton<SaveManager>.Instance;
            if (!Plugin.Instance.Host.Session.IsHost
                && sm != null)
            {
                sm.currentUniqueID += 5000;
            }

            var at = Centre + new Vector3(F(a[1]), 0f, F(a[2]));
            var go = Core.AddPrefab(
                "Characters/NPC/" + a[0], at, Quaternion.Euler(90f, 0f, 0f),
                null, worldSpace: true);
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
            Copies(name).Where(
                x => !x.alive && x.gameObject.activeInHierarchy
                    && x.GetComponent<Item>() != null && x.inventory != null)
                .OrderBy(
                    x => (x.transform.position
                            - P.transform.position).sqrMagnitude)
                .FirstOrDefault();

        private static int Things(Inventory inv) =>
            inv.slots.Count(s => s != null && !InvItemClass.isNull(s.invItem));

        /// <summary>
        /// "sn-loot NAME": our player takes everything from the NPC's corpse,
        /// as from any container.
        /// </summary>
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
            foreach (var slot in inv.slots.Where(
                s => s != null && !InvItemClass.isNull(s.invItem)).ToList())
            {
                var item = new InvItemClass(slot.invItem);
                AsPlayer(() =>
                {
                    slot.removeItem();
                    P.Inventory.addItemTypeToPlayer(
                        item.type, item.amount, dropIfNoRoom: true);
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

        /// <summary>
        /// "sn-looted NAME": the NPC's corpse here is searchable and empty
        /// (another player took what it had).
        /// </summary>
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
    }
}
