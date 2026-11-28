using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.World.Scripted;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>dive-scan: the triggers and events found.</summary>
    public static partial class MapScenarioDive
    {
        private static string Name(Object o)
        {
            if (o == null)
            {
                return "null";
            }

            return o.name;
        }

        internal static string Req(EventTriggerRequirement q)
        {
            string state = "";
            if (q.type == EventTriggerRequirement.Type.playerState)
            {
                state = "/" + q.playerState;
            }

            return $"{q.type}{state} item={Name(q.itemType)} key='{q.keyType}' "
                + $"n={q.amount} mod={q.activeModifier} "
                + $"dest={Name(q.destTransform)}";
        }

        internal static string Ev(GameEvent e)
        {
            string head =
                $"{e.type} '{e.Value}' '{e.Value2}' "
                    + $"mod={e.activeModifier}/{e.activeModifier2}/"
                    + $"{e.relentlessPursuit} main={e.mainScriptModify} "
                    + $"n={e.amount} delay={e.delay}";
            List<Object> prefabs = e.prefabList;
            if (prefabs == null)
            {
                prefabs = new List<Object>();
            }

            string prefabNames = string.Join(",", prefabs.Select(Name));
            List<GameObject> targets = e.targetGameObjects;
            if (targets == null)
            {
                targets = new List<GameObject>();
            }

            string targetNames = string.Join(",", targets.Select(g =>
            {
                if (g == null)
                {
                    return "null";
                }

                return $"{g.name}@{Fmt(g.transform.position)}";
            }));

            List<string> uniques = e.targetUniqueObjects;
            if (uniques == null)
            {
                uniques = new List<string>();
            }

            string uniqueNames = string.Join(",", uniques);
            List<Location.GetObject> places = e.locationObjects;
            int count = 0;
            if (places != null)
            {
                count = places.Count;
            }

            string note = "";
            if (e.disabled)
            {
                note = " disabled";
            }

            return head + $" prefabs=[{prefabNames}]"
                + $" targets=[{targetNames}|{uniqueNames}]"
                + $" loc={count}{note}";
        }

        /// <summary>
        /// "dive-scan PATTERN": log every trigger (with its requirements) and
        /// every GameEvents under the objects whose root's name contains
        /// PATTERN, and anything anywhere that names an oxygen tank or a dive.
        /// </summary>
        private static IEnumerator Scan(string[] a, Result r)
        {
            string pat = "cellar";
            if (a.Length > 0)
            {
                pat = a[0];
            }

            var sb = new System.Text.StringBuilder();
            int n = 0;
            bool Wanted(Component c)
            {
                if (c == null)
                {
                    return false;
                }

                var root = c.transform.root;
                return c.name.IndexOf(
                    pat, System.StringComparison.OrdinalIgnoreCase) >= 0
                    || root.name.IndexOf(
                        pat, System.StringComparison.OrdinalIgnoreCase) >= 0
                    || c.GetComponentsInParent<Transform>(true).Any(
                        t => t.name.IndexOf(
                            pat,
                            System.StringComparison.OrdinalIgnoreCase) >= 0);
            }

            bool Mentions(GameEvents g) => g != null && g.events.Any(e =>
            {
                if (e == null)
                {
                    return false;
                }

                string content = e.Value;
                if (content == null)
                {
                    content = "";
                }

                if (content.IndexOf(
                    "dive", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                List<Object> prefabs = e.prefabList;
                if (prefabs == null)
                {
                    prefabs = new List<Object>();
                }

                return prefabs.Any(o => o != null && o.name.Contains("oxygen"));
            });

            foreach (var et in Object.FindObjectsOfType<EventTriggers>(true))
            {
                if (et == null || et.eventTriggers == null)
                {
                    continue;
                }

                bool any = Wanted(et)
                    || et.eventTriggers.Any(
                        t => t != null
                            && (Mentions(t.gameEvents) || Mentions(
                                t.gameEventsElse)
                                || t.eventRequirements.Any(
                                    q => q != null && q.itemType != null
                                        && q.itemType.name.Contains(
                                            "oxygen"))));
                if (!any)
                {
                    continue;
                }

                sb.Append(
                    $"\n{et.transform.root.name}/{et.name} at "
                        + $"{Fmt(et.transform.position)} "
                        + $"active={et.gameObject.activeInHierarchy}");
                foreach (var t in et.eventTriggers)
                {
                    if (t == null)
                    {
                        continue;
                    }

                    n++;
                    string text = "";
                    if (t.disabled)
                    {
                        text = " disabled";
                    }

                    sb.Append(
                        $"\n  > {t.type} '{t.reactsToItemType}' "
                            + $"fired={t.fired} multi={t.multipleFire}{text} "
                            + $"removeItem={t.removeItemThatTriggeredMe}");
                    foreach (var q in t.eventRequirements)
                    {
                        if (q != null)
                        {
                            sb.Append($"\n    req {Req(q)}");
                        }
                    }

                    foreach (var (label, ge) in new[]
                    {
                        ("do", t.gameEvents),
                        ("else", t.gameEventsElse),
                        ("exit", t.gameEventsExit)
                    })
                    {
                        if (ge != null)
                        {
                            sb.Append(
                                $"\n    {label}: {ge.name} "
                                    + $"multi={ge.multipleFire}");
                            foreach (var e in ge.events)
                            {
                                if (e != null)
                                {
                                    sb.Append($"\n      {Ev(e)}");
                                }
                            }
                        }
                    }
                }
            }

            foreach (var g in Object.FindObjectsOfType<GameEvents>(true))
            {
                if (g == null || g.GetComponent<EventTriggers>() != null
                    || !(Wanted(g) || Mentions(g)))
                {
                    continue;
                }

                sb.Append(
                    $"\n[events] {g.transform.root.name}/{g.name} at "
                        + $"{Fmt(g.transform.position)} "
                        + $"multi={g.multipleFire}");
                foreach (var e in g.events)
                {
                    if (e != null)
                    {
                        sb.Append($"\n      {Ev(e)}");
                    }
                }
            }

            foreach (var type in new[]
            {
                "oxygenTank_full",
                "oxygenTank_empty"
            })
            {
                var db = Singleton<ItemsDatabase>.Instance;
                GameObject prefab = null;
                if (db != null && db.itemsDict.TryGetValue(type, out var path))
                {
                    prefab = Resources.Load(path) as GameObject;
                }

                InvItem inv = null;
                if (prefab != null)
                {
                    inv = prefab.GetComponent<InvItem>();
                }

                var jd = Singleton<JournalDatabase>.Instance;
                bool hasPrefab = prefab != null;
                bool? important = null;
                if (inv != null)
                {
                    important = inv.isImportantItem;
                }

                bool journalItem = jd != null && jd.itemsDict.ContainsKey(type);
                string head =
                    $"\n[item] {type}: prefab={hasPrefab} "
                        + $"important={important} "
                        + $"journalItem={journalItem}";
                UI ui = Singleton<UI>.Instance;
                bool? inJournal = null;
                if (ui != null)
                {
                    Journal journal = ui.journal;
                    if (journal != null)
                    {
                        Dictionary<string, Journal.Item> items =
                            journal.itemsDict;
                        if (items != null)
                        {
                            inJournal = items.ContainsKey(type);
                        }
                    }
                }

                sb.Append(
                    head
                        + $" inJournal={inJournal} "
                        + $"carried={MapScenarioWorld.CarriedOf(type)}");
            }

            // Every loaded step that acts on the player
            // (ScriptedEvents.TargetsPlayer), by type and function, and how
            // many of them also name other targets.
            var onPlayer = new Dictionary<string, int>();
            int mixed = 0;
            foreach (var g in Object.FindObjectsOfType<GameEvents>(true))
            {
                foreach (var e in g.events)
                {
                    if (e != null && ScriptedEvents.TargetsPlayer(e))
                    {
                        string argument = "";
                        if (e.type == GameEvent.Type.runFunction)
                        {
                            argument = " " + e.Value;
                        }

                        GameEvent.Type kind = e.type;
                        string step = "";
                        if (ScriptedEvents.IsPlayerStep(e.type))
                        {
                            step = " (player step)";
                        }

                        string k = kind + argument + step;
                        int seen = 0;
                        if (onPlayer.TryGetValue(k, out var c))
                        {
                            seen = c;
                        }

                        onPlayer[k] = seen + 1;
                        List<GameObject> targetGameObjects =
                            e.targetGameObjects;
                        int count = 0;
                        if (targetGameObjects != null)
                        {
                            count = targetGameObjects.Count;
                        }

                        List<string> targetUniqueObjects =
                            e.targetUniqueObjects;
                        int uniqueCount = 0;
                        if (targetUniqueObjects != null)
                        {
                            uniqueCount = targetUniqueObjects.Count;
                        }

                        if (count + uniqueCount > 1)
                        {
                            mixed++;
                        }
                    }
                }
            }

            string counts = string.Join(
                "; ",
                onPlayer.OrderByDescending(x => x.Value)
                    .Select(x => $"{x.Key} x{x.Value}"));
            sb.Append(
                "\n[on player] "
                    + $"{counts}; with other targets too: {mixed}");
            UniqueObjects instance = Singleton<UniqueObjects>.Instance;
            GameObject uo = null;
            if (instance != null)
            {
                uo = instance.getObject("player");
            }

            bool isPlayer =
                uo != null
                && Player.Instance != null
                && uo == Player.Instance.gameObject;
            sb.Append(
                $"\n[unique] player -> {Name(uo)} is Player.Instance: "
                    + $"{isPlayer}");
            Plugin.Log.LogInfo($"[dive] scan {pat}:{sb}");
            r.Info = $"{n} triggers (in the log)";
            yield break;
        }
    }
}
