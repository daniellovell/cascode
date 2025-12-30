using System;
using Cascode.Workspace;

namespace Cascode.Workspace.Tests;

public sealed class WorkspaceBashEnvironmentTests
{
    [Fact]
    public void RemoveInlineComment_BasicCommentRemoval()
    {
        var input = "export FOO=bar # this is a comment";
        var result = WorkspaceBashEnvironment.RemoveInlineComment(input);
        Assert.Equal("export FOO=bar ", result);
    }

    [Fact]
    public void RemoveInlineComment_NoComment()
    {
        var input = "export FOO=bar";
        var result = WorkspaceBashEnvironment.RemoveInlineComment(input);
        Assert.Equal("export FOO=bar", result);
    }

    [Fact]
    public void RemoveInlineComment_HashInSingleQuotes()
    {
        var input = "export FOO='bar#baz' # comment";
        var result = WorkspaceBashEnvironment.RemoveInlineComment(input);
        Assert.Equal("export FOO='bar#baz' ", result);
    }

    [Fact]
    public void RemoveInlineComment_HashInDoubleQuotes()
    {
        var input = "export FOO=\"bar#baz\" # comment";
        var result = WorkspaceBashEnvironment.RemoveInlineComment(input);
        Assert.Equal("export FOO=\"bar#baz\" ", result);
    }

    [Fact]
    public void RemoveInlineComment_EscapedDoubleQuoteFollowedByHash()
    {
        // This is the critical test case that demonstrates the bug fix.
        // Before fix: the escaped quote would toggle the quote state,
        // causing the # to be treated as a comment start, truncating the string.
        // After fix: the escaped quote is recognized, and the # is preserved.
        var input = "export FOO=\"text with \\\" and #hash\"";
        var result = WorkspaceBashEnvironment.RemoveInlineComment(input);

        // The escaped quote and the # should both be preserved within the double-quoted string.
        // Without the fix, the method incorrectly treats \" as ending the quote,
        // then sees # and thinks it's a comment, truncating to: "text with \"
        var expected = "export FOO=\"text with \\\" and #hash\"";

        // Output diagnostic info on failure
        if (result != expected)
        {
            Console.WriteLine($"Input:    '{input}'");
            Console.WriteLine($"Expected: '{expected}'");
            Console.WriteLine($"Actual:   '{result}'");
            Console.WriteLine($"Expected length: {expected.Length}");
            Console.WriteLine($"Actual length:   {result.Length}");
        }

        Assert.Equal(expected, result);
    }

    [Fact]
    public void RemoveInlineComment_EscapedSingleQuoteInDoubleQuotes()
    {
        // Backslashes in double quotes should be preserved when escaping quotes
        var input = "export FOO=\"path/with/\\'single\" # comment";
        var result = WorkspaceBashEnvironment.RemoveInlineComment(input);
        Assert.Equal("export FOO=\"path/with/\\'single\" ", result);
    }

    [Fact]
    public void RemoveInlineComment_MultipleEscapedQuotes()
    {
        var input = "export FOO=\"first\\\"second\\\"third #hash\" # comment";
        var result = WorkspaceBashEnvironment.RemoveInlineComment(input);
        Assert.Equal("export FOO=\"first\\\"second\\\"third #hash\" ", result);
    }

    [Fact]
    public void RemoveInlineComment_EscapedBackslash()
    {
        // Two backslashes should represent an escaped backslash
        var input = "export FOO=\"path\\\\with\\\\backslashes\" # comment";
        var result = WorkspaceBashEnvironment.RemoveInlineComment(input);
        Assert.Equal("export FOO=\"path\\\\with\\\\backslashes\" ", result);
    }

    [Fact]
    public void RemoveInlineComment_ComplexEscaping()
    {
        // Combination of escaped backslash followed by quote
        var input = "export FOO=\"path\\\\\\\"complex\" # comment";
        var result = WorkspaceBashEnvironment.RemoveInlineComment(input);
        Assert.Equal("export FOO=\"path\\\\\\\"complex\" ", result);
    }

    [Fact]
    public void RemoveInlineComment_HashAtStart()
    {
        var input = "# full line comment";
        var result = WorkspaceBashEnvironment.RemoveInlineComment(input);
        Assert.Equal("", result);
    }

    [Fact]
    public void RemoveInlineComment_MixedQuotes()
    {
        var input = "export FOO=\"outer 'inner #hash' outer\" # comment";
        var result = WorkspaceBashEnvironment.RemoveInlineComment(input);
        Assert.Equal("export FOO=\"outer 'inner #hash' outer\" ", result);
    }
}
