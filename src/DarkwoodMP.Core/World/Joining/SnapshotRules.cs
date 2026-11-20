namespace DarkwoodMP.World.Joining
{
    /// The host's decisions about the snapshot of its world it makes for
    /// joiners (WorldShare.SaveSnapshot, PrepareWorld, Pack).
    public static class SnapshotRules
    {
        /// Whether to make one, first that applies: no SaveManager, a game
        /// that doesn't save or one loading skips; a snapshot of this scene
        /// less than SnapshotReuse old whose sav.dat is there is reused.
        public static SnapshotStep Plan(in SnapshotFacts facts)
        {
            if (!facts.Saves || facts.DontSave || facts.Loading)
            {
                return SnapshotStep.Skip;
            }

            if (facts.Now - facts.LastAt < JoinGateRules.SnapshotReuse
                && facts.LastScene == facts.Scene
                && facts.HasSave)
            {
                return SnapshotStep.Reuse;
            }

            return SnapshotStep.Make;
        }

        /// The dynamic part is written off the main thread (SnapshotSave):
        /// only with the static part copied from the host's save, no new
        /// location to save, the setting on, and not broken this run.
        public static bool OffThread(in OffThreadFacts facts) =>
            facts.HaveStatic
                && !facts.NewLocation
                && facts.Setting
                && !facts.Broken;

        /// After the worker's write failed, the game's own save is tried
        /// only while still in a world that isn't loading.
        public static bool FallsBack(bool inWorld, bool loading) =>
            inWorld && !loading;

        /// The packed world made last is offered again (PrepareWorld): the
        /// files have a stamp (none when left meanwhile), and it is the one
        /// it was made with, in this scene.
        public static bool Reuses(string stamp, string made, bool sameScene) =>
            stamp.Length > 0 && made == stamp && sameScene;

        /// The world is sent packed (Pack) only when that is smaller.
        public static bool SendsPacked(int packed, int raw) => packed < raw;
    }
}
