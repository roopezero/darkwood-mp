namespace DarkwoodMP.Analyzers.Tests
{
    /// One nested class per rule. Each rule has a case that fires and one
    /// that stays quiet, so a change to the code the rules share can't
    /// break one unnoticed. DW0007's cases are in the BlankLine files.
    public partial class FormattingAnalyzerTests
    {
        public class LineTooLong
        {
            [Fact]
            public void Reports_a_line_over_80_columns()
            {
                var code = "class C\n{\n" + Comment(81) + "\n}\n";

                var lines = Snippet.Lines("DW0001", code);

                lines.ShouldBe(new[] { 3 });
            }

            [Fact]
            public void Allows_a_line_of_80_columns()
            {
                var code = "class C\n{\n" + Comment(80) + "\n}\n";

                var lines = Snippet.Lines("DW0001", code);

                lines.ShouldBeEmpty();
            }

            [Fact]
            public void Leaves_a_windows_line_break_out_of_the_count()
            {
                var code = "class C\r\n{\r\n" + Comment(80) + "\r\n}\r\n";

                var lines = Snippet.Lines("DW0001", code);

                lines.ShouldBeEmpty();
            }

            /// A comment line that many columns wide.
            static string Comment(int columns) =>
                "// " + new string('x', columns - 3);
        }

        public class OneLineBlock
        {
            [Fact]
            public void Reports_a_block_on_one_line()
            {
                var code = """
                    B();
                    if (A()) { B(); }
                    """;

                var lines = Snippet.LinesInMethod("DW0002", code);

                lines.ShouldBe(new[] { 2 });
            }

            [Fact]
            public void Reports_a_type_on_one_line()
            {
                var code = """
                    namespace N
                    {
                        class C { }
                    }
                    """;

                var lines = Snippet.Lines("DW0002", code);

                lines.ShouldBe(new[] { 3 });
            }

            [Fact]
            public void Allows_braces_on_their_own_lines()
            {
                var code = """
                    if (A())
                    {
                        B();
                    }
                    """;

                var lines = Snippet.LinesInMethod("DW0002", code);

                lines.ShouldBeEmpty();
            }

            [Fact]
            public void Allows_an_accessor_list_on_one_line()
            {
                var code = """
                    class C
                    {
                        public int X { get; init; }
                    }
                    """;

                var lines = Snippet.Lines("DW0002", code);

                lines.ShouldBeEmpty();
            }

            [Fact]
            public void Allows_an_initializer_on_one_line()
            {
                var code = """
                    var door = new Door { Open = true };
                    """;

                var lines = Snippet.LinesInMethod("DW0002", code);

                lines.ShouldBeEmpty();
            }
        }

        public class Ternary
        {
            [Fact]
            public void Reports_a_conditional_operator()
            {
                var code = """
                    B();
                    var x = A() ? 1 : 2;
                    """;

                var lines = Snippet.LinesInMethod("DW0003", code);

                lines.ShouldBe(new[] { 2 });
            }

            [Fact]
            public void Allows_a_nullable_type()
            {
                var code = """
                    int? x = null;
                    """;

                var lines = Snippet.LinesInMethod("DW0003", code);

                lines.ShouldBeEmpty();
            }
        }

        public class Coalesce
        {
            [Fact]
            public void Reports_a_null_coalescing_operator()
            {
                var code = """
                    B();
                    var x = A() ?? B();
                    """;

                var lines = Snippet.LinesInMethod("DW0004", code);

                lines.ShouldBe(new[] { 2 });
            }

            [Fact]
            public void Leaves_a_null_coalescing_assignment_to_DW0005()
            {
                var code = """
                    x ??= A();
                    """;

                var lines = Snippet.LinesInMethod("DW0004", code);

                lines.ShouldBeEmpty();
            }
        }

        public class CoalesceAssign
        {
            [Fact]
            public void Reports_a_null_coalescing_assignment()
            {
                var code = """
                    B();
                    x ??= A();
                    """;

                var lines = Snippet.LinesInMethod("DW0005", code);

                lines.ShouldBe(new[] { 2 });
            }

            [Fact]
            public void Leaves_a_null_coalescing_operator_to_DW0004()
            {
                var code = """
                    x = x ?? A();
                    """;

                var lines = Snippet.LinesInMethod("DW0005", code);

                lines.ShouldBeEmpty();
            }
        }

        public class NullConditional
        {
            [Fact]
            public void Reports_a_null_conditional_member_access()
            {
                var code = """
                    B();
                    var name = door?.Name;
                    """;

                var lines = Snippet.LinesInMethod("DW0006", code);

                lines.ShouldBe(new[] { 2 });
            }

            [Fact]
            public void Reports_a_null_conditional_element_access()
            {
                var code = """
                    B();
                    var first = doors?[0];
                    """;

                var lines = Snippet.LinesInMethod("DW0006", code);

                lines.ShouldBe(new[] { 2 });
            }

            [Fact]
            public void Allows_a_plain_member_access()
            {
                var code = """
                    var name = door.Name;
                    """;

                var lines = Snippet.LinesInMethod("DW0006", code);

                lines.ShouldBeEmpty();
            }
        }
    }
}
