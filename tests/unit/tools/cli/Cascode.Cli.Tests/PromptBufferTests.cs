using Cascode.Cli;
using Xunit;

namespace Cascode.Cli.Tests;

public sealed class PromptBufferTests
{
    [Fact]
    public void InsertAndNavigation_TracksCursor()
    {
        var buffer = new PromptBuffer();

        buffer.Insert('a');
        buffer.Insert('b');
        buffer.Insert('c');

        Assert.Equal("abc", buffer.Text);
        Assert.Equal(3, buffer.Cursor);

        buffer.MoveLeft();
        buffer.MoveLeft();
        buffer.Insert('x');

        Assert.Equal("axbc", buffer.Text);
        Assert.Equal(2, buffer.Cursor);

        buffer.MoveRight();
        buffer.Backspace();

        Assert.Equal("axc", buffer.Text);
        Assert.Equal(2, buffer.Cursor);
    }

    [Fact]
    public void ReplaceAndClear_ResetState()
    {
        var buffer = new PromptBuffer();

        buffer.Replace("hello");
        Assert.Equal("hello", buffer.Text);
        Assert.Equal(5, buffer.Cursor);
        Assert.Equal(0, buffer.TailLength);

        buffer.MoveLeft();
        buffer.MoveLeft();
        Assert.Equal(3, buffer.Cursor);
        Assert.Equal(2, buffer.TailLength);

        buffer.MoveHome();
        Assert.Equal(0, buffer.Cursor);

        buffer.MoveEnd();
        Assert.Equal(5, buffer.Cursor);

        buffer.Clear();

        Assert.Equal(string.Empty, buffer.Text);
        Assert.Equal(0, buffer.Cursor);
        Assert.Equal(0, buffer.TailLength);
    }

    [Fact]
    public void WordNavigation_SkipsWhitespace()
    {
        var buffer = new PromptBuffer();
        buffer.Replace("one two  three");

        buffer.MoveWordLeft();
        Assert.Equal(9, buffer.Cursor);

        buffer.MoveWordLeft();
        Assert.Equal(4, buffer.Cursor);

        buffer.MoveWordLeft();
        Assert.Equal(0, buffer.Cursor);

        buffer.MoveWordRight();
        Assert.Equal(4, buffer.Cursor);

        buffer.MoveWordRight();
        Assert.Equal(9, buffer.Cursor);

        buffer.MoveWordRight();
        Assert.Equal(14, buffer.Cursor);
    }
}
