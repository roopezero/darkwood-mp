using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.Net;
using DarkwoodMP.World.Workbenches;
using Newtonsoft.Json;
using UnityEngine;

namespace DarkwoodMP.World.Inventories
{
    /// <summary>
    /// SlotShare: the workbench's level, shared (WorkbenchLevel).
    /// </summary>
    internal sealed partial class SlotShare
    {
        // ---- the workbench's level ------------------------------------------

        internal void LevelRaised(int level)
        {
            if (!Coop.Guards.Sharing)
            {
                return;
            }

            var w = _session.Begin(MsgType.WorkbenchLevel);
            WorkbenchLevelMessage.Write(w, level);
            if (_session.IsHost)
            {
                _session.Broadcast(w, Channel.Control, reliable: true);
            }
            else
            {
                _session.SendToHost(w, Channel.Control, reliable: true);
            }

            Plugin.Log.LogInfo($"The workbench is now level {level}");
        }

        private void OnLevel(ulong from, PacketReader r)
        {
            var s = _session;
            if (!WorldShare.InWorld || (s.IsClient && from != s.HostId))
            {
                return;
            }

            int level = WorkbenchLevelMessage.Read(r);
            var ctl = Singleton<Controller>.Instance;
            if (ctl == null)
            {
                return;
            }

            if (!WorkbenchLevelRules.Raises(level, ctl.workbenchLevel))
            {
                return;
            }

            if (s.IsHost)
            {
                // An upgrade (CraftingRecipes.doCraft) raises the level by one,
                // at an open workbench.
                var raise = new LevelFacts
                {
                    Level = level,
                    Current = ctl.workbenchLevel,
                    AtWorkbench = AtWorkbench(from),
                };

                var verdict = WorkbenchLevelRules.MayRaise(raise);
                if (verdict.Refused)
                {
                    Plugin.Log.LogInfo(
                        $"[authority] {s.NameOf(from)} raised the workbench "
                            + $"level: refused ({Refusals.Text(verdict)})");
                    return;
                }

                var w = s.Begin(MsgType.WorkbenchLevel);
                WorkbenchLevelMessage.Write(w, level);
                s.Broadcast(w, Channel.Control, reliable: true, except: from);
            }

            ctl.workbenchLevel = level;
            Plugin.Log.LogInfo(
                $"The workbench is now level {level} ({s.NameOf(from)} "
                    + "upgraded it)");
            // its new row, before any change to it arrives
            Plugin.Instance.Host.Workbenches.ApplyAll("upgraded");
            // An open workbench shows the new level's recipes.
            Inventory open = null;
            if (Player.Instance != null)
            {
                open = Player.Instance.openedItemInventory;
            }

            if (open != null && open.isWorkbench && open.workbench != null)
            {
                open.workbench.workbenchInventory.clear();
                open.workbench.setRecipes();
                // its "Level N" label and the next upgrade
                open.workbench.refreshWorkbenchUpgrade();
            }
        }

        private static bool AtWorkbench(ulong player)
        {
            foreach (var wb in Object.FindObjectsOfType<Workbench>(true))
            {
                if (Coop.Current.Authority.Near(
                    player, wb.transform.position, WorldActions.Reach))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The workbench's level before a craft (DoCraft), 0 with no world.
        /// </summary>
        internal static int LevelNow()
        {
            var ctl = Singleton<Controller>.Instance;
            if (ctl == null)
            {
                return 0;
            }

            return ctl.workbenchLevel;
        }

        /// <summary>
        /// A craft is done (DoCraft): if it was an upgrade, the storage gets
        /// its new row (also without co-op), and the new level is sent.
        /// </summary>
        internal static void Crafted(int before)
        {
            var ctl = Singleton<Controller>.Instance;
            if (ctl == null || ctl.workbenchLevel <= before)
            {
                return;
            }

            Plugin.Instance.Host.Workbenches.ApplyAll("upgraded");
            if (Coop.Guards.Replaying)
            {
                return;
            }

            CoopSession coop = Coop.Current;
            if (coop != null)
            {
                coop.Slots.LevelRaised(ctl.workbenchLevel);
            }
        }
    }
}
