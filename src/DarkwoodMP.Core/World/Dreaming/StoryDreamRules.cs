namespace DarkwoodMP.World.Dreaming
{
    /// <summary>
    /// The story's dreams that every player dreams together. Most dreams a
    /// story step starts (GameEvent startDream) are the player's own, as the
    /// night trader's killer's is. The village cellar's in chapter 2 (the
    /// dive into the flooded basement) is a place of its own, full of doors
    /// and scripted events: dreamt apart, each player had a separate copy of
    /// it. Whoever goes in, the host runs it (DreamSync.Shared), and every
    /// player goes in with them, as into the level dreams.
    /// </summary>
    public static class StoryDreamRules
    {
        /// The village cellar's dream (chapter 2), entered by diving.
        public const string CellarDream = "dream_village_cellar";

        /// Whether every player dreams this preset together.
        public static bool Together(string preset)
        {
            return preset == CellarDream;
        }

        /// What our game does as it draws a story dream (Dreams.getPreset):
        /// the host shares the one everyone dreams together, a client goes
        /// in at once and asks the host. Not while a shared dream is on (a
        /// player following one in draws it too), not while loading.
        public static StoryStep Drawn(in StoryDreamFacts dream)
        {
            if (!dream.HasSession
                || !dream.Together
                || dream.Shared
                || dream.LoadingGame
                || dream.EpiloguePlaying)
            {
                return StoryStep.None;
            }

            if (dream.IsHost)
            {
                return StoryStep.Share;
            }

            if (dream.ClientMode)
            {
                return StoryStep.Ask;
            }

            return StoryStep.None;
        }

        /// Host: what a joiner's DreamStart asks. The one shared already is
        /// told again; any other, while our player is in a dream or the game
        /// loads, stays the joiner's own (dropped).
        public static StoryStep Asked(in StoryDreamFacts dream)
        {
            if (!dream.HasSession || !dream.IsHost || !dream.Together)
            {
                return StoryStep.None;
            }

            if (dream.Shared)
            {
                if (dream.SameDream)
                {
                    return StoryStep.Confirm;
                }

                return StoryStep.None;
            }

            if (dream.InDream || dream.LoadingGame || dream.EpiloguePlaying)
            {
                return StoryStep.None;
            }

            return StoryStep.Follow;
        }
    }
}
