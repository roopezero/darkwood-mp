using System.Collections.Generic;
using System.Linq;
using DarkwoodMP.Combat.Enemies;
using DarkwoodMP.Net;
using DarkwoodMP.Sync;
using DarkwoodMP.World;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The client side: copies that die and settle as corpses. See
    /// NetEnemies.cs.
    /// </summary>
    internal sealed partial class NetEnemies
    {
        /// <summary>
        /// Client: what Character.die2 does to how a dying character looks, for
        /// a copy (which never runs die2): its particles (a burning one's
        /// flames) stop, its shadow fades out, and so does a shadow armour's
        /// effect.
        /// </summary>
        private static void DeathLooks(Character c)
        {
            try
            {
                foreach (var ps in c.GetComponentsInChildren<ParticleSystem>())
                {
                    if (ps != null)
                    {
                        ps.Stop();
                    }
                }

                if (c.shadow != null)
                {
                    Core.tweenColor(
                        c.shadow.gameObject, new Color(1f, 1f, 1f, 0f), 1f);
                }

                // its shadow armour's effect fades, as die2 does
                var armor = c.GetComponent<ShadowArmor>();
                if (armor != null)
                {
                    armor.die();
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't play {c.name}'s death here: {e.Message}");
            }
        }

        /// <summary>
        /// Client: a separate location's characters were switched off (left) or
        /// on again (entered) here. The game does that to the corpses of the
        /// location's own characters (Location.activateCharacters: SetActive
        /// false, then true), so on the host a corpse's play-on-awake particles
        /// (a ChomperRed's) start again when its location is entered again. A
        /// copy we made from its prefab isn't one of the location's characters,
        /// so it would stay as it was: the dead ones of those standing in the
        /// location are switched off and on with it, as the host's are. Living
        /// copies stay as they are: the host runs them while a joiner is in
        /// their location.
        /// </summary>
        internal void LocationCharacters(Location loc, bool active)
        {
            if (loc == null)
            {
                return;
            }

            foreach (var p in _corpses.Values.Concat(_puppets.Values).ToList())
            {
                if (p == null || !p.Ours || p.C == null || p.C.alive
                    || p.C.gameObject.activeSelf == active)
                {
                    continue;
                }

                if (!Core.withinLocation(p.C.transform.position, 0f, loc))
                {
                    continue;
                }

                p.C.gameObject.SetActive(active);
                string again = "";
                if (active)
                {
                    again = string.Join(
                        ", ",
                        p.C.GetComponentsInChildren<ParticleSystem>()
                            .Where(ps => ps.isEmitting).Select(ps => ps.name));
                }

                string text = "off";
                if (active)
                {
                    text = "on";
                }

                string note = "";
                if (again != "")
                {
                    note = "; playing again: " + again;
                }

                Plugin.Log.LogInfo(
                    $"[enemy] the host's corpse #{p.Id} ({p.C.name}) switched "
                        + $"{text} with "
                        + $"{Core.getTrueLocationName(loc.name)}{note}");
            }
        }

        /// <summary>
        /// How long the host's death animation is shown before the corpse is
        /// settled here.
        /// </summary>
        private const float DeathAnimTime = 2f;

        private static readonly System.Reflection.MethodInfo[] CorpseSteps =
        {
            HarmonyLib.AccessTools.Method(
                typeof(Character), "destroyComponents"),
            HarmonyLib.AccessTools.Method(
                typeof(Character), "destroyComponents2"),
            HarmonyLib.AccessTools.Method(
                typeof(Character), "setDeathCollider"),
        };

        /// <summary>
        /// Client: one of the host's enemies died, and its death has played. In
        /// the game it's the end of the death animation that turns the body
        /// into a lootable corpse (Character.animationCompleted:
        /// destroyComponents, destroyComponents2, setDeathCollider); a copy's
        /// animation callbacks are off, so that's done here, once. After it the
        /// copy's frames are left on the last one rather than replayed from the
        /// host's updates.
        /// </summary>
        private void Settle(Puppet p)
        {
            p.Settled = true;
            var c = p.C;
            if (c == null)
            {
                return;
            }

            foreach (var a in p.Animators.Values)
            {
                if (a == null || a.CurrentClip == null
                    || a.CurrentClip.frames == null
                    || a.CurrentClip.frames.Length == 0)
                {
                    continue;
                }

                a.SetFrame(
                    a.CurrentClip.frames.Length - 1, triggerEvent: false);
                a.Pause();
            }

            // destroyComponents destroys CharacterSounds without stopping its
            // loop
            StopSounds(c, keepDeathSound: true);
            // whatever a hide switched off (it's shown already; this only makes
            // sure)
            SetVisible(c, true);
            DieBody(c);
            if (p.Cut)
            {
                SettleCut(p);
                return;
            }

            if (c.animator == null)
            {
                // No sprite animation (the worms): the end of a death animation
                // never comes in the host's game either, so its corpse is never
                // made lootable there; only what dying itself does (die2) to
                // the collider.
                if (c.collider != null)
                {
                    if (!c.dontSwitchColliderTriggerOnDeath)
                    {
                        c.collider.isTrigger = true;
                    }

                    c.collider.enabled = true;
                }

                Plugin.Log.LogInfo(
                    $"The host's {c.name} #{p.Id} died: it has no death "
                        + "animation, so its body isn't searchable (as on the "
                        + "host)");
                return;
            }

            foreach (var step in CorpseSteps)
            {
                if (step == null)
                {
                    continue;
                }

                try
                {
                    step.Invoke(c, null);
                }
                catch (System.Exception e)
                {
                    if (step.Name == "setDeathCollider")
                    {
                        System.Exception why = e.InnerException;
                        if (why == null)
                        {
                            why = e;
                        }

                        DeathColliderRest(c, why);
                    }
                    else
                    {
                        System.Exception innerException = e.InnerException;
                        string message = null;
                        if (innerException != null)
                        {
                            message = innerException.Message;
                        }

                        if (message == null)
                        {
                            message = e.Message;
                        }

                        Plugin.Log.LogWarning(
                            $"Couldn't settle {c.name}'s corpse ({step.Name}): "
                                + $"{message}");
                    }
                }
            }

            string data;
            if (c.GetComponent<Item>() != null)
            {
                data =
                    $"The host's {c.name} #{p.Id} died: its corpse can be "
                        + "searched here";
            }
            else
            {
                data =
                    $"The host's {c.name} #{p.Id} died, but its corpse "
                        + "couldn't be made searchable here";
            }

            Plugin.Log.LogInfo(data);
            LogFovBlockers(c, $"#{p.Id} settled");
        }

        /// <summary>
        /// Client: what Character.die2 does to a dying character's body, for a
        /// copy (which never runs die2): its body collider is on and walked
        /// through (unless the kind keeps it solid), its hit collider goes
        /// (unless it has a pre-death, which keeps it alive a while longer),
        /// its Selectable when it isn't selectable dead, and its
        /// InSightOfPlayer. Logs the parts on the view's shadow layers that are
        /// off here, which Settle used to switch on.
        /// </summary>
        private static void DieBody(Character c)
        {
            try
            {
                int mask = FovShadowMask();
                var off = c.GetComponentsInChildren<Collider>(true)
                    .Where(
                        x => x != null && !x.enabled
                            && (mask & (1 << x.gameObject.layer)) != 0)
                    .Select(x => $"{x.name}:{x.gameObject.layer}").ToList();
                var hit = c.hitCollider;
                string text;
                if (hit == null)
                {
                    text = "none";
                }
                else
                {
                    string cuts = "";
                    if ((mask & (1 << hit.layer)) != 0)
                    {
                        cuts = " (cuts the view)";
                    }

                    text = $"{hit.name} layer {hit.layer}{cuts}";
                }

                Plugin.Log.LogInfo(
                    $"[enemy] {c.name} dies here: hit collider {text}, " +
                                   $"pre-death {c.hasPreDeath}; switched-off "
                        + $"parts on the view's shadow layers (mask {mask}): "
                        + $"[{string.Join(", ", off)}]");
                if (c.collider != null)
                {
                    if (!c.dontSwitchColliderTriggerOnDeath)
                    {
                        c.collider.isTrigger = true;
                    }

                    c.collider.enabled = true;
                }

                if (!c.hasPreDeath)
                {
                    if (hit != null)
                    {
                        Object.Destroy(hit);
                    }

                    if (c.notSelectableWhenDead)
                    {
                        var sel = c.GetComponent<Selectable>();
                        if (sel != null)
                        {
                            Object.Destroy(sel);
                        }
                    }
                }

                if (c.inSightOfPlayer != null)
                {
                    Object.Destroy(c.inSightOfPlayer);
                    c.inSightOfPlayer = null;
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't settle {c.name}'s body as die2 does: "
                        + $"{e.Message}");
            }
        }

        /// <summary>
        /// Client: how a copy cut in half on the host looks as it starts: what
        /// Character.cutMeInHalf does to the upper part (its particles stop; it
        /// doesn't fade its shadow as a death does). The lower half is a new
        /// enemy on the host, shown here as any other.
        /// </summary>
        private static void CutLooks(Character c)
        {
            try
            {
                foreach (var ps in c.GetComponentsInChildren<ParticleSystem>())
                {
                    if (ps != null)
                    {
                        ps.Stop();
                    }
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't play {c.name}'s cut here: {e.Message}");
            }
        }

        // destroyComponents and destroyComponents2, without setDeathCollider.
        private static readonly System.Reflection.MethodInfo[] CutSteps =
            { CorpseSteps[0], CorpseSteps[1] };

        /// <summary>
        /// Client: the upper part of a copy cut in half on the host, once its
        /// cut has played: as on the host (cutMeInHalf, then its Cut_half
        /// animation's end), no corpse: no Item to search, its inventory went
        /// with the lower half (the host's new enemy), its selectable and
        /// capsule are gone, and it lies flat.
        /// </summary>
        private static void SettleCut(Puppet p)
        {
            var c = p.C;
            foreach (var step in CutSteps)
            {
                if (step == null)
                {
                    continue;
                }

                try
                {
                    step.Invoke(c, null);
                }
                catch (System.Exception e)
                {
                    System.Exception innerException = e.InnerException;
                    string message = null;
                    if (innerException != null)
                    {
                        message = innerException.Message;
                    }

                    if (message == null)
                    {
                        message = e.Message;
                    }

                    Plugin.Log.LogWarning(
                        $"Couldn't settle {c.name}'s cut ({step.Name}): "
                            + $"{message}");
                }
            }

            try
            {
                c.cuttingInHalf = true;
                c.cutInHalf = true;
                var item = c.GetComponent<Item>();
                if (item != null)
                {
                    Object.Destroy(item);
                }

                var inv = c.GetComponent<Inventory>();
                if (inv != null)
                {
                    World.Inventories.Containers.CloseIfOpen(c.gameObject);
                    Object.Destroy(inv);
                }

                var sel = c.GetComponent<Selectable>();
                if (sel != null)
                {
                    Object.Destroy(sel);
                }

                if (c.capsuleCollider != null)
                {
                    Object.Destroy(c.capsuleCollider);
                }

                if (c.collider != null)
                {
                    c.collider.isTrigger = true;
                }

                var t = c.transform;
                t.localPosition =
                    new Vector3(t.localPosition.x, -1f, t.localPosition.z);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't settle {c.name}'s cut: {e.Message}");
            }

            Plugin.Log.LogInfo(
                $"The host's {c.name} #{p.Id} was cut in half: its lower half "
                    + "is the host's, nothing to search here");
        }

        private static readonly System.Reflection.MethodInfo YPosAfterDeath =
            HarmonyLib.AccessTools.Method(
                typeof(Character), "setYPosAfterDeath");
    }
}
