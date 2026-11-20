namespace DarkwoodMP.Tests
{
    /// Every refusal with a detail as its rule builds it, and the words the
    /// host gives for them. The words are the shell's own, checked
    /// character for character against its code at commit f588326.
    public static class RefusalSentences
    {
        public static TheoryData<Refusal, string, string> All() =>
            new TheoryData<Refusal, string, string>
            {
                { Refusal.NotHere, null, "not here" },
                { Refusal.NoKey, "Bunker", "nobody has the key (Bunker)" },
                {
                    Refusal.HitTooLarge,
                    "1500",
                    "a hit of 1500 is more than any weapon does"
                },
                { Refusal.EmptyRequest, null, "empty request" },
                {
                    Refusal.HandlerFailed,
                    "Index was outside the bounds of the array.",
                    "error: Index was outside the bounds of the array."
                },
                {
                    Refusal.OutOfReach,
                    Refusals.Detail("4041", "600", "Wooden door"),
                    "Wooden door is 4041 away from them (reach 600)"
                },
                { Refusal.SessionEnded, null, "the session ended" },
                {
                    Refusal.NothingToBuild,
                    null,
                    "there's nothing to build there (built already?)"
                },
                { Refusal.NoSuchOption, "7", "it has no option 7" },
                { Refusal.NotBroken, null, "it isn't broken" },
                { Refusal.BuiltAlready, null, "it's built already" },
                { Refusal.NoSuchEgg, null, "there's no such egg here" },
                { Refusal.NotAnEgg, "Spider nest", "Spider nest isn't an egg" },
                { Refusal.OnlyLighting, null, "players only light things" },
                {
                    Refusal.NothingToBurn,
                    null,
                    "there's no such thing here to burn"
                },
                { Refusal.CantBurn, "Wooden chair", "Wooden chair can't burn" },
                { Refusal.Broken, "Wooden door", "Wooden door is broken" },
                {
                    Refusal.NoSuchGenerator,
                    "Saved:1234",
                    "no generator Saved:1234 here"
                },
                { Refusal.NoSuchSaw, "Saved:1234", "no saw Saved:1234 here" },
                { Refusal.TooFar, null, "too far from it" },
                { Refusal.NotANumber, null, "not a number" },
                {
                    Refusal.MoreThanACan,
                    Refusals.Detail("25.0", "20"),
                    "25.0 is more than a can holds (20)"
                },
                {
                    Refusal.TankFull,
                    Refusals.Detail("99.5", "100"),
                    "the tank is full (99.5 of 100)"
                },
                {
                    Refusal.GeneratorFuelTaken,
                    null,
                    "a generator's fuel only goes down by itself"
                },
                {
                    Refusal.NotWholeLogs,
                    Refusals.Detail("50.0", "40"),
                    "50.0 isn't a whole number of logs (40 each)"
                },
                { Refusal.NotEnoughFuel, "12.5", "the saw has only 12.5" },
                {
                    Refusal.InfectionOutOfReach,
                    null,
                    "none of that infection is within their reach"
                },
                { Refusal.OtherTalking, "Anna", "Anna is talking to them" },
                {
                    Refusal.JustTalked,
                    "Anna",
                    "Anna just talked to them: try again in a moment"
                },
                { Refusal.AlreadyGone, null, "it's already gone" },
                { Refusal.NotAVine, null, "it isn't a vine" },
                { Refusal.AlreadyCut, null, "it's already cut" },
                {
                    Refusal.HostDecidesShadows,
                    null,
                    "the host decides when a shadow dies"
                },
                { Refusal.NotASwitch, null, "it isn't a switch" },
                { Refusal.AlreadyOn, null, "it's already on" },
                { Refusal.AlreadyOff, null, "it's already off" },
                {
                    Refusal.AnimalsEatLures,
                    null,
                    "lures are eaten by the host's animals"
                },
                { Refusal.CantPickUp, null, "it can't be picked up" },
                { Refusal.NotFlammable, null, "it can't burn" },
                { Refusal.NotAFeeder, null, "it isn't a feeder" },
                {
                    Refusal.FeederUsed,
                    null,
                    "the feeder was already used today"
                },
                {
                    Refusal.ExplosiveGone,
                    null,
                    "it isn't here (gone or already blown up)"
                },
                { Refusal.AlreadyWentOff, null, "it had already gone off" },
                { Refusal.AlreadyBroken, null, "it is already broken" },
                { Refusal.Unbreakable, null, "it can't be broken" },
                { Refusal.NoSuchNpc, null, "no such NPC here" },
                { Refusal.NoSuchLock, null, "there's no lock on it here" },
                { Refusal.NotLocked, null, "it isn't locked" },
                {
                    Refusal.StoryOnly,
                    null,
                    "only the story does that, in the host's game"
                },
                { Refusal.NoSuchThing, null, "no such thing here" },
                {
                    Refusal.HostRollsCrates,
                    null,
                    "a crate is picked open by the host's roll"
                },
                {
                    Refusal.NothingToSearch,
                    null,
                    "there's nothing to search in it"
                },
                { Refusal.NoSuchPadlock, null, "no such padlock here" },
                { Refusal.WrongCode, null, "that isn't the padlock's code" },
                { Refusal.NoLockToPick, null, "there's no lock to pick on it" },
                { Refusal.TrapGone, null, "the trap is gone" },
                {
                    Refusal.TestTrapWentOff,
                    null,
                    "the trap had already gone off (test)"
                },
                { Refusal.TestTrapGone, null, "the trap is gone (test)" },
                { Refusal.TrapWentOff, null, "the trap had already gone off" },
                { Refusal.CantDisarm, null, "the trap can't be disarmed now" },
                { Refusal.NoSuchDoor, null, "no such door here" },
                { Refusal.DoorBroken, null, "the door is broken" },
                { Refusal.DoorLocked, null, "the door is locked" },
                { Refusal.DoorBlocked, null, "the door is blocked" },
                {
                    Refusal.DoorBarricaded,
                    null,
                    "the door is already barricaded"
                },
                {
                    Refusal.DoorNotBarricaded,
                    null,
                    "the door isn't barricaded"
                },
                {
                    Refusal.DoorAlreadyBroken,
                    null,
                    "the door is already broken"
                },
                { Refusal.DoorHasNoLock, null, "the door has no lock to open" },
                { Refusal.DoorNotLocked, null, "the door isn't locked" },
                { Refusal.NoSuchWindow, null, "no such window here" },
                {
                    Refusal.WindowBarricaded,
                    null,
                    "the window is already barricaded"
                },
                {
                    Refusal.WindowNotBarricaded,
                    null,
                    "the window isn't barricaded"
                },
                {
                    Refusal.NotPlaceable,
                    "Generator",
                    "Generator isn't something a player can place"
                },
                {
                    Refusal.NotCarried,
                    "Generator",
                    "they don't carry anything that places Generator"
                },
                {
                    Refusal.NoSuchPrefab,
                    "Generator",
                    "there's no Generator here"
                },
                {
                    Refusal.NoSuchStorage,
                    null,
                    "no such workbench storage here"
                },
                { Refusal.Locked, null, "locked" },
                { Refusal.NotBrokenOpen, null, "locked (not broken open)" },
                {
                    Refusal.HostMovingItem,
                    null,
                    "the host is moving an item in it"
                },
                { Refusal.TraderNotHere, "Wolfman", "Wolfman isn't here" },
                {
                    Refusal.BadStock,
                    "Unexpected end when reading JSON",
                    "bad stock: Unexpected end when reading JSON"
                },
                { Refusal.NoStock, null, "no stock" },
                {
                    Refusal.TookBadAmount,
                    Refusals.Detail("0", "Wood"),
                    "took 0 Wood"
                },
                {
                    Refusal.TraderHasLess,
                    Refusals.Detail("2", "5", "Wood"),
                    "he has 2 Wood, not 5"
                },
                {
                    Refusal.GaveBadAmount,
                    Refusals.Detail("-1", "Wood"),
                    "gave -1 Wood"
                },
                {
                    Refusal.StockMismatch,
                    Refusals.Detail("3", "4", "Wood"),
                    "his stock would have 3 Wood, not 4"
                },
                {
                    Refusal.StockHasExtra,
                    "Wood",
                    "his stock wouldn't have Wood"
                },
                {
                    Refusal.ReputationTooLow,
                    Refusals.Detail("3", "5"),
                    "his reputation is 3, which doesn't pay for 5"
                },
                {
                    Refusal.TakingRaisesReputation,
                    "+2",
                    "taking only can't raise the reputation (+2)"
                },
                {
                    Refusal.GivingLowersReputation,
                    "-2",
                    "giving only can't lower the reputation (-2)"
                },
                { Refusal.ItemNotHere, null, "it isn't here" },
                { Refusal.OtherMoving, "Anna", "Anna is moving it" },
                { Refusal.NoStandIn, null, "no stand-in for that player yet" },
                { Refusal.EnemyNotAlive, null, "that enemy isn't alive here" },
                {
                    Refusal.HitsTooFast,
                    "bare hands",
                    "more hits a second than bare hands makes"
                },
                { Refusal.NoSuchTrigger, null, "no such trigger here" },
                {
                    Refusal.EventSwitchedOff,
                    null,
                    "that event is switched off"
                },
                { Refusal.NotHappened, null, "that hasn't happened here" },
                {
                    Refusal.NoPlayerPart,
                    null,
                    "nothing in it is a player's own"
                },
                {
                    Refusal.HadTheirPart,
                    null,
                    "they have had their part of it"
                },
                {
                    Refusal.RequirementNotMet,
                    "hasItem",
                    "its requirement (hasItem) isn't met here"
                },
                {
                    Refusal.HostRunsWorldEvents,
                    null,
                    "the world's own events are the host's"
                },
                { Refusal.NoSuchEvent, null, "no such event here" },
                {
                    Refusal.NoSuchWorldEvent,
                    "bunkerDoor",
                    "no world event bunkerDoor here"
                },
                {
                    Refusal.WorldEventHappened,
                    "bunkerDoor",
                    "bunkerDoor has already happened"
                },
                {
                    Refusal.NotAnArea,
                    "Trigger_cellar",
                    "Trigger_cellar isn't an area here"
                },
                { Refusal.AlreadyHappened, null, "that has already happened" },
                { Refusal.NothingHappens, null, "nothing happens there" },
                { Refusal.EventsRan, null, "its events have already run" },
                {
                    Refusal.PositionUnknown,
                    null,
                    "where they are isn't known yet"
                },
                {
                    Refusal.NoSuchHideout,
                    null,
                    "no such hideout to take things from"
                },
                { Refusal.NotAtHideout, null, "not at a hideout" },
                { Refusal.AlreadyAtHideout, null, "already at that hideout" },
                {
                    Refusal.NoPackagePlace,
                    "Hideout",
                    "Hideout has no place for the porter's package"
                },
                {
                    Refusal.NoPorterPlace,
                    null,
                    "there's no porter's place there"
                },
                { Refusal.NotAtPlace, "Hideout", "they aren't at Hideout" },
                {
                    Refusal.NotDaytime,
                    null,
                    "the porter comes in the daytime only"
                },
                {
                    Refusal.PorterOnTheWay,
                    null,
                    "the porter is on the way already"
                },
                { Refusal.PorterDead, null, "the porter is dead" },
                { Refusal.PorterThere, null, "the porter is there already" },
                { Refusal.NoSuchOven, null, "no such oven here" },
                {
                    Refusal.OvenNotAtHideout,
                    null,
                    "that oven isn't at a hideout"
                },
                {
                    Refusal.ImplausibleEssence,
                    "250000",
                    "implausible essence 250000"
                },
                {
                    Refusal.EssenceFull,
                    null,
                    "the shared essence bar is full: someone has to take the"
                        + " level at a stove"
                },
                {
                    Refusal.NotMorning,
                    null,
                    "Skills can only be reset in the morning, before you leave"
                        + " your hideout"
                },
                {
                    Refusal.NoSharedProgression,
                    null,
                    "nobody shares the progression"
                },
                {
                    Refusal.AlreadyReset,
                    null,
                    "you already reset your skills today"
                },
                { Refusal.HostNotInWorld, null, "the host isn't in a world" },
                {
                    Refusal.LevelTooHigh,
                    Refusals.Detail("5", "3"),
                    "level 5 is more than the shared essence gives (3)"
                },
                { Refusal.NoSuchSkill, null, "there's no such skill" },
                { Refusal.HaveSkill, null, "you have this skill already" },
                { Refusal.TierClosed, "4", "this tier opens at level 4" },
                { Refusal.TierFull, null, "this tier's two skills are taken" },
                {
                    Refusal.NoSkillPoints,
                    null,
                    "no skill points left: take a level at a stove"
                },
                {
                    Refusal.NeedsNegative,
                    null,
                    "a negative skill of its tier has to be taken with it"
                },
                {
                    Refusal.NoNegativeNeeded,
                    null,
                    "no negative skill goes with this skill"
                },
                {
                    Refusal.NoSuchNegative,
                    null,
                    "there's no such negative skill"
                },
                {
                    Refusal.HaveNegative,
                    null,
                    "you have this negative skill already"
                },
                {
                    Refusal.NegativeWithFirst,
                    null,
                    "this tier's negative skill was taken with its first skill"
                },
                {
                    Refusal.NegativeTaken,
                    null,
                    "this tier's negative skill is taken"
                },
                {
                    Refusal.NegativeOtherTier,
                    null,
                    "it goes with the first skill of its own tier"
                },
                { Refusal.NoSuchStoryStep, null, "no such story step" },
                {
                    Refusal.NotNextChapter,
                    Refusals.Detail("3", "1"),
                    "chapter 3 isn't the next one (this is chapter 1)"
                },
                {
                    Refusal.EpilogueTooEarly,
                    Refusals.Detail("2", "1"),
                    "the epilogue comes after chapter 2 (this is chapter 1)"
                },
                {
                    Refusal.WrongStep,
                    "Chapter2_start",
                    "Chapter2_start doesn't do that"
                },
                {
                    Refusal.EventTooFar,
                    "Chapter2_start",
                    "Chapter2_start is more than 3000 away from them"
                },
                {
                    Refusal.EventNotInWorld,
                    null,
                    "that event isn't in the host's world, and they aren't in"
                        + " a place of their own"
                },
                { Refusal.RunFailed, null, "it couldn't run" },
                {
                    Refusal.NoSavedGame,
                    null,
                    "The host isn't in a saved game yet. Join once they've"
                        + " loaded their world."
                },
                {
                    Refusal.PlayingEnding,
                    null,
                    "The host is playing the ending."
                },
                {
                    Refusal.UnexpectedFile,
                    "sav.exe",
                    "the host offered an unexpected file \"sav.exe\""
                },
                {
                    Refusal.UnknownPacking,
                    "2",
                    "the host packed its world in a way this version can't"
                        + " read (2)"
                },
                {
                    Refusal.BadWorldSize,
                    null,
                    "the host's world has a bad size"
                },
                {
                    Refusal.UnreadableProfile,
                    "Unexpected character",
                    "couldn't read the host's profile: Unexpected character"
                },
                {
                    Refusal.InvalidProfile,
                    null,
                    "the host's profile is invalid"
                },
                { Refusal.BadPiece, null, "bad piece from the host" },
                { Refusal.ShortPiece, null, "a short piece from the host" },
                {
                    Refusal.HostStoppedSending,
                    Refusals.Detail("512", "9216"),
                    "the host stopped sending its world (512 of 9216 KiB"
                        + " came)"
                },
                { Refusal.PathEmpty, null, "the path is empty" },
                { Refusal.PathNotAbsolute, null, "the path isn't absolute" },
                {
                    Refusal.PathInvalid,
                    "Illegal characters",
                    "the path isn't valid (Illegal characters)"
                },
                { Refusal.FilesystemRoot, null, "it's a filesystem root" },
                {
                    Refusal.HomeFolder,
                    null,
                    "it's your home folder or holds it"
                },
                {
                    Refusal.OutsideModFolders,
                    null,
                    "it isn't inside the mod's cache or save-redirect folder"
                },
                {
                    Refusal.NotASaveSlot,
                    "100",
                    "slot 100 isn't a save slot"
                },
                {
                    Refusal.NotPlayersFolder,
                    null,
                    "it isn't prof<N>/darkwoodmp_players in the save folder"
                },
            };
    }
}
