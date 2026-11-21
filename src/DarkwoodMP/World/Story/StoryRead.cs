using System.Collections.Generic;
using DarkwoodMP.World.Dreaming;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DarkwoodMP.World.Story
{
    /// <summary>
    /// Reads this game's story as FlagsSync sends it: one entry per key
    /// (StoryKeys), its value as text.
    /// </summary>
    internal static class StoryRead
    {
        private static readonly
            AccessTools.FieldRef<Flags, Dictionary<string, Flags.Flag>>
            FlagsDict = AccessTools
                .FieldRefAccess<Flags, Dictionary<string, Flags.Flag>>(
                    "flagsDict");

        /// <summary>
        /// Whether our game is in a dream every game is in (DreamSync.Shared),
        /// so what's found in it is shared. Once the dream ends here, it isn't.
        /// (Going from one prologue stage to the next isn't an end: the game
        /// keeps what was found in the first stage until the dreaming ends,
        /// Dreams.endDreaming.)
        /// </summary>
        internal static bool SharedDream
        {
            get
            {
                var d = Singleton<Dreams>.Instance;
                return DreamSync.SharedNow != null && d != null && d.dreaming;
            }
        }

        /// <summary>
        /// Our journal's entry for a "je:", "jn:", "jk:" or "ji:" key, as
        /// Snapshot writes it (null: none).
        /// </summary>
        internal static string OwnJournal(string key)
        {
            UI instance = Singleton<UI>.Instance;
            Journal j = null;
            if (instance != null)
            {
                j = instance.journal;
            }

            if (j == null || key.Length < 4)
            {
                return null;
            }

            string name = key.Substring(3);
            object e = null;
            switch (key[1])
            {
                case 'e':
                    if (j.journalEntriesDict.TryGetValue(name, out var x))
                    {
                        e = x;
                    }

                    break;
                case 'n':
                    if (j.notesDict.TryGetValue(name, out var n))
                    {
                        e = n;
                    }

                    break;
                case 'k':
                    if (j.keysDict.TryGetValue(name, out var k))
                    {
                        e = k;
                    }

                    break;
                case 'i':
                    if (j.itemsDict.TryGetValue(name, out var i))
                    {
                        e = i;
                    }

                    break;
            }

            if (e != null)
            {
                return JsonConvert.SerializeObject(e);
            }

            return null;
        }

        internal static Dictionary<string, string> Snapshot(bool host)
        {
            var f = Singleton<Flags>.Instance;
            if (f == null)
            {
                return null;
            }

            var d = new Dictionary<string, string>();
            if (host)
            {
                d["k"] = f.karmaPoints.ToString();
            }

            foreach (var kv in FlagsDict(f))
            {
                if (kv.Value != null && !StoryRules.PlayersOwn(kv.Key))
                {
                    d["f:" + kv.Key] = $"{kv.Value.isTrue}|{kv.Value.amount}";
                }
            }

            foreach (var n in f.npcStates)
            {
                if (n == null || n.name == null)
                {
                    continue;
                }

                d["n:" + n.name] = NpcJson(n);
                d["r:" + n.name] = n.reputation.ToString();
            }

            foreach (var dl in f.dialogues)
            {
                if (dl != null)
                {
                    d["d:" + dl.name] = JsonConvert.SerializeObject(
                        new CharacterDialogue.SaveState(dl));
                }
            }

            UI instance = Singleton<UI>.Instance;
            Journal j = null;
            if (instance != null)
            {
                j = instance.journal;
            }

            if (j != null)
            {
                // what's found in a dream: only a shared dream's (see the
                // class)
                bool dream = SharedDream;
                foreach (var e in j.journalEntriesDict.Values)
                {
                    if (e != null && (dream || !e.inDream))
                    {
                        d["je:" + e.type] = JsonConvert.SerializeObject(e);
                    }
                }

                foreach (var e in j.notesDict.Values)
                {
                    if (e != null && (dream || !e.inDream))
                    {
                        d["jn:" + e.type] = JsonConvert.SerializeObject(e);
                    }
                }

                foreach (var e in j.keysDict.Values)
                {
                    if (e != null && (dream || !e.inDream))
                    {
                        d["jk:" + e.type] = JsonConvert.SerializeObject(e);
                    }
                }

                foreach (var e in j.itemsDict.Values)
                {
                    if (e != null && (dream || !e.inDream))
                    {
                        d["ji:" + e.type] = JsonConvert.SerializeObject(e);
                    }
                }

                foreach (var name in j.locationsDict.Keys)
                {
                    d["jl:" + name] = "";
                }

                foreach (var e in j.locationDirections)
                {
                    if (e != null && e.locationName != null)
                    {
                        d["jd:" + e.locationName] =
                            JsonConvert.SerializeObject(e);
                    }
                }
            }

            return d;
        }

        /// <summary>
        /// An NPC's "n:" value: its state as JSON, without the reputation (that
        /// goes as "r:").
        /// </summary>
        internal static string NpcJson(Flags.NPCState n)
        {
            var jo = JObject.FromObject(n);
            jo.Remove("reputation");
            return jo.ToString(Formatting.None);
        }
    }
}
