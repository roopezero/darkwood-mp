namespace DarkwoodMP.World.Clock
{
    /// What a client does with the host's clock.
    public enum ClockMove
    {
        /// No Controller, or not in a world: nothing, and the dream log
        /// stays as it is.
        Ignore,

        /// Dreaming: the world's time waits in Dreams.timeCopy. Log once.
        Wait,

        /// The same minute and day: nothing.
        Nothing,

        /// Step ClockStep.Minutes minutes, each CurrentTime++ and
        /// refreshTime(), then setDay(HostDay) if the day still differs.
        Step,

        /// Too far off: setDay(HostDay), CurrentTime = HostMinute,
        /// refreshTime(afterGameLoad: true).
        Jump,
    }
}
