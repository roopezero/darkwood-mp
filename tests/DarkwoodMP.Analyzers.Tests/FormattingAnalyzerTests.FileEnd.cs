namespace DarkwoodMP.Analyzers.Tests
{
    public partial class FormattingAnalyzerTests
    {
        public class FileEnd
        {
            [Fact]
            public void Allows_a_file_that_ends_with_one_line_break()
            {
                var code = "class C\n{\n}\n";

                var lines = Snippet.Lines("DW0008", code);

                lines.ShouldBeEmpty();
            }

            [Fact]
            public void Allows_a_windows_line_break_at_the_end()
            {
                var code = "class C\r\n{\r\n}\r\n";

                var lines = Snippet.Lines("DW0008", code);

                lines.ShouldBeEmpty();
            }

            [Fact]
            public void Allows_an_empty_file()
            {
                var lines = Snippet.Lines("DW0008", "");

                lines.ShouldBeEmpty();
            }

            [Fact]
            public void Reports_a_last_line_without_a_line_break()
            {
                var code = "class C\n{\n}";

                var lines = Snippet.Lines("DW0008", code);

                lines.ShouldBe(new[] { 3 });
            }

            [Fact]
            public void Reports_a_blank_line_after_the_last_line()
            {
                var code = "class C\n{\n}\n\n";

                var lines = Snippet.Lines("DW0008", code);

                lines.ShouldBe(new[] { 4 });
            }

            [Fact]
            public void Reports_blank_lines_once_from_the_first()
            {
                var code = "class C\n{\n}\n\n  \n\n";

                var lines = Snippet.Lines("DW0008", code);

                lines.ShouldBe(new[] { 4 });
            }
        }
    }
}
