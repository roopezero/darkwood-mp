using System.Linq;

namespace DarkwoodMP.Analyzers.Tests
{
    /// What Find gives a caller that adds the missing lines, such as a
    /// codemod stage. Which places need a blank line is tested through the
    /// analyzer, in FormattingAnalyzerTests+BlankLine.
    public class BlankLinesTests
    {
        public class Find
        {
            [Fact]
            public void Returns_the_last_token_of_each_place()
            {
                var code = """
                    class C
                    {
                        void M()
                        {
                            if (A())
                            {
                                B();
                            }
                            Run(() =>
                            {
                                B();
                            });
                            C();
                        }
                    }
                    """;

                var found = BlankLines.Find(Snippet.Tree(code));

                found.Select(token => token.Text).ShouldBe(new[] { "}", ";" });
            }

            [Fact]
            public void Returns_the_places_in_the_order_of_the_file()
            {
                var code = """
                    class C
                    {
                        void M()
                        {
                            if (A())
                            {
                                B();
                            }
                            C();
                        }
                        void N()
                        {
                        }
                    }
                    """;

                var lines = BlankLines.Find(Snippet.Tree(code))
                    .Select(token => Snippet.LineOf(token.GetLocation()));

                lines.ShouldBe(new[] { 8, 10 });
            }
        }
    }
}
