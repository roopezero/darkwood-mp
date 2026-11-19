namespace DarkwoodMP.Analyzers.Tests
{
    public partial class FormattingAnalyzerTests
    {
        /// DW0007: a statement, member or accessor that holds a { } pair
        /// spread over more than one line is followed by a blank line,
        /// unless the next token is a } or the file ends. This file has the
        /// statements; FormattingAnalyzerTests.BlankLine.Members.cs has the
        /// members, accessors and a file's top-level code, and
        /// FormattingAnalyzerTests.BlankLine.Next.cs what may come after
        /// the last token: blank lines, comments, directives, a }.
        public partial class BlankLine
        {
            [Fact]
            public void Reports_an_if_followed_by_a_statement()
            {
                var code = """
                    if (A())
                    {
                        B();
                    }
                    C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 4 });
            }

            [Fact]
            public void Allows_an_if_followed_by_a_blank_line()
            {
                var code = """
                    if (A())
                    {
                        B();
                    }

                    C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBeEmpty();
            }

            [Fact]
            public void Allows_a_line_of_spaces_as_the_blank_line()
            {
                var code = string.Join(
                    "\n", "if (A())", "{", "    B();", "}", "    ", "C();");

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBeEmpty();
            }

            [Theory]
            [InlineData("for (;;)")]
            [InlineData("foreach (var x in A())")]
            [InlineData("while (A())")]
            [InlineData("using (A())")]
            [InlineData("lock (A())")]
            public void Reports_each_statement_with_a_body(string head)
            {
                var code = $$"""
                    {{head}}
                    {
                        B();
                    }
                    C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 4 });
            }

            [Fact]
            public void Reports_a_block_statement()
            {
                var code = """
                    {
                        B();
                    }
                    C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 3 });
            }

            [Fact]
            public void Reports_an_if_else_chain_after_its_last_brace()
            {
                var code = """
                    if (A())
                    {
                        B();
                    }
                    else if (C())
                    {
                        B();
                    }
                    else
                    {
                        B();
                    }
                    C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 12 });
            }

            [Fact]
            public void Reports_a_try_after_its_last_catch()
            {
                var code = """
                    try
                    {
                        B();
                    }
                    catch (System.ArgumentException)
                    {
                        B();
                    }
                    catch (System.Exception)
                    {
                        B();
                    }
                    C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 12 });
            }

            [Fact]
            public void Reports_a_try_after_its_finally()
            {
                var code = """
                    try
                    {
                        B();
                    }
                    catch
                    {
                        B();
                    }
                    finally
                    {
                        B();
                    }
                    C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 12 });
            }

            [Fact]
            public void Reports_a_do_while_after_its_while()
            {
                var code = """
                    do
                    {
                        B();
                    }
                    while (A());
                    C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 5 });
            }

            [Fact]
            public void Reports_a_switch_statement()
            {
                var code = """
                    switch (A())
                    {
                        case 1:
                            B();
                            break;
                    }
                    C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 6 });
            }

            [Fact]
            public void Reports_a_case_block_before_the_next_label()
            {
                var code = """
                    switch (A())
                    {
                        case 1:
                        {
                            B();
                            break;
                        }
                        case 2:
                        {
                            B();
                            break;
                        }
                        default:
                            break;
                    }
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 7, 12 });
            }

            [Fact]
            public void Allows_a_case_block_at_the_end_of_the_switch()
            {
                var code = """
                    switch (A())
                    {
                        case 1:
                        {
                            B();
                            break;
                        }
                    }
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBeEmpty();
            }

            [Fact]
            public void Reports_a_statement_in_a_switch_section()
            {
                var code = """
                    switch (A())
                    {
                        case 1:
                            if (A())
                            {
                                B();
                            }
                            break;
                    }
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 7 });
            }

            [Fact]
            public void Reports_a_local_function()
            {
                var code = """
                    void D()
                    {
                        B();
                    }
                    C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 4 });
            }

            [Fact]
            public void Reports_a_statement_inside_a_lambda()
            {
                var code = """
                    Run(() =>
                    {
                        if (A())
                        {
                            B();
                        }
                        C();
                    });
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 6 });
            }

            /// Statements four lines long that end after a { } pair: a lambda
            /// with a body, then each kind of initializer, a with, a switch
            /// expression and a property pattern.
            public static TheoryData<string> Pairs => new TheoryData<string>
            {
                """
                AsPlayer(() =>
                {
                    B();
                });
                """,
                """
                _win = new UiWindow(0x0D41, "Start Game")
                {
                    Width = 400,
                };
                """,
                """
                System.Action act = () =>
                {
                    B();
                };
                """,
                """
                var most = all.Select(x =>
                {
                    return x + 1;
                }).Max();
                """,
                """
                var list = new List<int>
                {
                    1,
                };
                """,
                """
                int[] all =
                {
                    1,
                };
                """,
                """
                var point = new
                {
                    X = 1,
                };
                """,
                """
                var moved = point with
                {
                    X = 2,
                };
                """,
                """
                var name = A() switch
                {
                    _ => "one",
                };
                """,
                """
                var near = A() is
                {
                    X: 1,
                };
                """,
            };

            [Theory]
            [MemberData(nameof(Pairs))]
            public void Reports_a_statement_that_holds_a_pair(string pair)
            {
                var code = pair + "\nC();";

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 4 });
            }

            [Fact]
            public void Ignores_a_brace_in_the_middle_of_a_statement()
            {
                var code = """
                    Ask("Leave?", () =>
                    {
                        B();
                    }, "Discard", () =>
                    {
                        C();
                    });
                    D();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 7 });
            }

            [Fact]
            public void Reports_a_call_chain_at_its_end()
            {
                var code = """
                    var some = all.Where(x =>
                    {
                        return x > 1;
                    })
                        .ToList();
                    C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBe(new[] { 5 });
            }

            [Fact]
            public void Ignores_the_braces_of_an_interpolation()
            {
                var code = """
                    var text = $"{A(
                        1)}";
                    C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBeEmpty();
            }

            [Fact]
            public void Ignores_a_pair_on_one_line()
            {
                var code = """
                    var door = new Door { Open = true };
                    C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBeEmpty();
            }

            [Fact]
            public void Ignores_a_statement_over_lines_without_a_pair()
            {
                var code = """
                    Run(
                        1,
                        2);
                    C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBeEmpty();
            }

            [Fact]
            public void Ignores_a_statement_over_lines_with_pairs_on_one_line()
            {
                var code = """
                    Run(new A { X = 1 },
                        new B { Y = 2 });
                    C();
                    """;

                var lines = Snippet.LinesInMethod("DW0007", code);

                lines.ShouldBeEmpty();
            }
        }
    }
}
