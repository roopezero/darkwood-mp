using System;
using System.Collections.Generic;
using DarkwoodMP.Net;
using DarkwoodMP.World.Building;
using HarmonyLib;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Building and repairing (Constructible.construct): a repaired stove or
    /// generator, a built option (a barricade, a trap spot), an enabled feeder.
    /// The builder's game builds it with the ingredients (manual); the others
    /// build their copy with construct(manual: false, option), which takes no
    /// ingredients and plays no sound.
    /// A joiner's build is a request: the joiner's game takes the ingredients
    /// at once and builds nothing until the host says yes (the game has no way
    /// to un-build: a built option's events show and hide objects, and the
    /// Constructible removes itself). The host checks reach and that it can
    /// still be built, builds its copy and passes it on; then the joiner builds
    /// its copy (construct(manual: false, option) and the build sound).
    /// Refused, the ingredients come back (InventoryWatch) and there's
    /// nothing to take down. The option's GameEvents runs by ScriptedEvents'
    /// parts: the builder's own steps only for the builder.
    /// </summary>
    internal sealed class Construction
    {
        private readonly CoopSession _coop;

        /// <summary>
        /// Joiner: builds waiting for the host's answer (one at a time per
        /// thing).
        /// </summary>
        private readonly HashSet<Constructible> _waiting =
            new HashSet<Constructible>();

        /// <summary>
        /// Joiner: true while building our copy after the host said yes (not
        /// sent again).
        /// </summary>
        private bool _confirming;

        public Construction(CoopSession coop)
        {
            _coop = coop;
            coop.Handle(MsgType.Construct, OnConstruct);
        }

        private bool IsWaiting(Constructible c) => _waiting.Contains(c);

        private PacketWriter Write(ObjRef r, int option)
        {
            var w = _coop.Net.Begin(MsgType.Construct);
            var order = new BuildOrder
            {
                Site = ObjRef.NameOf(r),
                Option = option,
            };

            Building.Construct.Write(w, order);
            return w;
        }

        /// <summary>
        /// Sends our build (nothing if it can't be named): the host's to
        /// everyone, a joiner's as a request.
        /// </summary>
        internal void Send(Constructible c, int option)
        {
            var r = ObjRef.Of(c.gameObject);
            if (!r.Valid)
            {
                return;
            }

            var w = Write(r, option);
            string what = $"build {c.name} {r} option {option}";
            if (WorldActions.Debug)
            {
                Plugin.Log.LogInfo($"[world] sent {what}");
            }

            if (_coop.Net.IsHost)
            {
                _coop.Net.Broadcast(w, Channel.Control, reliable: true);
                return;
            }

            string sound = null;
            if (ConstructionRules.HasOption(option, c.options.Count))
            {
                sound = c.options[option].buildCompleteSound;
            }

            _waiting.Add(c);
            _coop.Authority.Request(w, what, (ok, reason, _) =>
            {
                _waiting.Remove(c);
                if (!ok)
                {
                    Plugin.Log.LogInfo(
                        $"[world] {what} not built here: the host refused it "
                            + $"({reason})");
                    return;
                }

                if (c == null)
                {
                    return;
                }

                _confirming = true;
                try
                {
                    c.chosenOption = option;
                    ScriptedEvents.RunAs(
                        ScriptedEvents.Part.PlayerAndShared, replaying: false,
                        () => c.construct(manual: false, option));
                    if (!string.IsNullOrEmpty(sound))
                    {
                        AudioController.Play(sound);
                    }

                    if (WorldActions.Debug)
                    {
                        Plugin.Log.LogInfo(
                            $"[world] {what}: the host agreed, built it here");
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning(
                        $"Couldn't build {what}: {e.Message}");
                }
                finally
                {
                    _confirming = false;
                }
            });
        }

        private void OnConstruct(ulong from, PacketReader r)
        {
            var s = _coop.Net;
            if (s == null || !WorldShare.InWorld)
            {
                return;
            }

            if (s.IsClient && from != s.HostId)
            {
                return;
            }

            BuildOrder order = Building.Construct.Read(r);
            var obj = ObjRef.RefOf(order.Site);
            int option = order.Option;
            var c = ObjRef.Resolve<Constructible>(obj);
            if (s.IsHost && !Allowed(c, option))
            {
                return;
            }

            if (WorldActions.Debug)
            {
                string name = "not found here (built already?)";
                if (c != null)
                {
                    name = c.name;
                }

                Plugin.Log.LogInfo(
                    $"[world] got build {obj} option {option}: " + name);
            }

            int options = 0;
            if (c != null)
            {
                options = c.options.Count;
            }

            if (ConstructionRules.Builds(c != null, option, options))
            {
                try
                {
                    c.chosenOption = option;
                    ScriptedEvents.Part part;
                    if (s.IsHost)
                    {
                        part = ScriptedEvents.Part.HostAndShared;
                    }
                    else
                    {
                        part = ScriptedEvents.Part.SharedOnly;
                    }

                    ScriptedEvents.RunAs(part, replaying: true,
                        () => c.construct(manual: false, option));
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Couldn't build {obj}: {e.Message}");
                }
            }

            if (s.IsHost)
            {
                s.Broadcast(
                    Write(obj, option), Channel.Control, reliable: true,
                    except: from);
            }
        }

        /// <summary>
        /// Host: a joiner's build is one this world can still take, near them.
        /// </summary>
        private bool Allowed(Constructible c, int option)
        {
            // The reach is Authority's to check: ConstructionRules.MayBuild
            // decides the rest, with no request it would check reach for.
            var verdict = ConstructionRules.MayBuild(BuildOf(c, option));
            if (verdict.Refused)
            {
                _coop.Authority.Refuse(verdict);
                return false;
            }

            return _coop.Authority.InReach(
                c.transform.position, ConstructionRules.Reach, c.name);
        }

        /// <summary>
        /// What the host knows of the construction a joiner's Construct names:
        /// the only code that reads it for ConstructionRules.MayBuild. c may
        /// be null (not found).
        /// </summary>
        private BuildFacts BuildOf(Constructible c, int option)
        {
            if (c == null)
            {
                return new BuildFacts { Option = option };
            }

            var item = c.GetComponent<Item>();
            return new BuildFacts
            {
                Found = true,
                Option = option,
                Options = c.options.Count,
                OnlyWhenDestroyed = c.onlyWhenDestroyed,
                Constructed = c.constructed,
                Broken = item != null && item.destroyed,
                Name = c.name,
            };
        }

        /// <summary>
        /// What Constructible.construct does as it starts: the only code that
        /// reads the game for ConstructionRules.Start.
        /// </summary>
        internal BuildStart StartOf(Constructible c, bool manual)
        {
            var facts = new StartFacts
            {
                Confirming = _confirming,
                Sharing = Coop.Guards.Sharing,
                Manual = manual,
                TestAsPlayer = WorldActions.TestAsPlayer,
            };

            if (_coop.Net.IsClient)
            {
                facts = facts with { Client = true, Waiting = IsWaiting(c) };
            }

            return ConstructionRules.Start(facts);
        }
    }

    /// <summary>
    /// Our player built or repaired something (manual: with the ingredients):
    /// the others build it too.
    /// </summary>
    [HarmonyPatch(
        typeof(Constructible), nameof(Constructible.construct),
        new[] { typeof(bool), typeof(int) })]
    static class ConstructPatch
    {
        static bool Prefix(
            Constructible __instance, bool manual, int forceOption,
            out bool __state)
        {
            __state = false;
            CoopSession coop = Coop.Current;
            if (coop == null)
            {
                return true;
            }

            switch (coop.Construction.StartOf(__instance, manual))
            {
                case BuildStart.Game:
                    return true;
                case BuildStart.Swallow:
                    return false;
                case BuildStart.Host:
                    __state = true;
                    Coop.Guards.Depth++;
                    return true;
            }

            // A joiner asks first: the ingredients go now (to come back if
            // refused), the build once the host agrees.
            if (ConstructionRules.ReadsMenu(forceOption, manual))
            {
                // sets chosenOption from the construction menu
                __instance.getSelectedOption();
            }

            int option = ConstructionRules.OptionOf(
                forceOption, __instance.chosenOption);
            // Not one we can name to the host, or no such option: built
            // here alone, as the game would.
            int options = __instance.options.Count;
            bool named = false;
            if (ConstructionRules.HasOption(option, options))
            {
                named = ObjRef.Of(__instance.gameObject).Valid;
            }

            if (!ConstructionRules.Asks(option, options, named))
            {
                return true;
            }

            if (manual)
            {
                var menu = Singleton<ConstructionMenu>.Instance;
                ConstructionIcon icon = null;
                if (menu != null)
                {
                    icon = menu.selectedIcon;
                }

                if (icon != null)
                {
                    foreach (var req in icon.requirements)
                    {
                        req.removeIngredients();
                    }
                }
            }

            coop.Construction.Send(__instance, option);
            return false;
        }

        static void Postfix(
            Constructible __instance, int forceOption, bool __state)
        {
            CoopSession coop = Coop.Current;
            if (!__state || !Coop.Guards.Outermost() || coop == null)
            {
                return;
            }

            int option = ConstructionRules.OptionOf(
                forceOption, __instance.chosenOption);
            coop.Construction.Send(__instance, option);
        }
    }
}
