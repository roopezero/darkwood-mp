using DarkwoodMP.World.Skills;

namespace DarkwoodMP.Tests.World.Skills
{
    public class SkillPanelTextsTests
    {
        public class Text
        {
            [Theory]
            [InlineData(PanelNote.None, "")]
            [InlineData(PanelNote.Message, "")]
            [InlineData(PanelNote.Refused, "")]
            [InlineData(PanelNote.Asking, "Asking the host...")]
            [InlineData(PanelNote.NoPoints, "No skill points to spend")]
            [InlineData(
                PanelNote.ChooseNegative,
                "Choose a negative skill of its tier to go with it")]
            [InlineData(PanelNote.Learned, "Learned")]
            [InlineData(
                PanelNote.TakenWithFirst,
                "Taken with the first skill of its tier")]
            [InlineData(
                PanelNote.ComesWithNegative,
                "Comes with a negative skill of its tier")]
            [InlineData(PanelNote.CanBeLearned, "Can be learned")]
            public void Says_each_note_in_the_panels_words(
                PanelNote note,
                string words)
            {
                var text = SkillPanelTexts.Text(note);

                text.ShouldBe(words);
            }
        }

        public class Hint
        {
            [Theory]
            [InlineData(1, "Choose a skill to learn.")]
            [InlineData(0, "Take a level at a stove for a skill point.")]
            [InlineData(-1, "Take a level at a stove for a skill point.")]
            public void Hints_at_what_to_do_next(int points, string words)
            {
                var hint = SkillPanelTexts.Hint(points);

                hint.ShouldBe(words);
            }
        }

        public class Capital
        {
            [Theory]
            [InlineData(
                "you have this skill already",
                "You have this skill already")]
            [InlineData("Learned", "Learned")]
            [InlineData("", "")]
            [InlineData(null, null)]
            public void Makes_the_first_letter_capital(string text, string made)
            {
                var capital = SkillPanelTexts.Capital(text);

                capital.ShouldBe(made);
            }
        }
    }
}
