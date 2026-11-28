using DarkwoodMP.Combat;
using DarkwoodMP.Net;

namespace DarkwoodMP.World.Dreaming
{
    /// <summary>
    /// DreamSync for the story's dreams every player dreams together
    /// (StoryDreamRules.Together: the village cellar's). A story step starts
    /// one in the game of the player who set it off (GameEvent startDream,
    /// the player's own part). As that game draws it (Dreams.getPreset):
    /// the host shares it and sends it to everyone (DreamStart), as a level
    /// dream; a joiner goes in at once and sends the host a DreamStart,
    /// which the host takes as the dream to share: it sends it to everyone
    /// (the joiner then knows its dream is the shared one, StartStep.Adopt)
    /// and follows the joiner in, once its own menus let it. From then on
    /// the host runs it, as the level dreams (Whereabouts.SharedDream), and
    /// its end ends everyone's.
    /// </summary>
    internal sealed partial class DreamSync
    {
        /// Host: the pending start is a joiner's story dream we follow.
        private bool _hostFollows;

        private StoryDreamFacts StoryOf(Dreams d, string name)
        {
            bool inDream = false;
            if (d != null)
            {
                inDream = DreamRules.InDream(DreamOf(d));
            }

            return new StoryDreamFacts
            {
                HasSession = true,
                IsHost = S.IsHost,
                ClientMode = NetEnemies.ClientMode,
                Together = StoryDreamRules.Together(name),
                Shared = Shared != null,
                SameDream = Shared == name,
                InDream = inDream,
                LoadingGame = Core.loadingGame,
                EpiloguePlaying = _coop.Epilogue.Playing,
            };
        }

        /// <summary>
        /// Our game drew a dream that isn't the host's level dream: a story
        /// dream every player dreams together is shared (host) or asked of
        /// the host (client).
        /// </summary>
        private void StoryDrawn(DreamPreset preset)
        {
            if (preset == null)
            {
                return;
            }

            string name = preset.name;
            var d = Singleton<Dreams>.Instance;
            StoryStep step = StoryDreamRules.Drawn(StoryOf(d, name));
            var w = S.Begin(MsgType.DreamStart);
            DreamStart.Write(w, name);
            if (step == StoryStep.Share)
            {
                Shared = name;
                _ended = false;
                S.Broadcast(w, Channel.Control, reliable: true);
                Plugin.Log.LogInfo(
                    $"[dream] story dream {name}: everyone goes in");
            }
            else if (step == StoryStep.Ask)
            {
                S.SendToHost(w, Channel.Control, reliable: true);
                Plugin.Log.LogInfo(
                    $"[dream] story dream {name}: we go in, the host shares "
                        + "it");
            }
        }

        /// <summary>
        /// Host: a joiner went into a story dream (its DreamStart).
        /// </summary>
        private void OnStoryAsked(ulong from, string name)
        {
            var d = Singleton<Dreams>.Instance;
            StoryStep step = StoryDreamRules.Asked(StoryOf(d, name));
            var w = S.Begin(MsgType.DreamStart);
            DreamStart.Write(w, name);
            if (step == StoryStep.Confirm)
            {
                S.SendTo(from, w, Channel.Control, reliable: true);
                return;
            }

            if (step != StoryStep.Follow)
            {
                Plugin.Log.LogInfo(
                    $"[dream] {S.NameOf(from)} went into {name}: it stays "
                        + "theirs (we can't follow now)");
                return;
            }

            Shared = name;
            _ended = false;
            S.Broadcast(w, Channel.Control, reliable: true);
            _pendingStart = name;
            _pendingEnd = null;
            _hostFollows = true;
            _startBy = DreamRules.StartBy(Now);
            _startAt = new Seconds(0f);
            Plugin.Log.LogInfo(
                $"[dream] {S.NameOf(from)} went into {name}: everyone goes "
                    + "in");
        }

        /// <summary>
        /// Client: the host's DreamStart names the dream we went into first.
        /// </summary>
        private void Adopt(string name)
        {
            Shared = name;
            _ended = false;
            Plugin.Log.LogInfo(
                $"[dream] the host shares our dream {name}: everyone's in it");
        }

        private static string PresetOf(Dreams d)
        {
            if (d == null || d.preset == null)
            {
                return null;
            }

            return d.preset.name;
        }
    }
}
