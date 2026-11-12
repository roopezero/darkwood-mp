using System;
using System.Collections.Generic;
using DarkwoodMP.Net;
using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Building and repairing (Constructible.construct): a repaired stove or generator, a built option (a
    /// barricade, a trap spot), an enabled feeder. The builder's game builds it with the ingredients (manual); the
    /// others build their copy with construct(manual: false, option), which takes no ingredients and plays no sound.
    /// A joiner's build is a request: the joiner's game takes the ingredients at once and builds nothing until the
    /// host says yes (the game has no way to un-build: a built option's events show and hide objects, and the
    /// Constructible removes itself). The host checks reach and that it can still be built, builds its copy and passes
    /// it on; then the joiner builds its copy (construct(manual: false, option) and the build sound). Refused, the
    /// ingredients come back (InventoryWatch) and there's nothing to take down. The option's GameEvents runs by
    /// ScriptedEvents' parts: the builder's own steps only for the builder.
    /// </summary>
    public static class Construction
    {
        private static Session S => Session.Instance;

        /// <summary>Joiner: builds waiting for the host's answer (one at a time per thing).</summary>
        private static readonly HashSet<Constructible> Waiting = new HashSet<Constructible>();

        /// <summary>Joiner: true while building our copy after the host said yes (not sent again).</summary>
        internal static bool Confirming;

        public static void Register(Session s) => s.Register(MsgType.Construct, OnConstruct);

        internal static bool IsWaiting(Constructible c) => Waiting.Contains(c);

        /// <returns>False if it couldn't be named (nothing was sent).</returns>
        internal static bool Send(Constructible c, int option)
        {
            var r = ObjRef.Of(c.gameObject);
            if (!r.Valid) return false;
            var w = S.Begin(MsgType.Construct);
            r.Write(w);
            w.W.Write(option);
            string what = $"build {c.name} {r} option {option}";
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] sent {what}");
            if (S.IsHost) { S.Broadcast(w, Channel.Control, reliable: true); return true; }
            string sound = option >= 0 && option < c.options.Count ? c.options[option].buildCompleteSound : null;
            Waiting.Add(c);
            Authority.Request(w, what, (ok, reason, _) =>
            {
                Waiting.Remove(c);
                if (!ok) { Plugin.Log.LogInfo($"[world] {what} not built here: the host refused it ({reason})"); return; }
                if (c == null) return;
                Confirming = true;
                try
                {
                    c.chosenOption = option;
                    ScriptedEvents.RunAs(ScriptedEvents.Part.PlayerAndShared, replaying: false, () => c.construct(manual: false, option));
                    if (!string.IsNullOrEmpty(sound)) AudioController.Play(sound);
                    if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] {what}: the host agreed, built it here");
                }
                catch (Exception e) { Plugin.Log.LogWarning($"Couldn't build {what}: {e.Message}"); }
                finally { Confirming = false; }
            });
            return true;
        }

        private static void OnConstruct(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !WorldShare.InWorld) return;
            if (s.IsClient && from != s.HostId) return;
            var obj = ObjRef.Read(r);
            int option = r.R.ReadInt32();
            var c = ObjRef.Resolve<Constructible>(obj);
            if (s.IsHost && !Allowed(c, option)) return;
            if (WorldActions.Debug) Plugin.Log.LogInfo($"[world] got build {obj} option {option}: " + (c != null ? c.name : "not found here (built already?)"));
            if (c != null && option >= 0 && option < c.options.Count)
            {
                try
                {
                    c.chosenOption = option;
                    ScriptedEvents.RunAs(s.IsHost ? ScriptedEvents.Part.HostAndShared : ScriptedEvents.Part.SharedOnly, replaying: true,
                        () => c.construct(manual: false, option));
                }
                catch (Exception e) { Plugin.Log.LogWarning($"Couldn't build {obj}: {e.Message}"); }
            }
            if (s.IsHost)
            {
                var w = s.Begin(MsgType.Construct);
                obj.Write(w);
                w.W.Write(option);
                s.Broadcast(w, Channel.Control, reliable: true, except: from);
            }
        }

        /// <summary>Host: a joiner's build is one this world can still take, near them.</summary>
        private static bool Allowed(Constructible c, int option)
        {
            if (c == null) { Authority.Refuse("there's nothing to build there (built already?)"); return false; }
            if (option < 0 || option >= c.options.Count) { Authority.Refuse($"it has no option {option}"); return false; }
            var item = c.GetComponent<Item>();
            bool broken = item != null && item.destroyed;
            if (c.onlyWhenDestroyed && !broken) { Authority.Refuse("it isn't broken"); return false; }
            if (c.constructed && !broken) { Authority.Refuse("it's built already"); return false; }
            return Authority.InReach(c.transform.position, WorldActions.Reach, c.name);
        }
    }

    /// <summary>Our player built or repaired something (manual: with the ingredients): the others build it too.</summary>
    [HarmonyPatch(typeof(Constructible), nameof(Constructible.construct), new[] { typeof(bool), typeof(int) })]
    static class ConstructPatch
    {
        static bool Prefix(Constructible __instance, bool manual, int forceOption, out (bool, ScriptedEvents.Part) __state)
        {
            __state = (false, ScriptedEvents.Current);
            if (Construction.Confirming || !WorldActions.Sharing || !(manual || WorldActions.TestAsPlayer)) return true;
            if (Session.Instance.IsClient)
            {
                // A joiner asks first: the ingredients go now (to come back if refused), the build once the host agrees.
                if (Construction.IsWaiting(__instance)) return false;
                int option = forceOption;
                if (option == -1)
                {
                    if (manual) __instance.getSelectedOption(); // sets chosenOption from the construction menu
                    option = __instance.chosenOption;
                }
                // Not one we can name to the host, or no such option: built here alone, as the game would.
                if (option < 0 || option >= __instance.options.Count || !ObjRef.Of(__instance.gameObject).Valid) return true;
                if (manual)
                {
                    var menu = Singleton<ConstructionMenu>.Instance;
                    var icon = menu != null ? menu.selectedIcon : null;
                    if (icon != null) foreach (var req in icon.requirements) req.removeIngredients();
                }
                Construction.Send(__instance, option);
                return false;
            }
            __state.Item1 = true;
            WorldActions.Depth++;
            return true;
        }

        static void Postfix(Constructible __instance, int forceOption, (bool, ScriptedEvents.Part) __state)
        {
            if (!__state.Item1 || !WorldActions.Outermost()) return;
            int option = forceOption != -1 ? forceOption : __instance.chosenOption;
            Construction.Send(__instance, option);
        }

        static Exception Finalizer(Exception __exception, (bool, ScriptedEvents.Part) __state)
        {
            if (__state.Item1) ScriptedEvents.Current = __state.Item2;
            return __exception;
        }
    }
}
