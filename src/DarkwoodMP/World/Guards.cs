using DarkwoodMP.Combat;
using DarkwoodMP.Net;

namespace DarkwoodMP.World
{
    /// <summary>
    /// The guards every shared world call goes through. Depth: how deep in
    /// patched calls we are, so only the outermost is sent (a hit that
    /// breaks a barricade calls destroyBarricade itself, and the other
    /// games' hit will do the same). Replaying: a call that came from
    /// another game is being made, so it isn't sent back. Sharing: whether
    /// this game shares its world right now.
    ///
    /// One lives for each session, in CoopSession.Guards; patches reach it
    /// through Coop.Guards. With no session that is Alone, which shares
    /// nothing and keeps no count: what it's told is dropped, so playing
    /// alone is unaffected. The core decides (GuardRules).
    /// </summary>
    internal sealed class Guards
    {
        /// <summary>
        /// The guards while there is no session: Sharing is false, and
        /// Depth and Replaying stay 0 and false whatever is set.
        /// </summary>
        public static readonly Guards Alone = new Guards(null);

        private readonly CoopSession _coop;
        private int _depth;
        private bool _replaying;

        public Guards(CoopSession coop)
        {
            _coop = coop;
        }

        /// <summary>
        /// How deep in patched calls we are: a patch's prefix adds one, and
        /// its postfix asks Outermost. Reset every frame, in case a call
        /// threw.
        /// </summary>
        public int Depth
        {
            get => _depth;
            set
            {
                if (_coop != null)
                {
                    _depth = value;
                }
            }
        }

        /// <summary>
        /// True while making a call that came from another game, so it isn't
        /// sent back.
        /// </summary>
        public bool Replaying
        {
            get => _replaying;
            set
            {
                if (_coop != null)
                {
                    _replaying = value;
                }
            }
        }

        public void ResetDepth()
        {
            Depth = 0;
        }

        /// <summary>
        /// A patch's postfix: leaves its call, and says whether it was the
        /// outermost.
        /// </summary>
        public bool Outermost()
        {
            bool outermost = GuardRules.IsOutermost(Depth);
            Depth = GuardRules.Leave(Depth);
            return outermost;
        }

        /// <summary>
        /// Whether this game shares its world right now (the host with
        /// players in, or a client in it). Never while a world loads: the
        /// save loader makes the saved dropped items, thrown things and the
        /// rest again, and a joiner sending those as new would make the host
        /// a second copy of each (WorldShare.Loading). False with no
        /// session.
        /// </summary>
        public bool Sharing
        {
            get
            {
                if (_coop == null)
                {
                    return false;
                }

                return GuardRules.Sharing(FactsOf(_coop.Net));
            }
        }

        /// <summary>
        /// Reads the game in the order the rule looks, and no further than
        /// it needs: Loading asks the game's SaveManager singleton, which is
        /// left alone at the main menu.
        /// </summary>
        private GuardFacts FactsOf(Session s)
        {
            var facts = new GuardFacts { Replaying = _replaying };
            if (facts.Replaying)
            {
                return facts;
            }

            facts = facts with { InWorld = WorldShare.InWorld };
            if (!facts.InWorld)
            {
                return facts;
            }

            return facts with
            {
                Loading = WorldShare.Loading,
                Host = s.IsHost,
                Peers = s.Peers.Count,
                Client = NetEnemies.ClientMode,
            };
        }
    }
}
