using DarkwoodMP.World.Skills;

namespace DarkwoodMP.Tests.World.Skills
{
    public class SkillNotesTests
    {
        public class Text
        {
            [Theory]
            [InlineData(SkillNote.None, "")]
            [InlineData(SkillNote.NotInWorld, "not in a world")]
            [InlineData(SkillNote.Waiting, "waiting for the host")]
            [InlineData(SkillNote.Busy, "busy")]
            [InlineData(SkillNote.Watching, "watching")]
            [InlineData(SkillNote.NoSkills, "no skills")]
            public void Says_each_note_in_the_shells_words(
                SkillNote note,
                string words)
            {
                var text = SkillNotes.Text(note);

                text.ShouldBe(words);
            }
        }
    }
}
