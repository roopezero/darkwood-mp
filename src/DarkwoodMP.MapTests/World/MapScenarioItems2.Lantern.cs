using System.Collections;
using System.Linq;
using DarkwoodMP.Combat;
using UnityEngine;
using static DarkwoodMP.World.MapScenario;

namespace DarkwoodMP.World
{
    /// <summary>A lantern worn on the hotbar.</summary>
    public static partial class MapScenarioItems2
    {
        // ---- the lantern ----------------------------------------------------

        private static (InvSlot slot, InvItemClass was) _lanternWas;

        /// <summary>
        /// "i2-lantern on|off": a lantern put on our hotbar (lit, as the game
        /// does for one there) or taken off.
        /// </summary>
        private static IEnumerator Lantern(string[] a, Result r)
        {
            var p = P;
            bool on = a.Length == 0 || a[0] == "on";
            var ctl = Singleton<Controller>.Instance;
            var slot = p.Hotbar.slots.FirstOrDefault(
                s => !InvItemClass.isNull(s.invItem)
                    && s.invItem.type == "lantern");
            if (on && slot == null)
            {
                // A free slot, else the last one not in hand (its item put back
                // when the lantern comes off).
                InvSlot free = p.Hotbar.slots.LastOrDefault(
                    s => InvItemClass.isNull(s.invItem) && !s.selected);
                if (free == null)
                {
                    free = p.Hotbar.slots.LastOrDefault(s => !s.selected);
                }

                slot = free;
                if (slot == null)
                {
                    r.Fail("no hotbar slot");
                    yield break;
                }

                slot.inventory = p.Hotbar;
                if (!InvItemClass.isNull(slot.invItem))
                {
                    _lanternWas = (slot, new InvItemClass(slot.invItem));
                    slot.removeItem();
                }

                AsPlayer(() =>
                {
                    slot.createItem("lantern", 1);
                    slot.invItem.checkForActiveSwitches();
                });
            }
            else if (!on && slot != null)
            {
                AsPlayer(() =>
                {
                    slot.invItem.deactivateSwitches("lantern");
                    slot.removeItem();
                });

                if (_lanternWas.slot == slot && _lanternWas.was != null)
                {
                    slot.createItem(_lanternWas.was);
                }

                _lanternWas = (null, null);
            }

            yield return new WaitForSeconds(0.5f);
            bool lit =
                ctl.logicLights.Contains(p.lightDot) && p.lightDot.lightsPlayer;
            string text = "off";
            if (on)
            {
                text = "on";
            }

            string dot = "not lit";
            if (lit)
            {
                dot = "lit";
            }

            r.Info =
                $"lantern {text}: our light dot {dot}, radius "
                    + $"{p.lightDot.LightRadius:0}";
            if (lit != on)
            {
                r.Fail(r.Info);
            }
        }

        /// <summary>
        /// "i2-lit yes|no" (host): the other player's puppet shows a lantern
        /// light, and their stand-in stands in light (Core.isInLight).
        /// </summary>
        private static IEnumerator Lit(string[] a, Result r)
        {
            bool want = a.Length == 0 || a[0] == "yes";
            var ps = Sync.PlayerSync.Instance;
            Sync.RemotePlayer rp = null;
            NetPlayerProxy proxy = null;
            bool shown = false, inLight = false;
            float until = Time.realtimeSinceStartup + 6f;
            while (true)
            {
                if (ps != null)
                {
                    rp = ps.Remotes.Values.FirstOrDefault(
                        x => x.Owner != Sync.PlayerSync.DummyId);
                }
                else
                {
                    rp = null;
                }

                if (rp != null)
                {
                    NetPlayerProxy.All.TryGetValue(rp.Owner, out proxy);
                }

                shown = rp != null && rp.LanternShown > 0f;
                Vector3 pos;
                if (proxy != null)
                {
                    pos = proxy.transform.position;
                }
                else
                {
                    Vector3? position = null;
                    if (rp != null)
                    {
                        Sync.PlayerSnapshot last = rp.Last;
                        if (last != null)
                        {
                            position = last.Position;
                        }
                    }

                    if (position.HasValue)
                    {
                        pos = position.Value;
                    }
                    else
                    {
                        pos = Vector3.zero;
                    }
                }

                inLight =
                    rp != null && Core.isInLight(pos, mustBeWalkable: false);
                if ((shown == want && inLight == want)
                    || Time.realtimeSinceStartup > until)
                {
                    break;
                }

                yield return new WaitForSeconds(0.3f);
            }

            string name = null;
            if (rp != null)
            {
                name = rp.Name;
            }

            if (name == null)
            {
                name = "nobody";
            }

            float lanternShown = 0f;
            if (rp != null)
            {
                lanternShown = rp.LanternShown;
            }

            string copy = "puppet";
            if (proxy != null)
            {
                copy = "stand-in";
            }

            r.Info =
                $"{name}: lantern light on their puppet {shown} (radius "
                    + $"{lanternShown:0}), their {copy} in light {inLight}";
            if (shown != want || inLight != want)
            {
                r.Fail(r.Info);
            }
        }
    }
}
