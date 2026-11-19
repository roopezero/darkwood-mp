namespace DarkwoodMP.Analyzers.Tests
{
    public partial class FormattingAnalyzerTests
    {
        public partial class BlankLine
        {
            [Fact]
            public void Allows_a_closing_brace_as_the_next_token()
            {
                var code = """
                    if (A())
                    {
                        B();
                    }
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBeEmpty();
            }

            [Fact]
            public void Allows_a_closing_brace_after_a_comment()
            {
                var code = """
                    if (A())
                    {
                        B();
                    }
                    // Nothing more.

                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBeEmpty();
            }

            [Fact]
            public void Allows_a_closing_brace_after_a_directive()
            {
                var code = """
                    if (A())
                    {
                        B();
                    }
                    #region Tail
                    #endregion
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBeEmpty();
            }

            [Fact]
            public void Reports_a_block_followed_by_a_comment()
            {
                var code = """
                    if (A())
                    {
                        B();
                    }
                    // Then C.
                    C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 4 });
            }

            [Fact]
            public void Reports_a_block_followed_by_a_directive()
            {
                var code = """
                    if (A())
                    {
                        B();
                    }
                    #region Tail
                    C();
                    #endregion
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 4 });
            }

            [Fact]
            public void Reports_a_block_with_the_blank_line_after_a_comment()
            {
                var code = """
                    if (A())
                    {
                        B();
                    }
                    // Then C.

                    C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 4 });
            }

            [Fact]
            public void Reports_a_block_with_the_blank_line_after_a_directive()
            {
                var code = """
                    if (A())
                    {
                        B();
                    }
                    #region Tail

                    C();
                    #endregion
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 4 });
            }

            [Fact]
            public void Leaves_a_next_token_on_the_same_line_to_other_rules()
            {
                var code = """
                    if (A())
                    {
                        B();
                    } C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBeEmpty();
            }
        }
    }
}
