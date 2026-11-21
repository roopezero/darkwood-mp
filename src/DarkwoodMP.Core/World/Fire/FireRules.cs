namespace DarkwoodMP.World.Fire
{
    /// Fires on doors, windows, items and enemies (the plugin's Fire).
    /// The host's world burns and says so; a client's player's flame asks
    /// the host first.
    public static class FireRules
    {
        /// How far from a thing a client's player may set it alight,
        /// generously (a flamethrower's flame flies 700 a second for 1 to
        /// 1.5 s).
        public const float FlameReach = 1500f;

        /// The shortest burn the host gives a client's fire.
        public const float ShortestBurn = 1f;

        /// The longest burn the host gives a client's fire.
        public const float LongestBurn = 20f;

        /// The burn time a client sends when the thing has no Burn of its
        /// own.
        public const float UnknownBurn = 20f;

        /// What a GameObject is, for the Burning message. Players and
        /// stand-ins are never sent. The first match wins, so a Character
        /// that is also an Item is an enemy.
        public static FireKind KindOf(in KindFacts thing)
        {
            if (thing.Player || thing.Proxy)
            {
                return FireKind.None;
            }

            if (thing.Character)
            {
                return FireKind.Enemy;
            }

            if (thing.Door)
            {
                return FireKind.Door;
            }

            if (thing.Window)
            {
                return FireKind.Window;
            }

            if (thing.Item)
            {
                return FireKind.Item;
            }

            return FireKind.None;
        }

        /// What a flame touching a collider sets alight: never a character,
        /// then a destructible item (the collider's, or else the flame
        /// transform's), a door, a window. An item that isn't destructible
        /// on the collider hides the transform's.
        public static FlameTarget Flammable(in FlameFacts touch)
        {
            if (!touch.Collider)
            {
                return FlameTarget.Nothing;
            }

            if (touch.CharOnCollider || touch.CharOnTransform)
            {
                return FlameTarget.Nothing;
            }

            if (touch.ColliderItem)
            {
                if (touch.ColliderItemDestructible)
                {
                    return FlameTarget.ColliderItem;
                }
            }
            else if (touch.TransformItem && touch.TransformItemDestructible)
            {
                return FlameTarget.TransformItem;
            }

            if (touch.Door)
            {
                return FlameTarget.Door;
            }

            if (touch.Window)
            {
                return FlameTarget.Window;
            }

            return FlameTarget.Nothing;
        }

        /// Host: whether a fire that starts or goes out is sent to the
        /// others. Only while players are joined, so a fire burning when
        /// one joins isn't sent to them.
        public static bool SendsChange(in ChangeFacts fire)
        {
            if (!fire.Exists || fire.Replaying || !fire.Host)
            {
                return false;
            }

            if (fire.Peers == 0 || !fire.InWorld)
            {
                return false;
            }

            return fire.Kind != FireKind.None && fire.Named;
        }

        /// Client: whether to ask the host to set alight what its player's
        /// flame lit. Doors, windows and items only: an enemy's fire is the
        /// host's.
        public static bool MayAskToIgnite(in AskFacts thing)
        {
            if (!thing.Exists || !thing.ClientMode || thing.Replaying)
            {
                return false;
            }

            if (thing.OursAlone)
            {
                return false;
            }

            if (thing.Kind == FireKind.None || thing.Kind == FireKind.Enemy)
            {
                return false;
            }

            return thing.Named;
        }

        /// Client: the burn time it asks for: its own Burn's, or
        /// UnknownBurn.
        public static float AskedTime(bool burning, float burnTime)
        {
            if (burning)
            {
                return burnTime;
            }

            return UnknownBurn;
        }

        /// Client, when the host answers: whether to put its own fire out.
        /// Only when refused, and the thing is still there and burning.
        public static bool PutsOut(bool allowed, bool exists, bool burning)
        {
            return !allowed && exists && burning;
        }

        /// Host: a client's Burning, taken only inside a Request (a bare
        /// one is ignored). No state is sent back on a refusal: the
        /// client's callback puts its own fire out.
        public static Ignition MayIgnite(in IgniteFacts fire)
        {
            if (!fire.Reach.Handling)
            {
                return default;
            }

            if (!fire.On)
            {
                return Refuse(Verdict.Refuse(Refusal.OnlyLighting));
            }

            if (fire.Kind < FireKind.Door || fire.Kind > FireKind.Item)
            {
                return Refuse(Verdict.Refuse(Refusal.NothingToBurn));
            }

            if (!fire.Found)
            {
                return Refuse(Verdict.Refuse(Refusal.NothingToBurn));
            }

            var reach = fire.Reach with
            {
                Reach = FlameReach,
                What = fire.Name,
            };

            var verdict = ReachRules.InReach(reach);
            if (verdict.Refused)
            {
                return Refuse(verdict);
            }

            if (fire.Kind == FireKind.Item)
            {
                if (!fire.Destructible || fire.Destroyed)
                {
                    var cant = Verdict.Refuse(Refusal.CantBurn, fire.Name);
                    return Refuse(cant);
                }
            }

            if (fire.Kind == FireKind.Door && fire.Destroyed)
            {
                return Refuse(Verdict.Refuse(Refusal.Broken, fire.Name));
            }

            if (fire.Burning)
            {
                return default;
            }

            return new Ignition { Lights = true, Time = Clamp(fire.Time) };
        }

        /// Client: what to do with the host's Burning. Any kind but None
        /// and the unknown ones, enemies too; the host's burn time is
        /// taken as it is.
        public static FireChange FromHost(in HostFireFacts fire)
        {
            if (fire.Kind == FireKind.None || fire.Kind > FireKind.Item)
            {
                return FireChange.Nothing;
            }

            if (!fire.Found)
            {
                return FireChange.Nothing;
            }

            if (fire.On && !fire.Burning)
            {
                return FireChange.Light;
            }

            if (!fire.On && fire.Burning)
            {
                return FireChange.PutOut;
            }

            return FireChange.Nothing;
        }

        /// Whether a Burn's damage runs (Burn.waitToGetHit): everywhere but
        /// on a client, where only its own player gets hurt. The host's
        /// fire hurts the host's enemies.
        public static bool BurnHurts(bool clientMode, bool onPlayer)
        {
            return !clientMode || onPlayer;
        }

        static Ignition Refuse(Verdict verdict)
        {
            return new Ignition { Verdict = verdict };
        }

        /// As Mathf.Clamp: NaN stays NaN.
        static float Clamp(float time)
        {
            if (time < ShortestBurn)
            {
                return ShortestBurn;
            }

            if (time > LongestBurn)
            {
                return LongestBurn;
            }

            return time;
        }
    }
}
