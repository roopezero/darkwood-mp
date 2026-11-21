using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    public static partial class MapScenarioGaps
    {
        // ---- 61 journal -----------------------------------------------------

        private static string _jKind, _jType;

        /// <summary>"journal-pick key|item": the first key (or quest item) by
        /// name that our journal hasn't got. Run in both games, which have the
        /// same journal, so they pick the same.</summary>
        private static IEnumerator JournalPick(string[] a, Result r)
        {
            var j = Singleton<UI>.Instance.journal;
            var db = Singleton<JournalDatabase>.Instance;
            if (a.Length > 0)
            {
                _jKind = a[0];
            }
            else
            {
                _jKind = "key";
            }

            Dictionary<string, string>.KeyCollection keys;
            if (_jKind == "key")
            {
                keys = db.keysDict.Keys;
            }
            else
            {
                keys = db.itemsDict.Keys;
            }

            if (keys != null)
            {
                _jType = keys.Where(k =>
                {
                    if (string.IsNullOrEmpty(k))
                    {
                        return false;
                    }

                    if (_jKind == "key")
                    {
                        return !j.keysDict.ContainsKey(k);
                    }

                    return !j.itemsDict.ContainsKey(k);
                })
                    .OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();
            }
            else
            {
                _jType = null;
            }

            if (_jType == null)
            {
                r.Fail(
                    $"no {_jKind} our journal hasn't got (of {keys.Count} in "
                        + "the database)");
            }

            r.Info = $"{_jKind} {_jType}";
            yield break;
        }

        /// <summary>
        /// "journal-add": our player picks up the picked key or item (as
        /// KeyReference/QuestItemReference.pickup).
        /// </summary>
        private static IEnumerator JournalAdd(string[] a, Result r)
        {
            if (_jType == null)
            {
                r.Fail("journal-pick found nothing");
                yield break;
            }

            var j = Singleton<UI>.Instance.journal;
            if (_jKind == "key")
            {
                j.keysDict[_jType] = new Journal.Key { type = _jType };
            }
            else
            {
                j.itemsDict[_jType] = new Journal.Item { type = _jType };
            }

            r.Info = $"{_jKind} {_jType} added";
            yield break;
        }

        /// <summary>
        /// "journal-remove": handed over in a dialogue (DialogueWindow's
        /// removeItem outcome takes it out of the journal).
        /// </summary>
        private static IEnumerator JournalRemove(string[] a, Result r)
        {
            if (_jType == null)
            {
                r.Fail("journal-pick found nothing");
                yield break;
            }

            var j = Singleton<UI>.Instance.journal;
            bool had;
            if (_jKind == "key")
            {
                had = j.keysDict.Remove(_jType);
            }
            else
            {
                had = j.itemsDict.Remove(_jType);
            }

            if (!had)
            {
                r.Fail($"our journal hadn't got {_jType}");
            }

            r.Info = $"{_jKind} {_jType} removed";
            yield break;
        }

        /// <summary>
        /// "journal-has yes|no": whether our journal has the picked one
        /// (waiting up to 8 s for it to arrive).
        /// </summary>
        private static IEnumerator JournalHas(string[] a, Result r)
        {
            if (_jType == null)
            {
                r.Fail("journal-pick found nothing");
                yield break;
            }

            var j = Singleton<UI>.Instance.journal;
            bool want = a.Length == 0 || a[0] == "yes";
            bool Has()
            {
                if (_jKind == "key")
                {
                    return j.keysDict.ContainsKey(_jType);
                }

                return j.itemsDict.ContainsKey(_jType);
            }

            float until = Time.realtimeSinceStartup + 8f;
            while (Has() != want && Time.realtimeSinceStartup < until)
            {
                yield return new WaitForSecondsRealtime(0.5f);
            }

            string kind = _jKind;
            string type = _jType;
            string answer = "not here";
            if (Has())
            {
                answer = "here";
            }

            r.Info = $"{kind} {type}: {answer}";
            if (Has() != want)
            {
                string text = "gone";
                if (want)
                {
                    text = "here";
                }

                r.Fail(r.Info + $", wanted {text}");
            }

            // Stay a moment, so that a game that still had it doesn't send it
            // back in its next snapshot.
            yield return new WaitForSecondsRealtime(
                Story.StoryRules.Interval + 0.5f);
            if (Has() != want)
            {
                string way = "back";
                if (want)
                {
                    way = "off";
                }

                r.Fail(
                    $"{_jType} came {way} after "
                        + $"{Story.StoryRules.Interval + 0.5f:0.#} s");
            }
        }
    }
}
