using System;

namespace DarkwoodMP
{
    /// The sentence for each refusal, in one table. The host logs it and
    /// sends it to the client, and tools/tests/authtest.py prints it, so a
    /// text that moves here keeps its exact words.
    public static class Refusals
    {
        /// Between the values of a detail with more than one. A number never
        /// holds it, but a name could, so a name goes last: Fill keeps the
        /// last value whole.
        const string Separator = "\u001f";

        /// The detail of a refusal whose words name two values, in the
        /// order its comment in Refusal gives.
        public static string Detail(string first, string second) =>
            first + Separator + second;

        /// The detail of a refusal whose words name three values, in the
        /// order its comment in Refusal gives.
        public static string Detail(
            string first,
            string second,
            string third) =>
            first + Separator + second + Separator + third;

        /// What to say about a verdict, with its detail in the sentence.
        /// Empty when nothing was refused.
        public static string Text(Verdict verdict)
        {
            var detail = verdict.Detail;
            switch (verdict.Reason)
            {
                case Refusal.None:
                    return "";
                case Refusal.NotHere:
                    return "not here";
                case Refusal.NoKey:
                    return "nobody has the key (" + detail + ")";
                case Refusal.HitTooLarge:
                    return "a hit of " + detail
                        + " is more than any weapon does";
                case Refusal.EmptyRequest:
                    return "empty request";
                case Refusal.HandlerFailed:
                    return "error: " + detail;
                case Refusal.OutOfReach:
                    return Fill(
                        "{2} is {0} away from them (reach {1})", detail, 3);
                case Refusal.SessionEnded:
                    return "the session ended";
                case Refusal.NothingToBuild:
                    return "there's nothing to build there (built already?)";
                case Refusal.NoSuchOption:
                    return "it has no option " + detail;
                case Refusal.NotBroken:
                    return "it isn't broken";
                case Refusal.BuiltAlready:
                    return "it's built already";
                case Refusal.NoSuchEgg:
                    return "there's no such egg here";
                case Refusal.NotAnEgg:
                    return detail + " isn't an egg";
                case Refusal.OnlyLighting:
                    return "players only light things";
                case Refusal.NothingToBurn:
                    return "there's no such thing here to burn";
                case Refusal.CantBurn:
                    return detail + " can't burn";
                case Refusal.Broken:
                    return detail + " is broken";
                case Refusal.NoSuchGenerator:
                    return "no generator " + detail + " here";
                case Refusal.NoSuchSaw:
                    return "no saw " + detail + " here";
                case Refusal.TooFar:
                    return "too far from it";
                case Refusal.NotANumber:
                    return "not a number";
                case Refusal.MoreThanACan:
                    return Fill(
                        "{0} is more than a can holds ({1})", detail, 2);
                case Refusal.TankFull:
                    return Fill("the tank is full ({0} of {1})", detail, 2);
                case Refusal.GeneratorFuelTaken:
                    return "a generator's fuel only goes down by itself";
                case Refusal.NotWholeLogs:
                    return Fill(
                        "{0} isn't a whole number of logs ({1} each)",
                        detail, 2);
                case Refusal.NotEnoughFuel:
                    return "the saw has only " + detail;
                case Refusal.InfectionOutOfReach:
                    return "none of that infection is within their reach";
                case Refusal.OtherTalking:
                    return detail + " is talking to them";
                case Refusal.JustTalked:
                    return detail
                        + " just talked to them: try again in a moment";
                case Refusal.AlreadyGone:
                    return "it's already gone";
                case Refusal.NotAVine:
                    return "it isn't a vine";
                case Refusal.AlreadyCut:
                    return "it's already cut";
                case Refusal.HostDecidesShadows:
                    return "the host decides when a shadow dies";
                case Refusal.NotASwitch:
                    return "it isn't a switch";
                case Refusal.AlreadyOn:
                    return "it's already on";
                case Refusal.AlreadyOff:
                    return "it's already off";
                case Refusal.AnimalsEatLures:
                    return "lures are eaten by the host's animals";
                case Refusal.CantPickUp:
                    return "it can't be picked up";
                case Refusal.NotFlammable:
                    return "it can't burn";
                case Refusal.NotAFeeder:
                    return "it isn't a feeder";
                case Refusal.FeederUsed:
                    return "the feeder was already used today";
                case Refusal.ExplosiveGone:
                    return "it isn't here (gone or already blown up)";
                case Refusal.AlreadyWentOff:
                    return "it had already gone off";
                case Refusal.AlreadyBroken:
                    return "it is already broken";
                case Refusal.Unbreakable:
                    return "it can't be broken";
                case Refusal.NoSuchNpc:
                    return "no such NPC here";
                case Refusal.NoSuchLock:
                    return "there's no lock on it here";
                case Refusal.NotLocked:
                    return "it isn't locked";
                case Refusal.StoryOnly:
                    return "only the story does that, in the host's game";
                case Refusal.NoSuchThing:
                    return "no such thing here";
                case Refusal.HostRollsCrates:
                    return "a crate is picked open by the host's roll";
                case Refusal.NothingToSearch:
                    return "there's nothing to search in it";
                case Refusal.NoSuchPadlock:
                    return "no such padlock here";
                case Refusal.WrongCode:
                    return "that isn't the padlock's code";
                case Refusal.NoLockToPick:
                    return "there's no lock to pick on it";
                case Refusal.TrapGone:
                    return "the trap is gone";
                case Refusal.TestTrapWentOff:
                    return "the trap had already gone off (test)";
                case Refusal.TestTrapGone:
                    return "the trap is gone (test)";
                case Refusal.TrapWentOff:
                    return "the trap had already gone off";
                case Refusal.CantDisarm:
                    return "the trap can't be disarmed now";
                case Refusal.NoSuchDoor:
                    return "no such door here";
                case Refusal.DoorBroken:
                    return "the door is broken";
                case Refusal.DoorLocked:
                    return "the door is locked";
                case Refusal.DoorBlocked:
                    return "the door is blocked";
                case Refusal.DoorBarricaded:
                    return "the door is already barricaded";
                case Refusal.DoorNotBarricaded:
                    return "the door isn't barricaded";
                case Refusal.DoorAlreadyBroken:
                    return "the door is already broken";
                case Refusal.DoorHasNoLock:
                    return "the door has no lock to open";
                case Refusal.DoorNotLocked:
                    return "the door isn't locked";
                case Refusal.NoSuchWindow:
                    return "no such window here";
                case Refusal.WindowBarricaded:
                    return "the window is already barricaded";
                case Refusal.WindowNotBarricaded:
                    return "the window isn't barricaded";
                case Refusal.NotPlaceable:
                    return detail + " isn't something a player can place";
                case Refusal.NotCarried:
                    return "they don't carry anything that places " + detail;
                case Refusal.NoSuchPrefab:
                    return "there's no " + detail + " here";
                case Refusal.NoSuchStorage:
                    return "no such workbench storage here";
                case Refusal.Locked:
                    return "locked";
                case Refusal.NotBrokenOpen:
                    return "locked (not broken open)";
                case Refusal.HostMovingItem:
                    return "the host is moving an item in it";
                case Refusal.TraderNotHere:
                    return detail + " isn't here";
                case Refusal.BadStock:
                    return "bad stock: " + detail;
                case Refusal.NoStock:
                    return "no stock";
                case Refusal.TookBadAmount:
                    return Fill("took {0} {1}", detail, 2);
                case Refusal.TraderHasLess:
                    return Fill("he has {0} {2}, not {1}", detail, 3);
                case Refusal.GaveBadAmount:
                    return Fill("gave {0} {1}", detail, 2);
                case Refusal.StockMismatch:
                    return Fill(
                        "his stock would have {0} {2}, not {1}", detail, 3);
                case Refusal.StockHasExtra:
                    return "his stock wouldn't have " + detail;
                case Refusal.ReputationTooLow:
                    return Fill(
                        "his reputation is {0}, which doesn't pay for {1}",
                        detail, 2);
                case Refusal.TakingRaisesReputation:
                    return "taking only can't raise the reputation (" + detail
                        + ")";
                case Refusal.GivingLowersReputation:
                    return "giving only can't lower the reputation (" + detail
                        + ")";
                case Refusal.ItemNotHere:
                    return "it isn't here";
                case Refusal.OtherMoving:
                    return detail + " is moving it";
                case Refusal.NoStandIn:
                    return "no stand-in for that player yet";
                case Refusal.EnemyNotAlive:
                    return "that enemy isn't alive here";
                case Refusal.HitsTooFast:
                    return "more hits a second than " + detail + " makes";
                case Refusal.NoSuchTrigger:
                    return "no such trigger here";
                case Refusal.EventSwitchedOff:
                    return "that event is switched off";
                case Refusal.NotHappened:
                    return "that hasn't happened here";
                case Refusal.NoPlayerPart:
                    return "nothing in it is a player's own";
                case Refusal.HadTheirPart:
                    return "they have had their part of it";
                case Refusal.RequirementNotMet:
                    return "its requirement (" + detail + ") isn't met here";
                case Refusal.HostRunsWorldEvents:
                    return "the world's own events are the host's";
                case Refusal.NoSuchEvent:
                    return "no such event here";
                case Refusal.NoSuchWorldEvent:
                    return "no world event " + detail + " here";
                case Refusal.WorldEventHappened:
                    return detail + " has already happened";
                case Refusal.NotAnArea:
                    return detail + " isn't an area here";
                case Refusal.AlreadyHappened:
                    return "that has already happened";
                case Refusal.NothingHappens:
                    return "nothing happens there";
                case Refusal.EventsRan:
                    return "its events have already run";
                case Refusal.PositionUnknown:
                    return "where they are isn't known yet";
                case Refusal.NoSuchHideout:
                    return "no such hideout to take things from";
                case Refusal.NotAtHideout:
                    return "not at a hideout";
                case Refusal.AlreadyAtHideout:
                    return "already at that hideout";
                case Refusal.NoPackagePlace:
                    return detail + " has no place for the porter's package";
                case Refusal.NoPorterPlace:
                    return "there's no porter's place there";
                case Refusal.NotAtPlace:
                    return "they aren't at " + detail;
                case Refusal.NotDaytime:
                    return "the porter comes in the daytime only";
                case Refusal.PorterOnTheWay:
                    return "the porter is on the way already";
                case Refusal.PorterDead:
                    return "the porter is dead";
                case Refusal.PorterThere:
                    return "the porter is there already";
                case Refusal.NoSuchOven:
                    return "no such oven here";
                case Refusal.OvenNotAtHideout:
                    return "that oven isn't at a hideout";
                case Refusal.ImplausibleEssence:
                    return "implausible essence " + detail;
                case Refusal.EssenceFull:
                    return "the shared essence bar is full: someone has to"
                        + " take the level at a stove";
                case Refusal.NotMorning:
                    return "Skills can only be reset in the morning, before"
                        + " you leave your hideout";
                case Refusal.NoSharedProgression:
                    return "nobody shares the progression";
                case Refusal.AlreadyReset:
                    return "you already reset your skills today";
                case Refusal.HostNotInWorld:
                    return "the host isn't in a world";
                case Refusal.LevelTooHigh:
                    return Fill(
                        "level {0} is more than the shared essence gives ({1})",
                        detail, 2);
                case Refusal.NoSuchSkill:
                    return "there's no such skill";
                case Refusal.HaveSkill:
                    return "you have this skill already";
                case Refusal.TierClosed:
                    return "this tier opens at level " + detail;
                case Refusal.TierFull:
                    return "this tier's two skills are taken";
                case Refusal.NoSkillPoints:
                    return "no skill points left: take a level at a stove";
                case Refusal.NeedsNegative:
                    return "a negative skill of its tier has to be taken with"
                        + " it";
                case Refusal.NoNegativeNeeded:
                    return "no negative skill goes with this skill";
                case Refusal.NoSuchNegative:
                    return "there's no such negative skill";
                case Refusal.HaveNegative:
                    return "you have this negative skill already";
                case Refusal.NegativeWithFirst:
                    return "this tier's negative skill was taken with its"
                        + " first skill";
                case Refusal.NegativeTaken:
                    return "this tier's negative skill is taken";
                case Refusal.NegativeOtherTier:
                    return "it goes with the first skill of its own tier";
                case Refusal.NoSuchStoryStep:
                    return "no such story step";
                case Refusal.NotNextChapter:
                    return Fill(
                        "chapter {0} isn't the next one (this is chapter {1})",
                        detail, 2);
                case Refusal.EpilogueTooEarly:
                    return Fill(
                        "the epilogue comes after chapter {0} (this is chapter"
                        + " {1})", detail, 2);
                case Refusal.WrongStep:
                    return detail + " doesn't do that";
                case Refusal.EventTooFar:
                    return detail + " is more than 3000 away from them";
                case Refusal.EventNotInWorld:
                    return "that event isn't in the host's world, and they"
                        + " aren't in a place of their own";
                case Refusal.RunFailed:
                    return "it couldn't run";
                case Refusal.NoSavedGame:
                    return "The host isn't in a saved game yet. Join once"
                        + " they've loaded their world.";
                case Refusal.PlayingEnding:
                    return "The host is playing the ending.";
                case Refusal.HasItOpen:
                    return detail + " has it open";
                case Refusal.SlotChanged:
                    return Fill(
                        "slot {0} had just changed, so the {1} changes made"
                        + " with it are refused too", detail, 2);
                case Refusal.JustChanged:
                    return "it had just changed";
                case Refusal.OverStack:
                    return "slot " + detail + ": more than one stack holds";
                case Refusal.LevelNotNext:
                    return Fill(
                        "level {0} isn't one above {1}", detail, 2);
                case Refusal.NotAtWorkbench:
                    return "they aren't at a workbench";
                case Refusal.UnexpectedFile:
                    return "the host offered an unexpected file \""
                        + detail + "\"";
                case Refusal.UnknownPacking:
                    return "the host packed its world in a way this version"
                        + " can't read (" + detail + ")";
                case Refusal.BadWorldSize:
                    return "the host's world has a bad size";
                case Refusal.UnreadableProfile:
                    return "couldn't read the host's profile: " + detail;
                case Refusal.InvalidProfile:
                    return "the host's profile is invalid";
                case Refusal.BadPiece:
                    return "bad piece from the host";
                case Refusal.ShortPiece:
                    return "a short piece from the host";
                case Refusal.HostStoppedSending:
                    return Fill(
                        "the host stopped sending its world"
                            + " ({0} of {1} KiB came)",
                        detail,
                        2);
                case Refusal.PathEmpty:
                    return "the path is empty";
                case Refusal.PathNotAbsolute:
                    return "the path isn't absolute";
                case Refusal.PathInvalid:
                    return "the path isn't valid (" + detail + ")";
                case Refusal.FilesystemRoot:
                    return "it's a filesystem root";
                case Refusal.HomeFolder:
                    return "it's your home folder or holds it";
                case Refusal.OutsideModFolders:
                    return "it isn't inside the mod's cache or save-redirect"
                        + " folder";
                case Refusal.NotASaveSlot:
                    return "slot " + detail + " isn't a save slot";
                case Refusal.NotPlayersFolder:
                    return "it isn't prof<N>/darkwoodmp_players in the save"
                        + " folder";
                default:
                    return verdict.Reason.ToString();
            }
        }

        /// The sentence with a detail's values in it, {0} being the first.
        /// A value the detail lacks is left empty, and the last value keeps
        /// the rest of the detail, separators and all.
        static string Fill(string sentence, string detail, int count)
        {
            var values = new object[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = "";
            }

            if (detail != null)
            {
                var parts = detail.Split(
                    new[] { Separator },
                    count,
                    StringSplitOptions.None);
                parts.CopyTo(values, 0);
            }

            return string.Format(sentence, values);
        }
    }
}
