using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;
using Object = UnityEngine.Object;

namespace DarkwoodMP.World
{
    /// <summary>Issue #2: a dead enemy's sounds.</summary>
    public static partial class MapScenarioIssues
    {
        // ---- #2 dead enemy's sounds (and #3's attack) -----------------------

        // host: the dog spawn-dog made
        private static Character _dog;
        // host: its health then
        private static float _dogHealth;
        // client: the copy attack-dog went for
        private static uint _dogId;
        // client: its death cry's audio ID
        private static string _dogDeath;

        /// <summary>
        /// "spawn-dog" (host): a Dog beside the centre, not set on anyone.
        /// </summary>
        private static IEnumerator SpawnDog(string[] a, Result r)
        {
            // Clear of the stations: a dog among them walked into a trap and
            // died before anyone hit it.
            Vector3 spot = Centre + new Vector3(0f, 0f, 1200f);
            string type = "Dog";
            if (a.Length > 0)
            {
                type = a[0];
            }

            var c = MapScenarioFixChecks.SpawnAt(spot, type);
            if (c == null)
            {
                r.Fail("couldn't spawn a dog");
                yield break;
            }

            _dog = c;
            yield return new WaitForSeconds(1.5f);
            _dogHealth = c.health; // once it's set up (it starts at 0)
            r.Info =
                $"{c.name} #{MapScenarioFixChecks.IdOf(c)} at "
                    + $"{Fmt(c.transform.position)}, health {c.health}";
        }

        /// <summary>
        /// "attack-dog" (client): walk to the host's dog and swing what we hold
        /// at it a few times.
        /// </summary>
        private static IEnumerator AttackDog(string[] a, Result r)
        {
            var p = Player.Instance;
            var ne = NetEnemies.Instance;
            Character dog = null;
            float until = Time.realtimeSinceStartup + 6f;
            while (dog == null && Time.realtimeSinceStartup < until)
            {
                // The one spawn-dog just made: by where it puts it, a copy made
                // from its prefab (the host spawned it while playing), then the
                // newest net id there. A save dog or the far module's dog can
                // be nearer the centre; hitting one killed it instead.
                var spot = Centre + new Vector3(0f, 0f, 1200f);
                dog = Object.FindObjectsOfType<Character>().Where(
                    c => c.alive && c.name.StartsWith("Dog") && ne != null
                        && ne.NetIdOf(c, out _)
                        && StateDump.Flat(c.transform.position - spot) < 400f)
                    .OrderBy(c =>
                    {
                        if (ne.MadeHere(c))
                        {
                            return 0;
                        }

                        return 1;
                    })
                    .ThenByDescending(c =>
                    {
                        ne.NetIdOf(c, out uint id);
                        return id;
                    }).FirstOrDefault();

                if (dog == null)
                {
                    yield return new WaitForSeconds(0.3f);
                }
            }

            if (dog == null)
            {
                r.Fail("no copy of the host's dog here");
                yield break;
            }

            ne.NetIdOf(dog, out _dogId);
            if (dog.sounds != null)
            {
                _dogDeath = dog.sounds.death;
            }
            else
            {
                _dogDeath = null;
            }

            // A copy has its own idle loop and the host's relayed sounds while
            // it lives: the death check below is only worth something if there
            // was something to stop.
            bool loud = dog.GetComponentsInChildren<AudioObject>(true).Any(
                ao => ao != null && ao.IsPlaying());
            // Darkwood has no bare-handed attack: a new character takes a
            // weapon out first (the game's own hotbar select), so the swing is
            // a real one.
            string type = "pitchfork";
            if (a.Length > 0)
            {
                type = a[0];
            }

            if (InvItemClass.isNull(p.currentItem))
            {
                InvSlot slot = p.getCurrentlySelectedHotbarSlot();
                if (slot == null)
                {
                    slot = p.Hotbar.slots[0];
                }

                if (!InvItemClass.isNull(slot.invItem))
                {
                    InvSlot chosen = p.Hotbar.slots.FirstOrDefault(
                        s => InvItemClass.isNull(s.invItem));
                    if (chosen == null)
                    {
                        chosen = slot;
                    }

                    slot = chosen;
                }

                slot.inventory = p.Hotbar;
                if (InvItemClass.isNull(slot.invItem))
                {
                    slot.createItem(type, 1);
                }

                foreach (var s in p.Hotbar.slots)
                {
                    s.deselect();
                }

                slot.select(noiseless: true);
                float t = Time.realtimeSinceStartup + 5f;
                while ((InvItemClass.isNull(p.currentItem) || p.switchingItem)
                    && Time.realtimeSinceStartup < t)
                {
                    yield return null;
                }

                if (InvItemClass.isNull(p.currentItem))
                {
                    r.Fail($"couldn't take the {type} out");
                    yield break;
                }
            }

            int swings = 0;
            for (int i = 0; i < 6 && dog != null && dog.alive; i++)
            {
                yield return Walk(dog.transform.position, 70f);
                Swing(p, dog.transform.position);
                swings++;
                yield return new WaitForSeconds(0.6f);
            }

            r.Info =
                $"{swings} swing(s) with {ItemName(p.currentItem)} at "
                    + $"{dog.name} #{_dogId} (sounds playing on it before: "
                    + $"{loud})";
        }

        /// <summary>
        /// "dog-hurt" (host): the client's swings took health off the dog here.
        /// </summary>
        private static IEnumerator DogHurt(string[] a, Result r)
        {
            yield return new WaitForSeconds(0.5f);
            if (_dog == null)
            {
                r.Fail("no dog (spawn-dog first)");
                yield break;
            }

            r.Info =
                $"{_dog.name}: health {_dogHealth} -> {_dog.health}, alive "
                    + $"{_dog.alive}";
            if (!(_dog.health < _dogHealth))
            {
                string cause =
                    "it died of something else before the client's attack: ";
                if (_dog.alive)
                {
                    cause = "the client's attack did nothing here: ";
                }

                r.Fail(cause + r.Info);
            }
        }

        /// <summary>"kill-dog" (host): our player kills the dog.</summary>
        private static IEnumerator KillDog(string[] a, Result r)
        {
            var p = Player.Instance;
            if (_dog == null)
            {
                r.Fail("no dog");
                yield break;
            }

            if (_dog.alive)
            {
                AsPlayer(
                    () => _dog.getHit(
                        _dog.health + 500f, p.transform, false, true, false));
            }

            yield return new WaitForSeconds(1f);
            r.Info = $"{_dog.name} alive {_dog.alive}";
            if (_dog.alive)
            {
                r.Fail("it's still alive");
            }
        }

        /// <summary>
        /// "dead-dog" (client): our copy of the dog is dead, and silent but for
        /// its death cry.
        /// </summary>
        private static IEnumerator DeadDog(string[] a, Result r)
        {
            var missing = new List<string>();
            yield return WaitLog(0, new[]
            {
                $@"\[enemy\] {_dogId} died: sounds stopped",
                $@"#{_dogId} died: its corpse can be searched here"
            }, 8f, missing);

            // past the death cry and any late sound from the host
            yield return new WaitForSeconds(2f);
            var dog = MapScenarioFixChecks.ById(_dogId);
            if (dog == null)
            {
                r.Fail($"the dog #{_dogId} is gone here");
                yield break;
            }

            var snd = dog.GetComponent<CharacterSounds>();
            AudioObject loop = null;
            if (snd != null)
            {
                loop = snd.idleAudioObject;
            }

            var playing = dog.GetComponentsInChildren<AudioObject>(true)
                .Where(ao => ao != null && ao.IsPlaying())
                .Select(ao => ao.audioID).ToList();
            var stray = playing.Where(id => id != _dogDeath).ToList();
            string name = dog.name;
            uint number = _dogId;
            bool alive = dog.alive;
            string idle = "off";
            if (loop != null && loop.IsPlaying())
            {
                idle = loop.audioID;
            }

            r.Info =
                $"{name} #{number} alive {alive}, idle loop {idle}, playing "
                    + $"[{string.Join(", ", playing)}], death cry {_dogDeath}";
            if (dog.alive)
            {
                r.Fail($"it's alive here: {r.Info}");
            }
            else if (missing.Count > 0)
            {
                r.Fail($"not in the log: {string.Join(", ", missing)}");
            }
            else if (loop != null && loop.IsPlaying())
            {
                r.Fail($"its idle loop {loop.audioID} still plays");
            }
            else if (stray.Count > 0)
            {
                r.Fail(
                    "sounds still playing on the corpse: "
                        + $"{string.Join(", ", stray)}");
            }
        }

        /// <summary>
        /// "hidden-silent" (client): our own characters stay out of the host's
        /// world. A spawn point makes nothing here (the host's spawns come from
        /// the host), and our hidden frozen characters, switched off and on
        /// again as the game does when their culling node or location is
        /// entered, start no sound (a dead dog camp's dogs barked on).
        /// </summary>
        private static IEnumerator HiddenSilent(string[] a, Result r)
        {
            var p = Player.Instance;
            var holder = new GameObject("dwmp_test_spawn_holder");
            holder.transform.position =
                p.transform.position + new Vector3(300f, 0f, 0f);
            var sp =
                new GameObject("dwmp_test_spawnpoint")
                    .AddComponent<CharacterSpawnPoint>();
            sp.transform.SetParent(holder.transform, false);
            sp.type = CharacterType.Dog;
            sp.spawnChance = 1f;
            sp.actuallySpawn();
            bool spawned = sp.spawnedCharacter != null;
            if (spawned)
            {
                Object.Destroy(sp.spawnedCharacter.gameObject);
            }

            Object.Destroy(holder);

            // One of our own, as a location's spawn would have made it before:
            // frozen and hidden by its Start. A Dog has no idle loop of its
            // own: one with a loop is picked, so the check means something.
            string kind = null;
            if (a.Length > 0)
            {
                kind = a[0];
            }

            GameObject gameObject =
                Resources.Load("Prefabs/Characters/Dog") as GameObject;
            CharacterSounds dogSnd = null;
            if (gameObject != null)
            {
                dogSnd = gameObject.GetComponent<CharacterSounds>();
            }

            string dogLoops;
            if (dogSnd == null)
            {
                dogLoops = "none";
            }
            else
            {
                dogLoops =
                    $"idle '{dogSnd.idleLoop}', sleeping '{dogSnd.sleeping}', "
                        + $"aggressive '{dogSnd.aggressive}', idle sound "
                        + $"'{dogSnd.idle}'";
            }

            if (kind == null)
            {
                foreach (var k in new[]
                {
                    "Dog",
                    "DogMutated",
                    "Villager",
                    "ChomperRed",
                    "Mimic",
                    "Banshee"
                }.Concat(Enum.GetNames(typeof(CharacterType))))
                {
                    GameObject prefab =
                        Resources.Load("Prefabs/Characters/" + k) as GameObject;
                    CharacterSounds s = null;
                    if (prefab != null)
                    {
                        s = prefab.GetComponent<CharacterSounds>();
                    }

                    if (s != null && !string.IsNullOrEmpty(s.idleLoop))
                    {
                        kind = k;
                        break;
                    }
                }
            }

            string wanted = kind;
            if (wanted == null)
            {
                wanted = "Dog";
            }

            var res = Resources.Load("Prefabs/Characters/" + wanted);
            GameObject go = null;
            if (res != null)
            {
                go = Core.AddPrefab(
                    res, p.transform.position + new Vector3(250f, 0f, 250f),
                    Quaternion.Euler(90f, 0f, 0f), null, worldSpace: true);
            }

            Character mine = null;
            if (go != null)
            {
                mine = go.GetComponent<Character>();
            }

            if (mine == null)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }

                r.Fail($"couldn't make a {wanted} of our own here");
                yield break;
            }

            var save = mine.GetComponent<SaveableObject>();
            if (save != null)
            {
                save.dontSave = true;
            }

            yield return null;
            yield return null;
            bool hidden = NetEnemies.Hidden.Contains(mine);

            var toggled = new List<Character>();
            foreach (var c in NetEnemies.Hidden.ToList())
            {
                if (c == null || !c.gameObject.activeInHierarchy)
                {
                    continue;
                }

                c.gameObject.SetActive(false);
                // CharacterSounds.OnEnable: its idle loop
                c.gameObject.SetActive(true);
                toggled.Add(c);
            }

            yield return new WaitForSeconds(0.5f);
            var loud = new List<string>();
            foreach (var c in toggled)
            {
                foreach (var ao in c.GetComponentsInChildren<AudioObject>(true))
                {
                    if (ao != null && ao.IsPlaying())
                    {
                        loud.Add($"{c.name}: {ao.audioID}");
                    }
                }
            }

            string idle = null;
            if (mine.sounds != null)
            {
                idle = mine.sounds.idleLoop;
            }

            string text = "nothing";
            if (spawned)
            {
                text = "a dog";
            }

            string idleName = idle;
            if (idleName == null)
            {
                idleName = "none";
            }

            r.Info =
                $"spawn point made {text}; our {mine.name} hidden {hidden}, "
                    + $"idle loop {idleName} (a Dog's: {dogLoops}); " +
                     $"{toggled.Count} hidden character(s) switched off and "
                    + $"on, playing [{string.Join(", ", loud)}]";
            NetEnemies.Hidden.Remove(mine);
            NetEnemies.Frozen.Remove(mine);
            Object.Destroy(mine.gameObject);
            if (spawned)
            {
                r.Fail(
                    "a spawn point made a character of our own in the host's "
                        + $"world: {r.Info}");
            }
            else if (!hidden)
            {
                r.Fail($"our own character wasn't hidden: {r.Info}");
            }
            else if (!toggled.Contains(mine))
            {
                r.Fail(
                    "our own character wasn't switched on to be checked: "
                        + $"{r.Info}");
            }
            else if (loud.Count > 0)
            {
                r.Fail($"hidden characters play sounds: {r.Info}");
            }
        }

        /// <summary>
        /// "search-corpse" (client): open the dead dog's body and take what's
        /// in it (as take-item does).
        /// </summary>
        private static IEnumerator SearchCorpse(string[] a, Result r)
        {
            var p = Player.Instance;
            var dog = MapScenarioFixChecks.ById(_dogId);
            if (dog == null)
            {
                r.Fail($"the dog #{_dogId} is gone here");
                yield break;
            }

            var item = dog.GetComponent<Item>();
            var inv = dog.GetComponentInChildren<Inventory>(true);
            if (item == null || inv == null)
            {
                r.Fail(
                    $"its corpse can't be searched here (Item {item != null}, "
                        + $"Inventory {inv != null})");
                yield break;
            }

            yield return Walk(dog.transform.position, 90f);
            int mark = LogMark();
            p.openedItemInventory = inv;
            // the host's contents come back (ContainerSlotsAll)
            yield return new WaitForSeconds(1.5f);
            var slot =
                inv.slots.FirstOrDefault(s => !InvItemClass.isNull(s.invItem));
            string took = "nothing in it";
            if (slot != null)
            {
                var it = new InvItemClass(slot.invItem);
                AsPlayer(() =>
                {
                    slot.removeItem();
                    p.Inventory.addItemTypeToPlayer(
                        it.type, it.amount, dropIfNoRoom: true);
                });

                took = $"took {it.type} x{it.amount}";
                yield return new WaitForSeconds(0.8f);
            }

            p.openedItemInventory = null;
            yield return new WaitForSeconds(1.5f);
            var refused = LogLines().Skip(mark)
                .FirstOrDefault(l => l.Contains("refused"));
            r.Info =
                $"{dog.name} #{_dogId} {ObjRef.Of(inv.gameObject)}: {took}";
            if (refused != null)
            {
                r.Fail($"{r.Info}; {refused}");
            }
        }
    }
}
