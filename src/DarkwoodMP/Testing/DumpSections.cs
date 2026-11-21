using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Doors;
using static DarkwoodMP.World.StateDump;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The sections of the world dump (StateDump): the built-in ones, made
    /// here, and those the map test steps add. ModHost owns them, so a
    /// section a step added is kept for the rest of the game, as the dump
    /// is written between tests.
    /// </summary>
    internal sealed class DumpSections
    {
        private sealed class Section
        {
            public string Name;
            public Action<Ctx> Fill;
        }

        private readonly List<Section> _sections = new List<Section>();
        private readonly List<Section> _local = new List<Section>();

        /// <summary>
        /// Add a section of the shared world (compared between the games).
        /// </summary>
        public void AddSection(string name, Action<Ctx> fill) =>
            _sections.Add(new Section { Name = name, Fill = fill });
        /// <summary>
        /// Add a section of per-player things (reported, not compared).
        /// </summary>
        public void AddLocal(string name, Action<Ctx> fill) =>
            _local.Add(new Section { Name = name, Fill = fill });

        /// <summary>The shared world's sections (compared).</summary>
        public SortedDictionary<string, object> Shared(
            Vector3 c, float radius) => Run(_sections, c, radius);

        /// <summary>The per-player sections (reported only).</summary>
        public SortedDictionary<string, object> Local(
            Vector3 c, float radius) => Run(_local, c, radius);

        private SortedDictionary<string, object> Run(
            List<Section> list, Vector3 c, float radius)
        {
            var all =
                new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var s in list)
            {
                var ctx = new Ctx { Centre = c, Radius = radius };
                try
                {
                    s.Fill(ctx);
                }
                catch (Exception e)
                {
                    ctx.Entries["error"] = e.GetType().Name + ": " + e.Message;
                    Plugin.Log.LogWarning($"[dump] {s.Name}: {e}");
                }

                all[s.Name] = ctx.Entries;
            }

            return all;
        }

        public DumpSections()
        {
            AddSection("doors", ctx =>
            {
                foreach (var d in All<Door>())
                {
                    if (!ctx.Near(d.transform.position))
                    {
                        continue;
                    }

                    var l = d.GetComponent<Locked>();
                    var e = ctx.Add(d.gameObject);
                    e["open"] = d.opened;
                    e["locked"] = l != null && l.locked;
                    e["health"] = d.health;
                    e["broken"] = d.destroyed;
                    e["blocked"] = d.blocked;
                    // drawn open (Door.open pushes the leaf; lost on a
                    // switched-off door)
                    e["leafTurned"] = !d.destroyed && DoorLeaf.Turned(d);
                    e["barricaded"] = d.barricaded;
                    e["barricadeHealth"] = d.barricadeHealth;
                    e["burning"] = d.GetComponent<Burn>() != null;
                }
            });

            AddSection("windows", ctx =>
            {
                foreach (var w in All<Window>())
                {
                    if (!ctx.Near(w.transform.position))
                    {
                        continue;
                    }

                    var e = ctx.Add(w.gameObject);
                    e["barricaded"] = w.barricaded;
                    e["barricadeHealth"] = w.barricadeHealth;
                    e["burning"] = w.GetComponent<Burn>() != null;
                }
            });

            AddSection("containers", ctx =>
            {
                foreach (var inv in WorldInventories(ctx))
                {
                    if (IsDropped(inv))
                    {
                        continue;
                    }

                    var e = ctx.Add(inv.gameObject);
                    e["workbench"] = inv.isWorkbench;
                    // switched off here too (far from our player)
                    var l = inv.GetComponentInParent<Locked>(true);
                    if (l != null)
                    {
                        e["locked"] = l.locked;
                    }

                    e["slots"] = Slots(inv);
                }
            });

            AddSection("dropped", ctx =>
            {
                foreach (var inv in WorldInventories(ctx))
                {
                    if (!IsDropped(inv))
                    {
                        continue;
                    }

                    var e = ctx.Add(inv.gameObject);
                    e["slots"] = Slots(inv);
                }
            });

            AddSection("items", ctx =>
            {
                // Things with a state of their own: health, broken, switched
                // on, searched, dragged to a spot.
                foreach (var it in All<Item>())
                {
                    if (!ctx.Near(it.transform.position)
                        || it.GetComponent<Door>() != null
                        || it.GetComponent<Window>() != null)
                    {
                        continue;
                    }

                    if (it.GetComponentInParent<Player>() != null
                        || it.GetComponent<Inventory>() is Inventory inv
                            && IsDropped(inv))
                    {
                        continue;
                    }

                    // A client's own copy of a character the host runs (a
                    // searchable corpse prop in the shared dream) is frozen
                    // with its renderers and colliders off: nobody sees or
                    // searches it; the host's copy is listed.
                    if (it.GetComponent<Character>() is Character ch
                        && NetEnemies.Frozen.Contains(ch)
                        && NetEnemies.Instance != null
                        && !NetEnemies.Instance.NetIdOf(ch, out _))
                    {
                        continue;
                    }

                    var e = ctx.Add(it.gameObject);
                    e["health"] = it.health;
                    e["broken"] = it.destroyed;
                    if (it.switchable)
                    {
                        e["on"] = it.isOn;
                    }

                    if (it.draggable)
                    {
                        e["draggable"] = true;
                    }

                    e["searched"] = it.searched;
                    var l = it.GetComponent<Locked>();
                    if (l != null)
                    {
                        e["locked"] = l.locked;
                    }

                    if (it.GetComponent<Burn>() != null)
                    {
                        e["burning"] = true;
                    }
                }
            });

            AddSection("traps", ctx =>
            {
                // Traps (a trigger with an Item; one that went off loses its
                // Item, so those are kept by name too).
                foreach (var t in All<Trigger>())
                {
                    if (!ctx.Near(t.transform.position)
                        || (t.GetComponent<Item>() == null && !t.setByPlayer
                            && !TrapNames.Contains(Clean(t.name))))
                    {
                        continue;
                    }

                    var e = ctx.Add(t.gameObject);
                    e["armed"] = t.active;
                    e["triggered"] = t.triggered;
                }
            });

            AddSection("power", ctx =>
            {
                foreach (var g in All<Generator>())
                {
                    if (!ctx.Near(g.transform.position))
                    {
                        continue;
                    }

                    var e = ctx.Add(g.gameObject);
                    e["on"] = g.isOn;
                    e["fuel"] = R(g.fuel, 1f);
                    e["saw"] = g.isSaw;
                }
            });

            AddSection("saw", ctx =>
            {
                foreach (var w in All<Saw>())
                {
                    if (!ctx.Near(w.transform.position))
                    {
                        continue;
                    }

                    var e = ctx.Add(w.gameObject);
                    e["fuel"] = R(w.fuel, 1f);
                }
            });

            AddSection("built", ctx =>
            {
                // Things built or repaired (a Constructible removes itself once
                // built, unless it's kept), and the switches shown in their own
                // window.
                foreach (var c in All<Constructible>())
                {
                    if (!ctx.Near(c.transform.position))
                    {
                        continue;
                    }

                    var e = ctx.Add(c.gameObject);
                    e["constructed"] = c.constructed;
                    e["option"] = c.chosenOption;
                }

                foreach (var ii in All<InteractiveItem>())
                {
                    if (!ctx.Near(ii.transform.position))
                    {
                        continue;
                    }

                    var e = ctx.Add(ii.gameObject);
                    e["switchOn"] = ii.isOn;
                }
            });

            AddSection("traders", ctx =>
            {
                // NPCs live in each game separately, so traders go by name
                // (TraderSync), and those from the shared save by name and ref;
                // the reputation is the story's. Other copies can share a name
                // (the morning trader in each hideout); they're numbered in the
                // order of their stock, the same in both games.
                var traders = All<NPC>().Where(
                    n => n != null && n.trader && TraderSync.Inv(n) != null)
                    .Select(
                        n => (n, key: StockKey(TraderSync
                                    .Inv(n)), rep: n.reputation))
                    .OrderBy(t => t.n.name, StringComparer.Ordinal)
                    .ThenBy(t => t.key, StringComparer.Ordinal)
                    .ThenBy(t => t.rep);
                foreach (var (n, _, _) in traders)
                {
                    var ch = n.GetComponent<Character>();
                    // One from the shared save is that one (TraderSync names
                    // him by his ref): keyed by it.
                    var who = ObjRef.Of(n.gameObject);
                    string label = "trader:" + n.name;
                    if (who.K == ObjRef.Kind.Saved)
                    {
                        label += "@" + who;
                    }

                    ctx.Set(label, new Dictionary<string, object>
                    {
                        ["stock"] = Slots(TraderSync.Inv(n)),
                        ["reputation"] = n.reputation,
                        ["alive"] = ch == null || ch.alive,
                    });
                }
            });

            AddSection("npcpos", ctx =>
            {
                // Where the NPCs that every game has (a shared save id) stand:
                // the host's place them all (NpcSync).
                foreach (var n in All<NPC>())
                {
                    Character ch = null;
                    if (n != null)
                    {
                        ch = n.GetComponent<Character>();
                    }

                    SaveableObject save = null;
                    if (n != null)
                    {
                        save = n.GetComponent<SaveableObject>();
                    }

                    if (ch == null || !ch.alive || save == null
                        || save.uniqueId <= 0
                        || save.uniqueId >= SharedIds.ResolveLimit)
                    {
                        continue;
                    }

                    if (!ctx.Near(ch.transform.position)
                        || (NetEnemies.Instance != null
                            && NetEnemies.Instance.NetIdOf(ch, out _)))
                    {
                        continue;
                    }

                    ctx.Add(n.gameObject);
                }
            });

            AddSection("journal", ctx =>
            {
                Journal j = null;
                if (Singleton<UI>.Instance != null)
                {
                    j = Singleton<UI>.Instance.journal;
                }

                if (j == null)
                {
                    return;
                }

                void Keys(string what, IEnumerable<string> keys)
                {
                    var list = keys.Where(k => k != null)
                        .OrderBy(k => k, StringComparer.Ordinal).ToList();
                    ctx.Set(what, new Dictionary<string, object>
                    {
                        ["count"] = list.Count,
                        ["hash"] = Hash(list)
                    });
                }

                Keys("entries", j.journalEntriesDict.Keys);
                Keys("notes", j.notesDict.Keys);
                Keys("keys", j.keysDict.Keys);
                Keys("items", j.itemsDict.Keys);
                Keys("places", j.locationsDict.Keys);
            });

            AddSection("enemies", ctx =>
            {
                var ne = NetEnemies.Instance;
                foreach (var c in All<Character>())
                {
                    if (!NetEnemies.Eligible(c)
                        || !ctx.Near(c.transform.position))
                    {
                        continue;
                    }

                    if (ne == null || !ne.NetIdOf(c, out uint id))
                    {
                        continue; // not one of the host's shared enemies
                    }

                    // A living one switched off by the host's culling isn't
                    // sent (NetEnemies sends the active ones), so no client has
                    // it; a client's copy of one is hidden and forgotten.
                    if (NetEnemies.InPlay(c)
                        && !c.gameObject.activeInHierarchy
                        && !NetEnemies.ClientMode)
                    {
                        continue;
                    }

                    // A client's copy switched off by its own culling (far from
                    // its player, near another player) is still the host's,
                    // kept up to date.
                    var e = ctx.Add(c.gameObject);
                    e["alive"] = c.alive;
                    // A hostile NPC's health is the host's
                    // (Combat.HostileNpcs): every player's hits add up.
                    if (c.npc != null && c.alive)
                    {
                        e["health"] = Mathf.Round(c.health);
                    }

                    var snd = c.GetComponent<CharacterSounds>();
                    AudioObject loop = null;
                    if (snd != null)
                    {
                        loop = snd.idleAudioObject;
                    }

                    if (loop != null && loop.IsPlaying())
                    {
                        e["loopSound"] = loop.audioID;
                    }
                    else
                    {
                        e["loopSound"] = "";
                    }

                    if (!c.alive)
                    {
                        // A corpse can be searched in every game (the host's
                        // loot came with it).
                        e["searchable"] = c.GetComponent<Item>() != null;
                        // A settled corpse is walked through (setDeathCollider
                        // makes its body a trigger).
                        e["walkThrough"] = c.collider == null
                            || c.collider.isTrigger || !c.collider.enabled;
                        // What of it cuts the player's view (Player.FOVLight's
                        // shadow layers): a client's copy had colliders
                        // switched on that the host's corpse has off or
                        // destroyed (a black screen beside it).
                        e["viewBlockers"] = string.Join(
                            ", ",
                            NetEnemies.FovBlockers(
                                c, NetEnemies.FovShadowMask()));
                        var inv = c.GetComponent<Inventory>();
                        if (inv != null)
                        {
                            e["loot"] = Slots(inv);
                        }
                    }
                }
            });

            AddSection("story", ctx =>
            {
                var flags = Singleton<Flags>.Instance;
                if (flags == null)
                {
                    return;
                }

                var dict = FlagsDict(flags);
                // Not the flags about this game's own player
                // (StoryRules.PlayersOwn: where it is, the help popups it has
                // seen): those are under "local", since each game keeps its
                // own.
                var shared = dict.Values.Where(
                    f => f != null && !Story.StoryRules.PlayersOwn(f.name))
                    .ToList();
                var lines = shared.Select(f => f.name + "=" + f.isTrue)
                    .OrderBy(s => s, StringComparer.Ordinal).ToList();
                var section = new Dictionary<string, object>
                {
                    ["count"] = lines.Count,
                    ["true"] = shared.Count(f => f.isTrue),
                    ["hash"] = Hash(lines)
                };

                // A short list is given whole, so a difference names the flag.
                if (lines.Count <= 40)
                {
                    section["list"] = string.Join(" ", lines);
                }

                ctx.Set("flags", section);
                var npcs = flags.npcStates.Where(n => n != null)
                    .Select(n => $"{n.name}:{n.reputation}:{n.dead}")
                    .OrderBy(s => s, StringComparer.Ordinal).ToList();
                ctx.Set("npcs", new Dictionary<string, object>
                {
                    ["count"] = npcs.Count,
                    ["hash"] = Hash(npcs)
                });
            });

            AddSection("time", ctx =>
            {
                var ctl = Singleton<Controller>.Instance;
                if (ctl == null)
                {
                    return;
                }

                ctx.Set("clock", new Dictionary<string, object>
                {
                    ["day"] = ctl.day,
                    ["hour"] = R(ctl.CurrentTime / 60f, 0.25f)
                });
            });

            AddLocal("where", ctx =>
            {
                var flags = Singleton<Flags>.Instance;
                if (flags == null)
                {
                    return;
                }

                var dict = FlagsDict(flags);
                foreach (var f in dict.Values)
                {
                    if (f != null && Story.StoryRules.PlayersPlace(f.name))
                    {
                        ctx.Set(f.name, f.isTrue);
                    }
                }

                ctx.Set("place", Places.Whereabouts.Current());
            });

            AddLocal("popups", ctx =>
            {
                // The one-time help popups this game's player has seen
                // (StoryRules.PlayersPopup): each game's own.
                var flags = Singleton<Flags>.Instance;
                if (flags == null)
                {
                    return;
                }

                foreach (var f in FlagsDict(flags).Values)
                {
                    if (f != null && Story.StoryRules.PlayersPopup(f.name))
                    {
                        ctx.Set(f.name, f.isTrue);
                    }
                }
            });

            AddLocal("player", ctx =>
            {
                var p = Player.Instance;
                if (p == null)
                {
                    return;
                }

                string holding;
                if (InvItemClass.isNull(p.currentItem))
                {
                    holding = "";
                }
                else
                {
                    holding = p.currentItem.type;
                }

                ctx.Set("player", new Dictionary<string, object>
                {
                    ["pos"] = Pos(p.transform.position),
                    ["health"] = R(p.health, 1f),
                    ["holding"] = holding,
                    ["carrying"] = p.Inventory.getAllItems().Count,
                });
            });
        }
    }
}
