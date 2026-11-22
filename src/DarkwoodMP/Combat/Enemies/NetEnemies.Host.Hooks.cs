using DarkwoodMP.Combat.Enemies;
using UnityEngine;

namespace DarkwoodMP.Combat
{
    /// <summary>
    /// The host side's answers to the game's calls that FreezePatches,
    /// EnemySoundPatches and ThreatPatches hand on. See NetEnemies.cs.
    /// </summary>
    internal sealed partial class NetEnemies
    {
        /// <summary>
        /// Host, with someone to tell: a sound played on something
        /// (AudioController._PlayAsSound) goes to the clients that see it.
        /// </summary>
        public void SoundPlayed(Transform parent, string audioId, float volume)
        {
            if (parent == null || !_session.IsHost
                || _session.Peers.Count == 0)
            {
                return;
            }

            HostSound(parent, audioId, volume);
        }

        /// <summary>
        /// Whether a character's idle loop is playing, before
        /// CharacterSounds.playIdleLoop runs.
        /// </summary>
        public static bool LoopPlaying(CharacterSounds sounds)
        {
            return sounds.idleAudioObject != null;
        }

        /// <summary>
        /// Host: an enemy's loop that stopped without another taking its
        /// place (playIdleLoop with no loop) played nothing, so SoundPlayed
        /// didn't see it: the clients are sent an empty EnemySound.
        /// </summary>
        public void LoopChanged(
            CharacterSounds sounds,
            string loop,
            bool before)
        {
            if (!EnemyLoopRules.Stopped(before, loop, LoopPlaying(sounds)))
            {
                return;
            }

            if (!_session.IsHost || _session.Peers.Count == 0)
            {
                return;
            }

            HostSound(sounds.transform, "", 0f);
        }

        /// <summary>
        /// Host: a normal hit that took health splats blood (not the night
        /// trader's); the clients are shown it too. hit.Damage is what the
        /// hit took.
        /// </summary>
        public void HitTaken(Character c, in GameHit hit)
        {
            if (!hit.NormalHit || hit.Damage <= 0f || c.isNightTrader
                || !_session.IsHost)
            {
                return;
            }

            float y;
            if (hit.Attacker != null)
            {
                y = (hit.Attacker.rotation * Quaternion.Euler(
                    90f, 180f, 0f)).eulerAngles.y;
            }
            else
            {
                y = c.transform.eulerAngles.y;
            }

            HostBlood(c, y);
        }

        /// <summary>
        /// Host: a corpse lying outside its own location isn't switched off
        /// as that location is left (see LocationActivateCharactersHost): it
        /// is let go of the location and culled by the world's grid.
        /// </summary>
        public void LocationLeaving(Location loc, bool active)
        {
            if (active || !_session.IsHost || !_session.Active
                || loc.isOutsideLocation
                || Plugin.Instance.Host.Culling.HostAway)
            {
                return;
            }

            var list = loc.charactersList;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var c = list[i];
                if (c == null || c.alive || c.dummy || c.npc != null
                    || Core.withinLocation(c.transform.position, 0f, loc))
                {
                    continue;
                }

                KeepCorpse(loc, c, i);
            }
        }

        private static void KeepCorpse(Location loc, Character c, int index)
        {
            try
            {
                loc.charactersList.RemoveAt(index);
                loc.objects.Remove(c.gameObject);
                c.disconnectFromLocation();
                CullableObject co = c.GetComponent<CullableObject>();
                if (co == null)
                {
                    co = c.gameObject.AddComponent<CullableObject>();
                }

                co.registerMe();
                Plugin.Log.LogInfo(
                    $"[enemy] {c.name}'s corpse at {c.transform.position} "
                        + "lies outside "
                        + $"{Core.getTrueLocationName(loc.name)}: "
                        + "kept as that location is left");
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning(
                    $"Couldn't keep {c.name}'s corpse out of "
                        + $"{loc.name}: {e.Message}");
            }
        }
    }
}
