using DarkwoodMP.World.Inventories;
using DarkwoodMP.World.Rolls;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.World.Scripted;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The world test's scripted events, and the client's side
    /// (WorldTest).
    /// </summary>
    public static partial class WorldTest
    {
        /// <summary>
        /// Scripted events, building and dialogue: what's near (for the
        /// authority test), then a trigger walked into, a world event and a
        /// build, each as our own player does it.
        /// </summary>
        private static IEnumerator Scripted(Vector3 at)
        {
            var triggers = AreaTriggers(at);
            foreach (var (et, i) in triggers.Take(3))
            {
                Plugin.Log.LogInfo(
                    $"[world test] trigger {et.eventTriggers[i].type} "
                        + $"{et.name} "
                        + $"{ObjRef.Of(et.gameObject)} "
                        + $"#{i} at {et.transform.position}: "
                        + $"{Steps(et.eventTriggers[i].gameEvents)}");
            }

            var cons = Object.FindObjectsOfType<Constructible>()
                .Where(
                    c => c.options.Count > 0 && !c.constructed
                        && !c.onlyWhenDestroyed
                        && ObjRef.Of(c.gameObject).Valid)
                .OrderBy(c => (c.transform.position - at).sqrMagnitude)
                .ToList();
            foreach (var c in cons.Take(3))
            {
                Plugin.Log.LogInfo(
                    $"[world test] buildable {c.name} "
                        + $"{ObjRef.Of(c.gameObject)} "
                        + $"at {c.transform.position}: "
                        + $"{c.options.Count} options");
            }

            foreach (var n in Object.FindObjectsOfType<NPC>().Where(
                n => n.characterDialogue != null
                    && ObjRef.Of(n.gameObject).Valid)
                .OrderBy(n => (n.transform.position
                        - at).sqrMagnitude).Take(3))
            {
                Plugin.Log.LogInfo(
                    $"[world test] NPC with dialogue {n.name} "
                        + $"{ObjRef.Of(n.gameObject)} "
                        + $"at {n.transform.position}");
            }

            if (triggers.Count > 0)
            {
                var (et, i) = triggers[0];
                var t = et.eventTriggers[i];
                Plugin.Log.LogInfo(
                    $"[world test] setting off {et.name} #{i} ({t.type})");
                AsPlayer(() => et.fireEventTrigger(et.eventTriggers[i].type));
                yield return new WaitForSeconds(t.delay + t.warmupTime + 1.5f);
                string text = "";
                if (et == null)
                {
                    text = ", and its object is gone (the event removed it)";
                }

                Plugin.Log.LogInfo($"[world test] #{i} fired={t.fired}{text}");
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no trigger found");
            }

            var events = Singleton<Events>.Instance;
            var we = events.worldEvents.Where(
                kv => kv.Value != null && !kv.Value.fired
                    && kv.Value.events.Count > 0
                    && kv.Value.events.All(
                        e => e != null && !ScriptedEvents.IsPlayerStep(e.type)
                            && e.type != GameEvent.Type.spawnCharacter))
                .OrderBy(kv => kv.Value.events.Count).Select(kv => kv.Key)
                .FirstOrDefault();
            if (we != null)
            {
                Plugin.Log.LogInfo(
                    $"[world test] world event {we}: "
                        + $"{Steps(events.worldEvents[we])}");
                AsPlayer(() => events.fireWorldEvent(we));
                yield return new WaitForSeconds(1.5f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no world event to fire");
            }

            if (cons.Count > 0)
            {
                var c = cons[0];
                Plugin.Log.LogInfo(
                    $"[world test] building {c.name} {ObjRef.Of(c.gameObject)} "
                        + "option 0");
                AsPlayer(() => c.construct(false, 0));
                yield return new WaitForSeconds(1.5f);
            }
            else
            {
                Plugin.Log.LogInfo("[world test] nothing to build");
            }

            // A trigger the world sets off by itself (at a time of day, or
            // something dying), not our player: the host runs it and sends it
            // as kind 3. One whose events have no steps of a player's own and
            // spawn nothing.
            var turnedOn = global::EventTrigger.Type.refreshWhenTurnedOn;
            var worldTriggers = Object.FindObjectsOfType<EventTriggers>(true)
                .SelectMany(et => et.eventTriggers.Select((t, i) => (et, t, i)))
                .Where(
                    x => x.t != null
                        && (x.t.type == global::EventTrigger.Type.onTime
                            || x.t.type == global::EventTrigger.Type.onDeath
                            || x.t.type == turnedOn))
                .ToList();
            Plugin.Log.LogInfo(
                "[world test] world triggers (time, death, turned on): "
                    + $"{worldTriggers.Count}, not fired "
                    + $"{worldTriggers.Count(x => !x.t.fired)}");
            foreach (var x in worldTriggers.OrderBy(
                x => (x.et.transform.position - at).sqrMagnitude).Take(4))
            {
                global::EventTrigger.Type kind = x.t.type;
                string triggerName = x.et.name;
                ObjRef triggerRef = ObjRef.Of(x.et.gameObject);
                bool active = x.et.gameObject.activeInHierarchy;
                bool disabled = x.t.disabled;
                string script = "none";
                if (x.t.gameEvents != null)
                {
                    string stepTypes = Steps(x.t.gameEvents);
                    string fired = "";
                    if (x.t.gameEvents.fired)
                    {
                        fired = " (fired)";
                    }

                    script = stepTypes + fired;
                }

                Plugin.Log.LogInfo(
                    $"[world test] world trigger {kind} {triggerName} "
                        + $"{triggerRef} #{x.i} active={active} "
                        + $"disabled={disabled} events: {script}");
            }

            var wt = worldTriggers
                .Where(x => Unfired(x.t)
                    && x.t.gameEvents.events.All(
                        e => e != null
                            && (!ScriptedEvents.IsPlayerStep(e.type)
                                || e.type == GameEvent.Type.displayMessage)
                            && e.type != GameEvent.Type.spawnCharacter)
                    && ObjRef.Of(x.et.gameObject).K == ObjRef.Kind.Saved)
                .OrderBy(x => (x.et.transform.position - at).sqrMagnitude)
                .FirstOrDefault();
            if (wt.et != null)
            {
                Plugin.Log.LogInfo(
                    $"[world test] the world sets off {wt.t.type} {wt.et.name} "
                        + $"{ObjRef.Of(wt.et.gameObject)} "
                        + $"#{wt.i}: {Steps(wt.t.gameEvents)}");
                // Started as fireEventTrigger would, also for one whose object
                // is culled (inactive) here.
                try
                {
                    Singleton<Controller>.Instance.StartCoroutine(
                        wt.t.fire(wt.t.type, "", wt.et));
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning($"[world test] {e.Message}");
                }

                yield return new WaitForSeconds(wt.t.delay + 1.5f);
                Plugin.Log.LogInfo(
                    $"[world test] world trigger #{wt.i} fired={wt.t.fired}");
            }
            else
            {
                Plugin.Log.LogInfo("[world test] no world trigger to set off");
            }
        }

        /// <summary>
        /// "--dwmp-test-client-actions" (a client): our player uses up a rope
        /// and opens (or closes) the nearest door, as one action; the scripted
        /// host refuses it. The door should go back as the host has it, and the
        /// rope come back.
        /// </summary>
        public static IEnumerator ClientActions()
        {
            while (!WorldShare.WorldReady || !Combat.NetEnemies.ClientMode)
            {
                yield return null;
            }

            yield return new WaitForSeconds(3f);
            var p = Player.Instance;
            var door = Nearest(
                Object.FindObjectsOfType<Door>().Where(
                    d => !d.destroyed && ObjRef.Of(d.gameObject).Valid),
                p.transform.position);
            float best = 0f;
            if (door != null)
            {
                best = Vector3.Distance(
                    door.transform.position, p.transform.position);
            }

            if (door == null || best >= 2500f)
            {
                Plugin.Log.LogInfo("[client test] no door near");
                yield break;
            }

            p.Inventory.addItemTypeToPlayer("rope", 2);
            yield return null;
            yield return null;
            int Ropes()
            {
                int n = 0;
                foreach (var it in p.Inventory.getAllItems())
                {
                    if (it.type == "rope")
                    {
                        n += it.amount;
                    }
                }

                foreach (var sl in p.Hotbar.slots)
                {
                    if (!InvItemClass.isNull(sl.invItem)
                        && sl.invItem.type == "rope")
                    {
                        n += sl.invItem.amount;
                    }
                }

                return n;
            }

            bool wasOpen = door.opened;
            int ropesBefore = Ropes();
            int refusedBefore = Coop.Current.Authority.RolledBack;
            Plugin.Log.LogInfo(
                $"[client test] {door.name} "
                    + $"{ObjRef.Of(door.gameObject)} "
                    + $"{best:0} away, open={wasOpen}, ropes={ropesBefore}");
            AsPlayer(() =>
            {
                p.Inventory.removeItemAmountFromPlayer("rope", 1);
                if (wasOpen)
                {
                    door.close(null);
                }
                else
                {
                    door.open(p.transform.position, p.transform, 0f);
                }
            });

            Plugin.Log.LogInfo(
                $"[client test] did it: open={door.opened}, ropes={Ropes()}");
            yield return new WaitForSeconds(4f);
            Plugin.Log.LogInfo(
                $"[client test] after the host's answer: open={door.opened} "
                    + $"(was {wasOpen}), ropes={Ropes()}, rolled back "
                    + $"{Coop.Current.Authority.RolledBack}");
            // The scripted host refuses the door: it is shut or open as before,
            // and the rope it used is back.
            bool back = door.opened == wasOpen && Ropes() == ropesBefore
                && Coop.Current.Authority.RolledBack > refusedBefore;
            string verdict;
            if (back)
            {
                verdict = "PASS";
            }
            else
            {
                verdict =
                    $"FAIL (open={door.opened}, was {wasOpen}; ropes "
                        + $"{Ropes()}, were {ropesBefore}; refusals "
                        + (Coop.Current.Authority.RolledBack - refusedBefore)
                        + ")";
            }

            Plugin.Log.LogInfo($"[client test] rollback: {verdict}");

            // A trigger our player walks into: our part and the shared part run
            // here, and the host is asked.
            var triggers = AreaTriggers(p.transform.position);
            if (triggers.Count > 0)
            {
                var (et, i) = triggers[0];
                Plugin.Log.LogInfo(
                    $"[client test] setting off {et.eventTriggers[i].type} "
                        + $"{et.name} "
                        + $"{ObjRef.Of(et.gameObject)} "
                        + $"#{i}: {Steps(et.eventTriggers[i].gameEvents)}");
                AsPlayer(() => et.fireEventTrigger(et.eventTriggers[i].type));
                yield return new WaitForSeconds(
                    et.eventTriggers[i].delay + et.eventTriggers[i].warmupTime
                        + 2f);
                string text = "";
                if (et == null)
                {
                    text = ", and its object is gone (the event removed it)";
                }

                Plugin.Log.LogInfo(
                    $"[client test] #{i} "
                        + $"fired={et.eventTriggers[i].fired}{text}");
            }
            else
            {
                Plugin.Log.LogInfo("[client test] no trigger near");
            }

            // Talking to an NPC someone else talks to: the scripted host
            // refuses it, and the dialogue closes again.
            var npc = Nearest(
                Object.FindObjectsOfType<NPC>().Where(
                    n => n.characterDialogue != null && n.wantsToTalk
                        && ObjRef.Of(n.gameObject).Valid),
                p.transform.position);
            if (npc != null)
            {
                Plugin.Log.LogInfo(
                    $"[client test] talking to {npc.name} "
                        + $"{ObjRef.Of(npc.gameObject)}");
                npc.talkTo();
                var win = Singleton<UI>.Instance.dialogueWindow;
                string who = "nobody";
                if (win.npc != null)
                {
                    who = win.npc.name;
                }

                Plugin.Log.LogInfo($"[client test] dialogue open with {who}");
                yield return new WaitForSecondsRealtime(8f);
                string dialogue = "closed";
                if (win.npc != null)
                {
                    dialogue = "still open with " + win.npc.name;
                }

                Plugin.Log.LogInfo(
                    "[client test] after the host's answer: dialogue "
                        + $"{dialogue}");
            }
            else
            {
                Plugin.Log.LogInfo("[client test] no NPC to talk to");
            }
        }
    }
}
