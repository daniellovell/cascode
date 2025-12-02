using System.Text;

namespace Cascode.Cli;

/// <summary>
/// Minimal line-edit buffer to support cursor-aware command editing without Spectre prompts.
/// </summary>
internal sealed class PromptBuffer
{
    private readonly StringBuilder _buffer = new();

    /// <summary>Current cursor position (0-based) within the buffer.</summary>
    public int Cursor { get; private set; }

    /// <summary>Current buffer text.</summary>
    public string Text => _buffer.ToString();

    /// <summary>Number of characters to the right of the cursor.</summary>
    public int TailLength => _buffer.Length - Cursor;

    public void Clear()
    {
        _buffer.Clear();
        Cursor = 0;
    }

    /// <summary>Replaces the buffer content and sets the cursor at the end.</summary>
    public void Replace(string text)
    {
        _buffer.Clear();
        _buffer.Append(text);
        Cursor = _buffer.Length;
    }

    /// <summary>Move cursor left by one character.</summary>
    public void MoveLeft()
    {
        if (Cursor > 0)
        {
            Cursor--;
        }
    }

    /// <summary>Move cursor right by one character.</summary>
    public void MoveRight()
    {
        if (Cursor < _buffer.Length)
        {
            Cursor++;
        }
    }

    /// <summary>Move cursor to the start of the buffer.</summary>
    public void MoveHome()
    {
        Cursor = 0;
    }

    /// <summary>Move cursor to the end of the buffer.</summary>
    public void MoveEnd()
    {
        Cursor = _buffer.Length;
    }

    /// <summary>Move cursor one word to the left.</summary>
    public void MoveWordLeft()
    {
        if (Cursor == 0) { return; }

        var i = Cursor - 1;
        while (i >= 0 && char.IsWhiteSpace(_buffer[i])) { i--; }
        while (i >= 0 && !char.IsWhiteSpace(_buffer[i])) { i--; }
        Cursor = i + 1;
    }

    /// <summary>Move cursor one word to the right.</summary>
    public void MoveWordRight()
    {
        if (Cursor >= _buffer.Length) { return; }

        var i = Cursor;
        while (i < _buffer.Length && char.IsWhiteSpace(_buffer[i])) { i++; }
        while (i < _buffer.Length && !char.IsWhiteSpace(_buffer[i])) { i++; }
        while (i < _buffer.Length && char.IsWhiteSpace(_buffer[i])) { i++; }
        Cursor = i;
    }

    /// <summary>Delete the character to the left of the cursor.</summary>
    public void Backspace()
    {
        if (Cursor == 0)
        {
            return;
        }

        _buffer.Remove(Cursor - 1, 1);
        Cursor--;
    }

    /// <summary>Delete the character at the cursor position (to the right of the cursor).</summary>
    public void DeleteUnderCursor()
    {
        if (Cursor >= _buffer.Length)
        {
            return;
        }

        _buffer.Remove(Cursor, 1);
    }

    /// <summary>Insert a character at the cursor position.</summary>
    public void Insert(char ch)
    {
        _buffer.Insert(Cursor, ch);
        Cursor++;
    }
}
