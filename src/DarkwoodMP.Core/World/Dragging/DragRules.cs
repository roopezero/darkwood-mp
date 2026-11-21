namespace DarkwoodMP.World.Dragging
{
    /// Who drags what (the plugin's DragSync): the host decides, from a
    /// client's DragClaim and its moves, and tells everyone with DragOwner.
    /// The owner table itself stays in the shell; these are the decisions
    /// about one entry of it.
    public static class DragRules
    {
        /// Seconds after which an owner who stopped sending is let go of.
        public const float StaleAfter = 3f;

        /// Seconds a client waits past StaleAfter, for the host's word.
        public const float ClientWait = 2f;

        /// Seconds between two sends of the host's place to a mover who
        /// doesn't drag the thing (strictly more than this).
        public const float CorrectEvery = 0.5f;

        /// Seconds between two sweeps for stale owners.
        public const float SweepEvery = 1f;

        /// Whether the owner has gone quiet: heard more than StaleAfter ago
        /// on the host, more than StaleAfter + ClientWait on a client.
        /// Looks at Heard, Now and Host.
        public static bool IsStale(in OwnerFacts owner)
        {
            float limit = StaleAfter + ClientWait;
            if (owner.Host)
            {
                limit = StaleAfter;
            }

            return Older(owner.Heard, owner.Now, limit);
        }

        /// Someone else who drags the thing now, or 0 for nobody: no valid
        /// name, no entry, our own drag, or a stale one. The shell names
        /// them (Session.NameOf). Owner ids are never 0: DragOwner 0
        /// removes the entry.
        public static ulong OtherOwner(in OwnerFacts owner)
        {
            if (!owner.Named || !owner.Owned)
            {
                return 0;
            }

            if (owner.Owner == owner.Local || IsStale(owner))
            {
                return 0;
            }

            return owner.Owner;
        }

        /// Host, a DragClaim inside a Request: whether the claimer drags
        /// the thing now. In this order: not found, out of reach (the
        /// reach's own refusal), someone else drags it and isn't stale.
        /// The owner claiming again is agreed again, and a claim on a
        /// stale owner's drag takes it over with no Free messages.
        public static ClaimOutcome MayClaim(in ClaimFacts claim)
        {
            if (!claim.Host || !claim.Handling)
            {
                return default;
            }

            if (!claim.Found)
            {
                return Refused(Verdict.Refuse(Refusal.ItemNotHere), false);
            }

            if (claim.Reach.Refused)
            {
                return Refused(claim.Reach, true);
            }

            if (claim.Owned && claim.Owner != claim.From
                && !Older(claim.Heard, claim.Now, StaleAfter))
            {
                var moving = Verdict.Refuse(
                    Refusal.OtherMoving,
                    claim.OwnerName);
                return Refused(moving, true);
            }

            return new ClaimOutcome { Answer = ClaimAnswer.Agreed };
        }

        /// Host, a client's ObjMove: whether it is taken. While someone
        /// drags the thing only their moves are; with nobody dragging it, a
        /// slide (not last) is taken only from a mover near both the thing
        /// and its new place, and an end must be in that reach too. Nothing
        /// else about the move is checked (not its speed, turn or height).
        public static MoveTake TakeMove(in DragMoveFacts move)
        {
            bool owned = move.Owned;
            bool other = owned && move.Owner != move.From;
            bool free = false;
            if (other && Older(move.Heard, move.Now, StaleAfter))
            {
                free = true;
                owned = false;
            }

            if (owned && other)
            {
                return new MoveTake { Correct = Corrects(move) };
            }

            bool near = move.Found && move.NearThing && move.NearPlace;
            if (!owned && !move.Last)
            {
                if (near)
                {
                    return new MoveTake { FreeStale = free, Taken = true };
                }

                return new MoveTake { FreeStale = free, Dropped = true };
            }

            if (!near)
            {
                return new MoveTake
                {
                    FreeStale = free,
                    PutBack = move.Found,
                    Disown = owned,
                };
            }

            if (owned)
            {
                return new MoveTake
                {
                    Refresh = true,
                    EndDrag = move.Last,
                    Taken = true,
                };
            }

            return new MoveTake { FreeStale = free, Taken = true };
        }

        /// The sweep's word on one entry: kept, freed on the host, or
        /// forgotten on a client.
        public static OwnerSweep Sweep(in OwnerFacts owner)
        {
            if (!IsStale(owner))
            {
                return OwnerSweep.Keep;
            }

            if (owner.Host)
            {
                return OwnerSweep.Free;
            }

            return OwnerSweep.Forget;
        }

        /// Whether the sweep forgets when each mover was last sent the
        /// host's place: only when nobody drags anything.
        public static bool ForgetsCorrections(int owners)
        {
            return owners == 0;
        }

        /// Host, a player left: whether this entry is theirs to free ("they
        /// left"). A client keeps it until it goes stale.
        public static bool FreesOnLeave(in OwnerFacts owner, ulong left)
        {
            return owner.Host && owner.Owner == left;
        }

        static bool Corrects(in DragMoveFacts move)
        {
            if (!move.Corrected)
            {
                return true;
            }

            return move.Now.Value - move.CorrectedAt.Value > CorrectEvery;
        }

        static ClaimOutcome Refused(Verdict verdict, bool putBack)
        {
            return new ClaimOutcome
            {
                Answer = ClaimAnswer.Refused,
                Verdict = verdict,
                PutBack = putBack,
            };
        }

        static bool Older(Seconds heard, Seconds now, float limit)
        {
            return now.Value - heard.Value > limit;
        }
    }
}
