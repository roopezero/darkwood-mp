using DarkwoodMP.World.Joining;

namespace DarkwoodMP.Tests.World.Joining
{
    public class JoinWaitsTests
    {
        public class Text
        {
            [Theory]
            [InlineData(
                JoinWait.Intro,
                "The host is watching the prologue's intro. You'll join when"
                    + " it's over.")]
            [InlineData(
                JoinWait.BetweenPrologueParts,
                "The host is between parts of the prologue. You'll join in a"
                    + " moment.")]
            [InlineData(
                JoinWait.Dreaming,
                "The host is in a dream. You'll join when they wake up.")]
            [InlineData(
                JoinWait.Away,
                "The host is away in another place. You'll join when they're"
                    + " back.")]
            [InlineData(JoinWait.Loading, "The host's world is loading.")]
            [InlineData(
                JoinWait.BetweenEpilogueParts,
                "The host is between parts of the epilogue. You'll join them"
                    + " in a moment.")]
            [InlineData(
                JoinWait.Preparing,
                "The host is saving its world for you...")]
            [InlineData(
                JoinWait.WorldChanged,
                "The host's world changed while you were getting it: starting"
                    + " again...")]
            [InlineData(JoinWait.None, null)]
            public void Keeps_the_words(JoinWait why, string text)
            {
                JoinWaits.Text(why).ShouldBe(text);
            }
        }
    }
}
