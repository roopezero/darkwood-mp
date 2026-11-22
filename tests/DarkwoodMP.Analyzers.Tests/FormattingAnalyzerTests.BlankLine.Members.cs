namespace DarkwoodMP.Analyzers.Tests
{
    public partial class FormattingAnalyzerTests
    {
        public partial class BlankLine
        {
            /// One of each kind of member, with a { } pair over lines.
            public static TheoryData<string> Members => new TheoryData<string>
            {
                """
                void M()
                {
                    B();
                }
                """,
                """
                C()
                {
                    B();
                }
                """,
                """
                public static C operator +(C a, C b)
                {
                    return a;
                }
                """,
                """
                int P
                {
                    get;
                }
                """,
                """
                int this[int i]
                {
                    get => i;
                }
                """,
                """
                event System.Action E
                {
                    add => B();
                    remove => B();
                }
                """,
                """
                int[] all =
                {
                    1,
                };
                """,
                """
                class N
                {
                    int X;
                }
                """,
                """
                enum E
                {
                    A,
                }
                """,
            };

            [Theory]
            [MemberData(nameof(Members))]
            public void Reports_each_kind_of_member(string member)
            {
                var code = "class C\n{\n" + member + "\nint After;\n}\n";
                var last = 2 + member.Split('\n').Length;

                var lines = Snippet.Lines("DW0007", code);

                lines.ShouldBe(new[] { last });
            }

            [Fact]
            public void Allows_a_member_followed_by_a_blank_line()
            {
                var code = """
                    class C
                    {
                        void M()
                        {
                        }

                        void N()
                        {
                        }
                    }
                    """;

                var lines = Snippet.Lines("DW0007", code);

                lines.ShouldBeEmpty();
            }

            [Fact]
            public void Ignores_members_with_braces_on_one_line()
            {
                var code = """
                    class C
                    {
                        public int X { get; set; }
                        public int Y { get; set; }
                    }
                    """;

                var lines = Snippet.Lines("DW0007", code);

                lines.ShouldBeEmpty();
            }

            [Theory]
            [InlineData("int P", "get", "set")]
            [InlineData("int P", "get", "init")]
            [InlineData("event System.Action P", "add", "remove")]
            public void Reports_an_accessor_before_the_next(
                string member,
                string first,
                string second)
            {
                var code = $$"""
                    class C
                    {
                        {{member}}
                        {
                            {{first}}
                            {
                                B();
                            }
                            {{second}}
                            {
                                B();
                            }
                        }
                    }
                    """;

                var lines = Snippet.Lines("DW0007", code);

                lines.ShouldBe(new[] { 8 });
            }

            [Fact]
            public void Ignores_accessors_on_one_line()
            {
                var code = """
                    class C
                    {
                        int P
                        {
                            get { return 1; }
                            set { }
                        }
                    }
                    """;

                var lines = Snippet.Lines("DW0007", code);

                lines.ShouldBeEmpty();
            }

            [Fact]
            public void Reports_a_statement_inside_an_accessor()
            {
                var code = """
                    class C
                    {
                        int P
                        {
                            get
                            {
                                if (A())
                                {
                                    return 1;
                                }
                                return 2;
                            }
                        }
                    }
                    """;

                var lines = Snippet.Lines("DW0007", code);

                lines.ShouldBe(new[] { 10 });
            }

            [Fact]
            public void Reports_a_type_before_the_next_in_a_namespace()
            {
                var code = """
                    namespace N
                    {
                        class A
                        {
                        }
                        class B
                        {
                        }
                    }
                    """;

                var lines = Snippet.Lines("DW0007", code);

                lines.ShouldBe(new[] { 5 });
            }

            [Fact]
            public void Reports_a_type_before_the_next_in_a_file_namespace()
            {
                var code = """
                    namespace N;

                    class A
                    {
                    }
                    class B
                    {
                    }
                    """;

                var lines = Snippet.Lines("DW0007", code);

                lines.ShouldBe(new[] { 5 });
            }

            [Fact]
            public void Reports_a_type_before_the_next_outside_a_namespace()
            {
                var code = """
                    class A
                    {
                    }
                    class B
                    {
                    }
                    """;

                var lines = Snippet.Lines("DW0007", code);

                lines.ShouldBe(new[] { 3 });
            }

            [Fact]
            public void Reports_a_namespace_before_the_next()
            {
                var code = """
                    namespace A
                    {
                    }
                    namespace B
                    {
                    }
                    """;

                var lines = Snippet.Lines("DW0007", code);

                lines.ShouldBe(new[] { 3 });
            }

            [Fact]
            public void Reports_a_top_level_statement()
            {
                var code = """
                    if (A())
                    {
                        B();
                    }
                    C();
                    """;

                var lines = Snippet.Lines("DW0007", code);

                lines.ShouldBe(new[] { 4 });
            }

            [Fact]
            public void Allows_the_end_of_the_file_after_a_comment()
            {
                var code = """
                    class C
                    {
                        void M()
                        {
                        }
                    }
                    // The end.
                    """;

                var lines = Snippet.Lines("DW0007", code);

                lines.ShouldBeEmpty();
            }
        }
    }
}
