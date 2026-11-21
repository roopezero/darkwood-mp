namespace DarkwoodMP.World.Scripted
{
    /// How a later player's part treats a step's type (the game's
    /// GameEvent.Type, sorted by the shell).
    public enum StepType : byte
    {
        /// Any other type: a later player gets it if it acts on the player.
        Other = 0,

        /// transportPlayerToObject: a trip, or a new chapter with
        /// activeModifier.
        PlayerTrip,

        /// transportToOutsideLocation, startDream, endDream, returnToWorld,
        /// displayMessage, cameraEffect, timeScale, forbidInputs,
        /// switchCantChangeForbidInputs, switchVisibleUI, addJournalItem,
        /// addAllJournalItems, addRecipes, unlockAchievement.
        Later,

        /// addOrRemoveInvItem.
        Item,

        /// openDialogue, moveInventory, endGame.
        NotLater,
    }
}
