using DarkwoodMP.Combat.Enemies;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The client side's answers to the game's calls that FreezePatches
    /// hands on: a frozen copy never acts. See NetEnemies.cs.
    /// </summary>
    internal sealed partial class NetEnemies
    {
        /// <summary>
        /// Character.enableComponents(enable) runs: not for a frozen one of
        /// ours as its area is switched on again. A copy of the host's enemy
        /// plays the host's clips on its sprite animator, which
        /// enableComponents(false) (Location.activateCharacters as its area
        /// was left) switched off: that one is switched back on.
        /// </summary>
        public bool EnablesComponents(Character c, bool enable)
        {
            bool client = enable && ClientMode;
            if (!FreezeRules.StaysFrozen(
                enable,
                client,
                client && _frozen.Contains(c)))
            {
                return true;
            }

            if (IsPuppet(c, out _))
            {
                var a = c.animator;
                if (a != null)
                {
                    a.enabled = true;
                }
            }

            return false;
        }

        /// <summary>
        /// CharacterSounds.playIdleLoop(loop) runs: a hidden one of ours
        /// starts no loop; stopping a loop ("") still runs.
        /// </summary>
        public bool PlaysIdleLoop(CharacterSounds sounds, string loop)
        {
            if (string.IsNullOrEmpty(loop) || _hidden.Count == 0)
            {
                return true;
            }

            var c = sounds.GetComponent<Character>();
            return !FreezeRules.StaysSilent(
                loop,
                ClientMode,
                c != null && _hidden.Contains(c));
        }

        /// <summary>
        /// CharacterSpawnPoint.actuallySpawn runs: not in the host's world,
        /// or the spawn point is in an area only we are in.
        /// </summary>
        public bool SpawnsHere(CharacterSpawnPoint point)
        {
            bool client = ClientMode;
            if (FreezeRules.SpawnsOwn(
                client, client && LocalOnly(point.gameObject)))
            {
                return true;
            }

            Plugin.Log.LogInfo(
                $"[enemy] {point.type} at "
                    + $"{point.transform.position}: not spawned here, the "
                    + "host's comes from the host");
            return false;
        }

        /// <summary>
        /// Character.heardSound runs: anything but a frozen one in the
        /// host's world.
        /// </summary>
        public bool Hears(Character c)
        {
            bool client = ClientMode;
            return FreezeRules.Hears(client, client && _frozen.Contains(c));
        }

        /// <summary>
        /// Location.activateCharacters(active) has run: the copies of the
        /// host's dead enemies made from their prefab go off and on with it.
        /// </summary>
        public void LocationSwitched(Location loc, bool active)
        {
            if (ClientMode)
            {
                LocationCharacters(loc, active);
            }
        }

    }
}
