using System;

namespace DarkwoodMP.World.Story
{
    /// When the story (flags, NPCs, dialogues, the journal) goes to the
    /// other games, and what a game does with what it gets (the plugin's
    /// FlagsSync).
    public static class StoryRules
    {
        /// Seconds between two looks for what changed (unscaled time).
        public const float Interval = 2f;

        /// Story entries in one FlagsDelta message at most.
        public const int PerMessage = 64;

        /// Whether the story is shared: the world is ready, and this game
        /// hosts someone or plays the host's world.
        public static bool Sharing(in SharingFacts game)
        {
            if (!game.WorldReady)
            {
                return false;
            }

            return (game.Host && game.Peers > 0) || game.Client;
        }

        /// While the story isn't shared: whether what was last sent is kept,
        /// so the changes made meanwhile go when sharing resumes. Only a
        /// pause in the same world keeps it (dead, dreaming, spectating, just
        /// loaded); a world being loaded, another scene, a host with nobody
        /// in, or a game that neither hosts nor plays the host's world starts
        /// again from what there is.
        public static bool KeepsBaseline(in BaselineFacts game)
        {
            bool sameWorld = game.Known && game.Active && game.InWorld
                && game.SameScene;
            if (!sameWorld || game.Loading)
            {
                return false;
            }

            if (game.Host && game.Peers == 0)
            {
                return false;
            }

            return game.Host || game.Client;
        }

        /// Whether it is time to look again: not before the next look.
        public static bool IsDue(Seconds now, Seconds next)
        {
            return !(now.Value < next.Value);
        }

        /// When to look again after a look now.
        public static Seconds NextLook(Seconds now)
        {
            return new Seconds(now.Value + Interval);
        }

        /// Whether a journal entry, key, note or item goes in this game's
        /// story: one found in a dream only while every game is in it.
        public static bool KeepsDreamEntry(bool inDream, bool sharedDream)
        {
            return sharedDream || !inDream;
        }

        /// What a game does with a key another game sent, in this order: a
        /// reward, a reputation, karma at the host, an NPC's state at the
        /// host, anything else.
        public static StoryRoute Route(string key, bool host)
        {
            if (key.StartsWith(StoryKeys.Reward))
            {
                return StoryRoute.Reward;
            }

            if (key.StartsWith(StoryKeys.Reputation))
            {
                return StoryRoute.Reputation;
            }

            if (key == StoryKeys.Karma && host)
            {
                return StoryRoute.HostsKarma;
            }

            if (host && key.StartsWith(StoryKeys.Npc))
            {
                return StoryRoute.NpcCheck;
            }

            return StoryRoute.Apply;
        }

        /// Where a flag another game sent goes: the player's own are
        /// looked for first, then the mod's.
        public static FlagHome FlagHomeOf(string name)
        {
            if (PlayersOwn(name))
            {
                return FlagHome.PlayersOwn;
            }

            if (name.StartsWith(StoryKeys.ModPrefix))
            {
                return FlagHome.Mod;
            }

            return FlagHome.Game;
        }

        /// Flags about this game's own player, which each game keeps for
        /// itself: never sent, and another game's never taken.
        public static bool PlayersOwn(string flag)
        {
            return PlayersPlace(flag) || PlayersPopup(flag);
        }

        /// Where this game's own player is: the game sets these every time
        /// it looks, true only while the player is at that hideout.
        public static bool PlayersPlace(string flag)
        {
            return flag == "player_inFirstHideout"
                || flag == "player_inSecondHideout"
                || flag == "player_inThirdHideout";
        }

        /// The game's one-time help popups this game's player has seen.
        public static bool PlayersPopup(string flag)
        {
            return flag == "player_shownSecondaryAttackPopup"
                || flag == "player_shownMapPopup"
                || flag == "player_shownActiveSkillPopup"
                || flag == "player_firstDrainReloadableItem";
        }

        /// How many entries the message that starts at entry start holds,
        /// of total: PerMessage, or what is left.
        public static int ChunkLength(int start, int total)
        {
            return Math.Min(PerMessage, total - start);
        }
    }
}
