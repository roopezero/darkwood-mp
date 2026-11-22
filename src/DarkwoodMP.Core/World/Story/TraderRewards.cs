using System.Collections.Generic;

namespace DarkwoodMP.World.Story
{
    /// The after-night reward each trader got, by name: the day and the
    /// amount. It counts once a day, not once per game: the host keeps the
    /// first one it gets for a day, and a joiner's own is replaced by the
    /// host's. One per session; it lasts across a world reload.
    public sealed class TraderRewards
    {
        readonly Dictionary<string, KeyValuePair<int, int>> _got =
            new Dictionary<string, KeyValuePair<int, int>>();

        /// This game gave a trader the after-night reward (the shell checks
        /// first that the story is shared). If another game's for the day
        /// came first, ours is taken back; otherwise it is remembered and
        /// goes to the others.
        public RewardOutcome Local(string trader, int day, int amount)
        {
            if (trader == null || amount == 0)
            {
                return default;
            }

            if (_got.TryGetValue(trader, out var had) && had.Key == day)
            {
                return new RewardOutcome
                {
                    Kind = RewardKind.TakenBack,
                    Change = -amount,
                    Had = had.Value,
                };
            }

            _got[trader] = new KeyValuePair<int, int>(day, amount);
            return new RewardOutcome { Kind = RewardKind.Sent };
        }

        /// Host: a joiner's reward for a trader. Refused when the trader got
        /// the day's already; otherwise given whole and remembered.
        public RewardOutcome TakeOnHost(string trader, int day, int amount)
        {
            if (_got.TryGetValue(trader, out var had) && had.Key == day)
            {
                return new RewardOutcome
                {
                    Kind = RewardKind.Refused,
                    Had = had.Value,
                };
            }

            _got[trader] = new KeyValuePair<int, int>(day, amount);
            return new RewardOutcome
            {
                Kind = RewardKind.Given,
                Change = amount,
            };
        }

        /// Client: the host's reward for a trader, the one that counts. One
        /// of ours for the same day is replaced by it (the difference is
        /// given); then it is remembered.
        public RewardOutcome TakeOnClient(string trader, int day, int amount)
        {
            int change = amount;
            int earlier = 0;
            if (_got.TryGetValue(trader, out var had) && had.Key == day)
            {
                change = amount - had.Value;
                earlier = had.Value;
            }

            _got[trader] = new KeyValuePair<int, int>(day, amount);
            return new RewardOutcome
            {
                Kind = RewardKind.Given,
                Change = change,
                Had = earlier,
            };
        }

        /// The session ended.
        public void Clear()
        {
            _got.Clear();
        }
    }
}
