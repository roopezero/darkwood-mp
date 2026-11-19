using System;

namespace DarkwoodMP.Analyzers.Tests
{
    public class SnippetTests
    {
        public class Tree
        {
            [Fact]
            public void Refuses_code_that_does_not_parse()
            {
                var code = "class C\n{\n    void M(\n}\n";

                Action parse = () => Snippet.Tree(code);

                parse.ShouldThrow<InvalidOperationException>();
            }
        }
    }
}
