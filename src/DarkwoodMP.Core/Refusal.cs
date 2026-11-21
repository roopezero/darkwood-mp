namespace DarkwoodMP
{
    /// Why the host refused something. None means it didn't. The words for
    /// each reason are in Refusals.Text. New names go at the end, so that
    /// the values already given stay as they are.
    public enum Refusal : byte
    {
        None = 0,

        /// What the request names isn't in the host's world. An NPC that isn't
        /// here is NoSuchNpc instead: the client compares its words.
        NotHere,

        /// It takes a key nobody has. The detail is the key's type.
        NoKey,

        /// More damage than any weapon does. The detail is the hit,
        /// formatted "0".
        HitTooLarge,

        // Any request.

        /// The request holds no message, or holds another request.
        EmptyRequest,

        /// The host's handler for the request threw. The detail is the
        /// exception's message.
        HandlerFailed,

        /// The player is further from it than the action reaches. The detail is
        /// Refusals.Detail(distance, reach, what), the numbers formatted "0".
        /// The client looks for "(reach" in this text.
        OutOfReach,

        /// The session ended before the host answered. Never sent: the client
        /// gives it to each request still waiting.
        SessionEnded,

        // Building, eggs, fire, fuel, infection and talking.

        /// No construction site is there, or it's built already.
        NothingToBuild,

        /// The construction has no option of that number. The detail is the
        /// number.
        NoSuchOption,

        /// It isn't broken: a repair, or a build that needs a broken thing.
        NotBroken,

        /// The construction is built already, and it isn't broken.
        BuiltAlready,

        /// The egg a client broke isn't in the host's world.
        NoSuchEgg,

        /// What a client broke as an egg isn't one. The detail is its name.
        NotAnEgg,

        /// A client asked to put a fire out: players only light things.
        OnlyLighting,

        /// What a client set on fire isn't in the host's world.
        NothingToBurn,

        /// The item can't burn: it isn't destructible, or it's broken. The
        /// detail is its name.
        CantBurn,

        /// The door a client set on fire is broken. The detail is its name.
        Broken,

        /// The generator isn't in the host's world. The detail is its ObjRef as
        /// text (ObjRef.ToString(), Kind:Id).
        NoSuchGenerator,

        /// The saw isn't in the host's world. The detail is its ObjRef as text
        /// (ObjRef.ToString(), Kind:Id).
        NoSuchSaw,

        /// The player isn't near enough to the generator, saw or container.
        TooFar,

        /// The fuel amount isn't a number: NaN or infinite.
        NotANumber,

        /// More fuel poured than a can holds. The detail is
        /// Refusals.Detail(amount, can), formatted "0.0" and "0".
        MoreThanACan,

        /// Fuel poured into a full tank. The detail is
        /// Refusals.Detail(has, holds), formatted "0.0" and "0".
        TankFull,

        /// A client took fuel out of a generator, which only burns it.
        GeneratorFuelTaken,

        /// Fuel taken at a saw isn't a whole number of logs. The detail is
        /// Refusals.Detail(fuel, per log), the fuel formatted "0.0".
        NotWholeLogs,

        /// More fuel taken at a saw than it has. The detail is what it has,
        /// formatted "0.0".
        NotEnoughFuel,

        /// None of the infection a client cleared is here within their reach.
        InfectionOutOfReach,

        /// Another player is talking to the NPC. The detail is their name.
        OtherTalking,

        /// Another player stopped talking to the NPC a moment ago. The detail
        /// is their name.
        JustTalked,

        // Items, switches, explosives, locks and traps.

        /// What a client picked up isn't in the host's world any more.
        AlreadyGone,

        /// What a client cut isn't a vine.
        NotAVine,

        /// The vine a client hit is cut already.
        AlreadyCut,

        /// A client said a shadow died, which only the host says.
        HostDecidesShadows,

        /// What a client switched isn't a switch.
        NotASwitch,

        /// A client switched on a switch that is on already.
        AlreadyOn,

        /// A client switched off a switch that is off already.
        AlreadyOff,

        /// A client picked up a lure, which only the host's animals take.
        AnimalsEatLures,

        /// What a client picked up is neither a note nor a death drop.
        CantPickUp,

        /// The liquid a client lit can't burn.
        NotFlammable,

        /// What a client used as a feeder isn't one.
        NotAFeeder,

        /// The feeder was used already today.
        FeederUsed,

        /// The explosive isn't in the host's world: gone, or blown up already.
        ExplosiveGone,

        /// The explosive had gone off already.
        AlreadyWentOff,

        /// The item a client hit or broke is broken already.
        AlreadyBroken,

        /// A client broke an item that can't be broken.
        Unbreakable,

        /// The NPC isn't in the host's world. The client compares this text, so
        /// it is never NotHere.
        NoSuchNpc,

        /// The lock a client opened isn't in the host's world.
        NoSuchLock,

        /// The lock a client opened isn't locked.
        NotLocked,

        /// Only a scripted event does this, in the host's game: locking or
        /// unblocking a door, or opening a lock with no key, lockpick or code.
        StoryOnly,

        /// The container a client opened isn't in the host's world.
        NoSuchThing,

        /// A client opened a crate that isn't picked open: the host's roll does
        /// that.
        HostRollsCrates,

        /// What a client searched has no inventory.
        NothingToSearch,

        /// The padlock isn't in the host's world.
        NoSuchPadlock,

        /// The code a client entered isn't the padlock's.
        WrongCode,

        /// A client picked the lock of something that isn't a crate.
        NoLockToPick,

        /// The trap isn't in the host's world.
        TrapGone,

        /// The map test's trap-refuse race: the trap went off in the host's
        /// game just before.
        TestTrapWentOff,

        /// The map test's trap-refuse: the trap was taken out of the host's
        /// game just before.
        TestTrapGone,

        /// The trap a client set off had gone off already.
        TrapWentOff,

        /// The trap can't be disarmed: it went off, or it can't be at all.
        CantDisarm,

        // Doors and windows.

        /// The door isn't in the host's world.
        NoSuchDoor,

        /// A client opened a door that is broken.
        DoorBroken,

        /// A client opened a door that is locked.
        DoorLocked,

        /// A client opened a door that is blocked.
        DoorBlocked,

        /// A client barricaded a door that is barricaded already.
        DoorBarricaded,

        /// A client broke a door's barricade, and it has none.
        DoorNotBarricaded,

        /// A client hit a door that is broken already.
        DoorAlreadyBroken,

        /// A client unlocked a door that has no lock.
        DoorHasNoLock,

        /// A client unlocked a door that isn't locked.
        DoorNotLocked,

        /// The window isn't in the host's world.
        NoSuchWindow,

        /// A client barricaded a window that is barricaded already.
        WindowBarricaded,

        /// A client broke a window's barricade, and it has none.
        WindowNotBarricaded,

        // Placing things, containers and trading.

        /// What a client placed isn't something a player can place. The detail
        /// is the prefab's name.
        NotPlaceable,

        /// A client placed something it carries nothing to place with. The
        /// detail is the prefab's name.
        NotCarried,

        /// The host's game has no prefab of the name a client placed. The
        /// detail is the name.
        NoSuchPrefab,

        /// The workbench storage a client sorted isn't in the host's world.
        NoSuchStorage,

        /// The container is locked in the host's world: a crate not picked
        /// open, or a lock that is locked.
        Locked,

        /// The container has to be broken open first, as a larva egg is.
        NotBrokenOpen,

        /// The host's player is moving an item in the storage a client sorted.
        HostMovingItem,

        /// The trader isn't in the host's world. The detail is the name.
        TraderNotHere,

        /// The trader's new stock can't be read. The detail is the error's
        /// message.
        BadStock,

        /// The trade sent no stock for the trader.
        NoStock,

        /// A trade took none, or less than none, of an item. The detail is
        /// Refusals.Detail(amount, item).
        TookBadAmount,

        /// A trade took more of an item than the trader has. The detail is
        /// Refusals.Detail(has, took, item).
        TraderHasLess,

        /// A trade gave none, or less than none, of an item. The detail is
        /// Refusals.Detail(amount, item).
        GaveBadAmount,

        /// The trader's new stock doesn't add up. The detail is
        /// Refusals.Detail(should hold, holds, item).
        StockMismatch,

        /// The trader's new stock holds an item he wouldn't have. The detail is
        /// the item.
        StockHasExtra,

        /// The trader's reputation can't pay for the trade. The detail is
        /// Refusals.Detail(reputation, cost).
        ReputationTooLow,

        /// A trade that only took raised the reputation. The detail is the
        /// change, formatted "+0".
        TakingRaisesReputation,

        /// A trade that only gave lowered the reputation. The detail is the
        /// change.
        GivingLowersReputation,

        // Moving things.

        /// The thing a client took hold of isn't in the host's world.
        ItemNotHere,

        /// Another player is moving it. The detail is their name. The client
        /// looks for the end of this text.
        OtherMoving,

        // Enemies and NPCs.

        /// The host has no stand-in for the player yet.
        NoStandIn,

        /// The enemy a client hit isn't alive, or isn't in play, in the host's
        /// world.
        EnemyNotAlive,

        /// More hits a second than the weapon makes. The detail is the weapon's
        /// type, or "bare hands".
        HitsTooFast,

        // Scripted events.

        /// The event trigger isn't in the host's world.
        NoSuchTrigger,

        /// The event trigger is switched off in the host's world.
        EventSwitchedOff,

        /// A client asked for its part of an event that hasn't happened in the
        /// host's world.
        NotHappened,

        /// The event has no part that is a player's own.
        NoPlayerPart,

        /// The player has had their part of the event already.
        HadTheirPart,

        /// One of the trigger's requirements isn't met in the host's world. The
        /// detail is the requirement's type.
        RequirementNotMet,

        /// A client sent one of the world's own events, which only the host's
        /// world sends.
        HostRunsWorldEvents,

        /// The event isn't in the host's world.
        NoSuchEvent,

        /// The host's world has no world event of that name. The detail is the
        /// name.
        NoSuchWorldEvent,

        /// The world event has happened already, and it happens once. The
        /// detail is its name.
        WorldEventHappened,

        /// The trigger a client left isn't an area. The detail is its name.
        NotAnArea,

        /// The trigger has fired already, and it fires once.
        AlreadyHappened,

        /// The trigger has no events to run.
        NothingHappens,

        /// The trigger's events have run already, and they run once.
        EventsRan,

        // The porter and the ovens.

        /// The host doesn't know where the player is yet: no stand-in.
        PositionUnknown,

        /// The hideout to take things from isn't one.
        NoSuchHideout,

        /// The player isn't at a hideout.
        NotAtHideout,

        /// The player is at the hideout to take things from.
        AlreadyAtHideout,

        /// The hideout has no place for the porter's package. The detail is its
        /// name.
        NoPackagePlace,

        /// There's no place for the porter where a client called him.
        NoPorterPlace,

        /// The player isn't at the place they called the porter to. The detail
        /// is its name.
        NotAtPlace,

        /// A client called the porter at night.
        NotDaytime,

        /// The porter is on the way already.
        PorterOnTheWay,

        /// The porter is dead.
        PorterDead,

        /// The porter is at that place already.
        PorterThere,

        /// The oven a client lit isn't in the host's world.
        NoSuchOven,

        /// The oven a client lit isn't at a hideout.
        OvenNotAtHideout,

        // Essence, skill resets and skills.

        /// The essence a client cooked is 0 or less, or over 100000. The detail
        /// is the amount.
        ImplausibleEssence,

        /// A client cooked essence while the shared bar is full: someone has to
        /// take a level first.
        EssenceFull,

        /// A client reset its skills outside the player's morning.
        NotMorning,

        /// A client reset its skills when nobody shares the progression.
        NoSharedProgression,

        /// The player reset their skills today already.
        AlreadyReset,

        /// Skills learned while the host isn't in a world (no skill tiers).
        HostNotInWorld,

        /// The level a client claims is more than the shared essence gives. The
        /// detail is Refusals.Detail(level, most).
        LevelTooHigh,

        /// The skill isn't one of the game's good skills.
        NoSuchSkill,

        /// The player has the skill already.
        HaveSkill,

        /// The skill's tier opens at a later level. The detail is that level.
        TierClosed,

        /// The tier's two good skills are taken.
        TierFull,

        /// The player has no skill points left.
        NoSkillPoints,

        /// The skill needs a negative skill of its tier, and none was chosen.
        NeedsNegative,

        /// A negative skill was chosen with a skill that needs none.
        NoNegativeNeeded,

        /// The negative skill isn't one of the game's.
        NoSuchNegative,

        /// The player has the negative skill already.
        HaveNegative,

        /// The tier's negative skill was taken with its first good skill.
        NegativeWithFirst,

        /// The tier's negative skill is taken.
        NegativeTaken,

        /// The negative skill is of another tier than the good skill.
        NegativeOtherTier,

        // The story's chapters.

        /// The story step a client set off isn't a chapter, the epilogue or the
        /// end screen.
        NoSuchStoryStep,

        /// The chapter a client started isn't the host's next. The detail is
        /// Refusals.Detail(chapter, now).
        NotNextChapter,

        /// A client set off the epilogue before the last chapter. The detail is
        /// Refusals.Detail(last, now).
        EpilogueTooEarly,

        /// The event's step isn't the story step the client named. The detail
        /// is the event's name.
        WrongStep,

        /// The player was more than 3000 from the story's event. The detail is
        /// the event's name.
        EventTooFar,

        /// The story's event isn't in the host's world, and the player isn't in
        /// a place of their own.
        EventNotInWorld,

        /// The host's game couldn't run the story step.
        RunFailed,

        // Joining: these go in Refused, not in a Verdict.

        /// A player joined while the host isn't in a saved game.
        NoSavedGame,

        /// A player joined while the host plays the ending.
        PlayingEnding,

        // Containers' slots and the workbench's level: the host logs these
        // and sends each change back (ContainerSlotRefused), or for the
        // level only logs them.

        /// Another player has the container open. The detail is their name.
        HasItOpen,

        /// A slot of a batch of changes had changed on the host. The detail
        /// is Refusals.Detail(slot, changes): the slot's number and how many
        /// changes the batch holds.
        SlotChanged,

        /// The one slot a client changed had changed on the host.
        JustChanged,

        /// A slot would hold more than one stack does. The detail is the
        /// slot's number.
        OverStack,

        /// The workbench level a client raised isn't one above the host's.
        /// The detail is Refusals.Detail(level, current).
        LevelNotNext,

        /// The player who raised the workbench level isn't near a workbench.
        NotAtWorkbench,

        // Getting the host's world: the client gives up on the download
        // with one of these, in "Couldn't get the host's world: {text}".

        /// The offer names a file the world doesn't have, or one twice. The
        /// detail is the file's name.
        UnexpectedFile,

        /// The world was packed in a way this version can't unpack. The
        /// detail is the packing's number.
        UnknownPacking,

        /// The offer's sizes don't add up, or are 0 or too large.
        BadWorldSize,

        /// The host's profile isn't JSON the game reads. The detail is the
        /// reader's message.
        UnreadableProfile,

        /// The host's profile is missing, or its slot isn't 1 to 99.
        InvalidProfile,

        /// A piece is empty, not on a piece's boundary, or past the end.
        BadPiece,

        /// A piece holds fewer bytes than it says.
        ShortPiece,

        /// No piece came after too many stalls. The detail is
        /// Refusals.Detail(got, total), both in KiB.
        HostStoppedSending,

        // Deleting a folder: the mod refuses to, logging
        // "Not deleting {path}: {text}".

        /// The path is null, empty or only spaces.
        PathEmpty,

        /// The path isn't rooted.
        PathNotAbsolute,

        /// The full path can't be made. The detail is the error's message.
        PathInvalid,

        /// The path is a filesystem's root.
        FilesystemRoot,

        /// The path is the user's home folder or holds it.
        HomeFolder,

        /// The path isn't strictly inside the mod's cache or the save
        /// redirect's folder.
        OutsideModFolders,

        /// The players' folder of a slot outside 1 to 99. The detail is the
        /// slot.
        NotASaveSlot,

        /// The players' folder isn't exactly prof<N>/darkwoodmp_players in the
        /// save folder.
        NotPlayersFolder,
    }
}
