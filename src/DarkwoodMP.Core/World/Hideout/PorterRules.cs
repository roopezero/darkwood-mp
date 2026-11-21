using System;
using System.Collections.Generic;

namespace DarkwoodMP.World.Hideout
{
    /// The porter (the plugin's Hideouts): a transport of a hideout's
    /// containers to the one a player stands in, and the porter whistle.
    /// While playing together the host decides both, for everyone.
    public static class PorterRules
    {
        /// The flag set while the porter carries a package.
        public const string InTransit = "porter_inTransit";

        /// The flag set once the porter was killed.
        public const string Killed = "porter_killed";

        /// The item type the porter doesn't carry as it is...
        public const string Alcohol = "alcohol";

        /// ...but as this one (its class assigned again).
        public const string Bottle = "bottle";

        /// How near a porter place a call's position must be (3D, strict).
        public const float PlaceReach = 50f;

        /// Who does a porter transport. While together the game's own
        /// never runs.
        public static TransportStep Transport(in TransportFacts move)
        {
            if (!move.Together)
            {
                return TransportStep.RunGame;
            }

            if (move.IsClient)
            {
                if (!move.OthersPart && !move.Replaying)
                {
                    return TransportStep.Request;
                }

                return TransportStep.Nothing;
            }

            if (move.JoinersPart)
            {
                return TransportStep.Nothing;
            }

            return TransportStep.Deliver;
        }

        /// Whether a Location is the hideout of this id (the plugin's
        /// HideoutById): an id above 0, not a sub-location.
        public static bool IsHideout(int wanted, int id, bool sub)
        {
            return wanted > 0 && id == wanted && !sub;
        }

        /// Why a transport from the source to the target can't be done,
        /// in the order the host checks. No day, flag or reach check.
        public static Verdict Why(in HideoutFacts move)
        {
            if (!move.HasSource || move.SourceId <= 0)
            {
                return Verdict.Refuse(Refusal.NoSuchHideout);
            }

            if (!move.HasTarget || !move.TargetBase || move.TargetId <= 0)
            {
                return Verdict.Refuse(Refusal.NotAtHideout);
            }

            if (move.Same)
            {
                return Verdict.Refuse(Refusal.AlreadyAtHideout);
            }

            if (!move.HasPackagePlace)
            {
                return Verdict.Refuse(Refusal.NoPackagePlace, move.TargetName);
            }

            return Verdict.Allow;
        }

        /// The host, checking a joiner's PorterTransport: where they are
        /// first, then Why.
        public static Verdict MayTransport(bool known, in HideoutFacts move)
        {
            if (!known)
            {
                return Verdict.Refuse(Refusal.PositionUnknown);
            }

            return Why(move);
        }

        /// Who calls the porter.
        public static CallStep Call(in CallFacts call)
        {
            if (!call.Together || call.Replaying || !call.HasPorterPlace)
            {
                return CallStep.RunGame;
            }

            if (call.IsHost)
            {
                return CallStep.RunGame;
            }

            return CallStep.Request;
        }

        /// Whether spawnPorter will make the porter's spawner, by three of
        /// the game's own conditions (not the day, not the transit flag).
        public static bool SpawnerMade(bool place, bool there, bool killed)
        {
            return place && !there && !killed;
        }

        /// Whether the host sends PorterCalled after spawnPorter ran.
        public static bool CalledHere(in CalledFacts called)
        {
            if (!called.SpawnerMade || !called.RanGame)
            {
                return false;
            }

            return called.Together && called.IsHost && called.HasPorterPlace;
        }

        /// The index of the porter place nearest the position, closer than
        /// PlaceReach (3D); -1 for none. On equal distance the first wins.
        public static int NearestPorterPlace(
            IReadOnlyList<Vec3> places,
            Vec3 pos)
        {
            int best = -1;
            float near = PlaceReach;
            for (int i = 0; i < places.Count; i++)
            {
                float d = Distance(places[i], pos);
                if (d < near)
                {
                    best = i;
                    near = d;
                }
            }

            return best;
        }

        /// The host, checking a joiner's porter call, in its order: the
        /// place first, then where they are.
        public static Verdict MayCall(in CallCheckFacts call)
        {
            if (!call.HasPlace)
            {
                return Verdict.Refuse(Refusal.NoPorterPlace);
            }

            if (!call.Known)
            {
                return Verdict.Refuse(Refusal.PositionUnknown);
            }

            if (!call.AtPlace)
            {
                return Verdict.Refuse(Refusal.NotAtPlace, call.PlaceName);
            }

            if (!call.IsDay)
            {
                return Verdict.Refuse(Refusal.NotDaytime);
            }

            if (call.InTransit)
            {
                return Verdict.Refuse(Refusal.PorterOnTheWay);
            }

            if (call.Killed)
            {
                return Verdict.Refuse(Refusal.PorterDead);
            }

            if (call.PorterThere)
            {
                return Verdict.Refuse(Refusal.PorterThere);
            }

            return Verdict.Allow;
        }

        /// As Unity's Vector3.Distance.
        static float Distance(Vec3 a, Vec3 b)
        {
            float x = a.X - b.X;
            float y = a.Y - b.Y;
            float z = a.Z - b.Z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }
    }
}
