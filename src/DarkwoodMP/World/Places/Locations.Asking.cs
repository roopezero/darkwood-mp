using System.Collections;
using DarkwoodMP.Net;
using UnityEngine;

namespace DarkwoodMP.World.Places
{
    /// Locations on a client: asking the host where a location stands, and
    /// waiting for its answer before the game spawns it.
    internal sealed partial class Locations
    {
        // ---- client: asking -------------------------------------------------

        /// <summary>
        /// Client: ask the host where a location we haven't loaded stands
        /// (once).
        /// </summary>
        private void Ask(string name)
        {
            var s = S;
            if (s == null || !s.IsClient || !_spots.Ask(name))
            {
                return;
            }

            var d = Singleton<Dreams>.Instance;
            var w = s.Begin(MsgType.LocationAsk);
            bool dream = d != null
                && LocationSlotRules.AsksForDream(d.dreamPrepared, d.dreaming);
            LocationAskMessage.Write(
                w, new LocationAsk { Dream = dream, Name = name });
            s.SendToHost(w, Channel.Control, reliable: true);
            Plugin.Log.LogInfo(
                $"[locations] asked the host where {name} stands");
        }

        private void OnSlot(ulong from, PacketReader r)
        {
            var s = S;
            if (s == null || !s.IsClient || from != s.HostId)
            {
                return;
            }

            LocationSlot slot = LocationSlotMessage.Read(r);
            string name = slot.Name;
            Vec3 pos = slot.Spot.Pos;
            _spots.Answered(name, slot.Spot);
            Plugin.Log.LogInfo(
                $"[locations] the host's {name} stands at {pos.X:0},{pos.Z:0}, "
                    + $"turned {slot.Spot.Turn}");
        }

        /// <summary>
        /// Whether spawnLocation(name) should wait for the host's answer first.
        /// </summary>
        private bool MustWait(string name)
        {
            var s = S;
            var wait = new WaitFacts
            {
                IsClient = s != null && s.IsClient,
                Connected = s != null && s.Connected,
                InWorld = WorldShare.InWorld,
            };

            return _spots.MustWait(wait, name);
        }

        private IEnumerator WaitThenSpawn(
            OutsideLocations o, string name, bool transport)
        {
            Ask(name);
            Seconds until = LocationSlotRules.WaitUntil(
                new Seconds(Time.realtimeSinceStartup));
            while (LocationSlotRules.Waits(
                MustWait(name), new Seconds(Time.realtimeSinceStartup), until))
            {
                yield return null;
            }

            if (MustWait(name))
            {
                _spots.GiveUp(name);
                Plugin.Log.LogWarning(
                    $"[locations] the host didn't say where {name} stands: "
                        + "loading it where our game puts it, so it may not "
                        + "match the host's");
            }

            yield return o.StartCoroutine(o.spawnLocation(name, transport));
        }
    }
}
