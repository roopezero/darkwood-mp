using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>Steps for story flags, reputation and the journal.</summary>
    public static partial class MapScenarioWorld
    {
        // ---- the story, traders, enemies ------------------------------------

        /// <summary>
        /// "flag N": flip the Nth story flag (Flags.setFlag).
        /// </summary>
        private static IEnumerator Flag(string[] a, Result r)
        {
            var ordered =
                StateDump.FlagsDict(Singleton<Flags>.Instance).Values.Where(
                    f => f != null).OrderBy(
                        f => f.name, StringComparer.Ordinal);
            int index = 0;
            if (a.Length > 0)
            {
                index = int.Parse(a[0]);
            }

            var flag = ordered.Skip(index).FirstOrDefault();
            if (flag == null)
            {
                r.Fail("no such flag");
                yield break;
            }

            bool to = !flag.isTrue;
            Singleton<Flags>.Instance.setFlag(flag.name, to);
            yield return new WaitForSeconds(0.5f);
            r.Info = $"{flag.name} -> {flag.isTrue}";
            if (flag.isTrue != to)
            {
                r.Fail($"{r.Info}: didn't change");
            }
        }

        /// <summary>
        /// "reputation N DELTA": change the Nth NPC's reputation.
        /// </summary>
        private static IEnumerator Reputation(string[] a, Result r)
        {
            var npc = Singleton<Flags>.Instance.npcStates.Where(n => n != null)
                .OrderBy(n => n.name, StringComparer.Ordinal)
                .Skip(int.Parse(a[0])).FirstOrDefault();
            if (npc == null)
            {
                r.Fail("no such NPC");
                yield break;
            }

            int was = npc.reputation;
            npc.reputation += int.Parse(a[1]);
            yield return new WaitForSeconds(0.5f);
            r.Info = $"{npc.name} reputation {was} -> {npc.reputation}";
        }

        /// <summary>
        /// "journal entry|note": add a journal entry or note this game hasn't
        /// got, as the game adds them.
        /// </summary>
        private static IEnumerator JournalStep(string[] a, Result r)
        {
            var journal = Singleton<UI>.Instance.journal;
            var jdb = Singleton<JournalDatabase>.Instance;
            if (jdb != null && jdb.journalEntriesDict.Count == 0)
            {
                jdb.populateDict();
            }

            if (jdb == null)
            {
                r.Fail("no journal database");
                yield break;
            }

            if (a.Length > 0 && a[0] == "note")
            {
                string note = jdb.notesDict.Keys.OrderBy(
                    k => k, StringComparer.Ordinal).FirstOrDefault(
                        k => !journal.notesDict.ContainsKey(k));
                if (note == null)
                {
                    r.Fail("every note is in already");
                    yield break;
                }

                journal.addNote(new Journal.Note { type = note });
                r.Info = $"note {note}";
                if (!journal.notesDict.ContainsKey(note))
                {
                    r.Fail($"{r.Info}: not added");
                }
            }
            else
            {
                string entry = jdb.journalEntriesDict.Keys.OrderBy(
                    k => k, StringComparer.Ordinal).FirstOrDefault(
                        k => !journal.journalEntriesDict.ContainsKey(k));
                if (entry == null)
                {
                    r.Fail("every entry is in already");
                    yield break;
                }

                journal.addJournalEntry(entry);
                r.Info = $"entry {entry}";
                if (!journal.journalEntriesDict.ContainsKey(entry))
                {
                    r.Fail($"{r.Info}: not added");
                }
            }

            yield return new WaitForSeconds(0.5f);
        }
    }
}
