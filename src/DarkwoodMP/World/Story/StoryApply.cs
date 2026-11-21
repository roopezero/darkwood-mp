using System;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Story
{
    /// <summary>
    /// Puts another game's story entry into this game (FlagsSync): flags,
    /// NPC states and dialogues through the game's own calls, the journal's
    /// entries with its popups, and the deaths an NPC state brings.
    /// </summary>
    internal static class StoryApply
    {
        internal static void Apply(string key, string value)
        {
            var f = Singleton<Flags>.Instance;
            var kind = StoryKeys.KindOf(key);
            if (kind == StoryKey.Malformed)
            {
                throw new FormatException("the key is too short");
            }

            if (kind == StoryKey.Karma)
            {
                f.karmaPoints = int.Parse(value);
                return;
            }

            if (kind == StoryKey.Journal)
            {
                ApplyJournal(key[1], key.Substring(3), value);
                return;
            }

            string name = key.Substring(2);
            switch (kind)
            {
                case StoryKey.Flag:
                    var home = StoryRules.FlagHomeOf(name);
                    if (home == FlagHome.PlayersOwn)
                    {
                        break;
                    }

                    var parts = value.Split('|');
                    if (home == FlagHome.Mod)
                    {
                        SetModFlag(
                            f, name, bool.Parse(parts[0]), int.Parse(parts[1]));
                        break;
                    }

                    f.setFlag(name, bool.Parse(parts[0]));
                    f.setFlag(name, int.Parse(parts[1]));
                    break;
                case StoryKey.Npc:
                    var state = f.getNPCState(name);
                    bool wasDead = state != null && state.dead;
                    if (state == null)
                    {
                        f.npcStates.Add(
                            state =
                                JsonConvert.DeserializeObject<Flags.NPCState>(
                                    value));
                    }
                    else
                    {
                        JsonConvert.PopulateObject(value, state);
                    }

                    if (state.dead && !wasDead)
                    {
                        KillNpc(name, state.deadID);
                    }

                    RefreshReputation(name);
                    break;
                case StoryKey.Dialogue:
                    var dialogue = f.getDialogue(name);
                    if (dialogue != null)
                    {
                        JsonConvert
                            .DeserializeObject<CharacterDialogue.SaveState>(
                                value).loadValues(dialogue);
                    }

                    break;
            }
        }

        /// <summary>
        /// Set one of the mod's own flags (StoryKeys.ModPrefix) in
        /// Flags.flagsDict, which the game's setFlag won't take.
        /// Flags.SaveState saves every entry there, so they're in the host's
        /// save and a joiner's snapshot, and the game itself never reads
        /// them.
        /// </summary>
        internal static void SetModFlag(
            Flags f, string name, bool on, int amount)
        {
            if (!f.flagsDict.TryGetValue(name, out var flag) || flag == null)
            {
                f.flagsDict[name] = flag = new Flags.Flag { name = name };
            }

            flag.isTrue = on;
            flag.amount = amount;
        }

        /// <summary>
        /// The trade window with this NPC is open: its reputation label shows
        /// the new value at once.
        /// </summary>
        internal static void RefreshReputation(string npc)
        {
            NPC talked = null;
            if (Player.Instance != null)
            {
                talked = Player.Instance.talkedToNPC;
            }

            if (talked == null || talked.name != npc
                || talked.inventory == null
                || talked.inventory.reputationText == null)
            {
                return;
            }

            if (!talked.inventory.reputationText.gameObject.activeInHierarchy)
            {
                return;
            }

            try
            {
                talked.inventory.refreshReputation();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't refresh {npc}'s reputation: {e.Message}");
            }
        }

        /// <summary>
        /// A place another player found (walking into it, or reading a map:
        /// Location.discoverMe) is shown on our map too, as discoverMe shows it
        /// on theirs: Map.showElement on the location's MapElement.
        /// </summary>
        private static void ShowOnMap(string name)
        {
            try
            {
                WorldGenerator instance = Singleton<WorldGenerator>.Instance;
                Location loc = null;
                if (instance != null)
                {
                    loc = instance.getLocation(name);
                }

                MapElement el = null;
                if (loc != null)
                {
                    el = loc.GetComponent<MapElement>();
                }

                if (el == null || el.isOnMap)
                {
                    return;
                }

                Map map = Singleton<Map>.Instance;
                if (map != null)
                {
                    map.showElement(el);
                }

                Plugin.Log.LogInfo($"Journal: {name} shown on the map");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't show {name} on the map: {e.Message}");
            }
        }

        /// <summary>
        /// Another player's new journal entry: added here if it isn't yet
        /// (removed, for "-").
        /// </summary>
        private static void ApplyJournal(char kind, string name, string value)
        {
            UI instance = Singleton<UI>.Instance;
            Journal j = null;
            if (instance != null)
            {
                j = instance.journal;
            }

            if (j == null)
            {
                return;
            }

            var take = JournalRules.Take(kind, value, StoryRead.SharedDream);
            if (take == JournalTake.Remove)
            {
                bool gone = kind switch
                {
                    'e' => j.journalEntriesDict.Remove(name),
                    'n' => j.notesDict.Remove(name),
                    'k' => j.keysDict.Remove(name),
                    'i' => j.itemsDict.Remove(name),
                    _ => false,
                };

                if (gone)
                {
                    Plugin.Log.LogInfo(
                        $"Journal: another player no longer has {name} (handed "
                            + "over): removed here too");
                }

                return;
            }

            if (take == JournalTake.OtherDream)
            {
                // Found in a shared dream that has ended here (or not begun):
                // the game would never drop it.
                Plugin.Log.LogInfo(
                    $"Journal: another player's {name} from a dream we aren't "
                        + "in: not taken");
                return;
            }

            if (take == JournalTake.Unknown)
            {
                return;
            }

            bool quiet = false;
            switch (kind)
            {
                case 'e':
                    if (j.journalEntriesDict.ContainsKey(name))
                    {
                        return;
                    }

                    j.journalEntriesDict[name] =
                        JsonConvert.DeserializeObject<Journal.JournalEntry>(
                            value);
                    break;
                case 'n':
                    if (j.notesDict.ContainsKey(name))
                    {
                        return;
                    }

                    j.notesDict[name] =
                        JsonConvert.DeserializeObject<Journal.Note>(value);
                    break;
                case 'k':
                    if (j.keysDict.ContainsKey(name))
                    {
                        return;
                    }

                    j.keysDict[name] =
                        JsonConvert.DeserializeObject<Journal.Key>(value);
                    break;
                case 'i':
                    if (j.itemsDict.ContainsKey(name))
                    {
                        return;
                    }

                    j.itemsDict[name] =
                        JsonConvert.DeserializeObject<Journal.Item>(value);
                    break;
                case 'l':
                    if (j.locationsDict.ContainsKey(name))
                    {
                        return;
                    }

                    j.locationsDict[name] = name;
                    quiet = QuietPlace(name);

                    ShowOnMap(name);
                    break;
                case 'd':
                    if (j.getLocationDirection(name) != null)
                    {
                        return;
                    }

                    j.locationDirections.Add(
                        JsonConvert
                            .DeserializeObject<Journal.LocationDirection>(
                                value));
                    break;
            }

            string what = JournalRules.Noun(kind);
            Plugin.Log.LogInfo(
                "Journal: another player's " + $"{what} {name}");
            string popup = JournalRules.Popup(kind, quiet);
            if (popup != null && WorldShare.WorldReady)
            {
                try
                {
                    j.showJournalInfoPopup(popup, name);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't show the journal popup for {name}: "
                            + $"{e.Message}");
                }
            }
        }

        /// <summary>Whether the game finds this place without a popup
        /// (Location.discoverMe): it's marked noDiscoverPopup, or it counts as
        /// found from the start.</summary>
        private static bool QuietPlace(string name)
        {
            foreach (var loc in Resources.FindObjectsOfTypeAll<Location>())
            {
                if (loc != null && Core.getTrueLocationName(loc.name) == name
                    && (loc.noDiscoverPopup || loc.discoveredOnStart))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Someone killed this NPC in their game: it dies here too, if it's
        /// still alive. Several copies of a story NPC can stand at once (the
        /// wolfman as a trader in more than one place, and as a night
        /// attacker); the one that died has the save id in deadID
        /// (Character.die), so when every game knows that id only that copy
        /// dies. The game itself removes the other copies only as they next
        /// start (NPC.Start, onlyOneInstance).
        /// </summary>
        private static void KillNpc(string name, int deadId)
        {
            foreach (var npc in UnityEngine.Object.FindObjectsOfType<NPC>())
            {
                if (!NpcStateRules.Kills(FactsOf(npc, name, deadId)))
                {
                    continue;
                }

                var c = npc.GetComponent<Character>();

                Plugin.Log.LogInfo(
                    $"{name} was killed in another player's game");
                Coop.Guards.Replaying = true;
                try
                {
                    c.die();
                }
                finally
                {
                    Coop.Guards.Replaying = false;
                }
            }
        }

        /// <summary>
        /// One NPC here, when another game's state says the NPC of this
        /// name died, with this save id. A copy of one the host runs as an
        /// enemy dies with the host's state (Combat.NetEnemies).
        /// </summary>
        private static NpcCopyFacts FactsOf(NPC npc, string name, int deadId)
        {
            if (npc.name != name)
            {
                return new NpcCopyFacts { SameName = false };
            }

            var sv = npc.GetComponent<SaveableObject>();
            int saveId = 0;
            if (sv != null)
            {
                saveId = sv.uniqueId;
            }

            var c = npc.GetComponent<Character>();
            bool alive = c != null && c.alive;
            bool puppet = alive && Combat.NetEnemies.Instance != null
                && Combat.NetEnemies.Instance.IsPuppet(c, out _);
            return new NpcCopyFacts
            {
                SameName = true,
                ById = NpcStateRules.ById(deadId, SharedIds.SendLimit),
                Saved = sv != null,
                SaveId = saveId,
                DeadId = deadId,
                Alive = alive,
                Puppet = puppet,
            };
        }
    }
}
