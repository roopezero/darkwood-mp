using System.Collections.Generic;
using DarkwoodMP.Combat;
using DarkwoodMP.World.Fuel;
using UnityEngine;

namespace DarkwoodMP.World
{
    /// <summary>
    /// Host: a generator burns fuel by the electricity skill of the player
    /// nearest it (Generator.drainFuel uses our own player's
    /// Player.electricityModifier for every generator). A joiner's skill is
    /// read from the character it last sent (Characters.Electrician); while it
    /// isn't known yet, ours is used, as before. The host's fuel is the one
    /// every game keeps (FuelSync).
    ///
    /// Client: its generators burn nothing (the modifier is 0 for the call);
    /// the level comes from the host. The rest of drainFuel still runs, so the
    /// lights dim on low fuel as the host's level says.
    /// </summary>
    internal sealed class GeneratorDrain
    {
        private readonly CoopSession _coop;

        public GeneratorDrain(CoopSession coop)
        {
            _coop = coop;
        }

        /// <summary>
        /// For the tests: the last drain's generator, whose skill it used, and
        /// the modifier.
        /// </summary>
        public string Last { get; private set; }

        /// <summary>
        /// Before Generator.drainFuel: set the modifier for the call, and
        /// return the one to put back after it (null: it wasn't changed).
        /// </summary>
        public float? Before(Generator generator)
        {
            var s = _coop.Net;
            var p = Player.Instance;
            var start = DrainRules.Start(new DrainFacts
            {
                Session = s != null,
                Client = s != null && s.IsClient,
                Host = s != null && s.IsHost,
                Player = p != null,
                Sharing = Coop.Guards.Sharing,
                Proxies = Proxies.Any,
                Characters = Characters.Instance != null,
            });

            if (start == DrainStart.Zero)
            {
                float was = p.electricityModifier;
                p.electricityModifier = 0f;
                return was;
            }

            if (start == DrainStart.Leave)
            {
                return null;
            }

            var standIns = new List<NetPlayerProxy>(NetPlayerProxy.All.Values);
            var players = new List<DrainPlayer>(standIns.Count);
            foreach (var x in standIns)
            {
                players.Add(PlayerOf(x));
            }

            int i = DrainRules.Nearest(new NearestFacts
            {
                Generator = Flat(generator.transform.position),
                Host = Flat(p.transform.position),
                HostAway = SharedCullingPatch.HostAway,
                Players = players,
            });

            NetPlayerProxy nearest = null;
            if (i >= 0)
            {
                nearest = standIns[i];
            }

            bool? has = null;
            string ownerName = "ours";
            if (nearest != null)
            {
                has = Characters.Instance.Electrician(nearest.Owner);
                ownerName = nearest.OwnerName;
            }

            float modifier = p.electricityModifier;
            if (has.HasValue)
            {
                modifier = DrainRules.Modifier(has.Value);
            }

            Last = $"{generator.name}: {ownerName}'s skill, {modifier}";
            if (has == null)
            {
                return null;
            }

            float before = p.electricityModifier;
            p.electricityModifier = modifier;
            return before;
        }

        /// <summary>
        /// After Generator.drainFuel: put the modifier Before changed back.
        /// </summary>
        public static void After(float? modifier)
        {
            if (modifier.HasValue && Player.Instance != null)
            {
                Player.Instance.electricityModifier = modifier.Value;
            }
        }

        /// <summary>
        /// A stand-in, for DrainRules.Nearest: the only code reading it
        /// there.
        /// </summary>
        private static DrainPlayer PlayerOf(NetPlayerProxy x)
        {
            if (x == null)
            {
                return default;
            }

            return new DrainPlayer
            {
                Exists = true,
                Away = x.Away,
                InLocation = x.InLocation != null,
                Dummy = x.Owner == Sync.PlayerSync.DummyId,
                Pos = Flat(x.transform.position),
            };
        }

        private static Vec2 Flat(Vector3 v) => new Vec2(v.x, v.z);
    }
}
