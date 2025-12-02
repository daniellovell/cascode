using Spectre.Console;
using System;

namespace Cascode.Cli;

internal static class ShellPrompt
{
    internal static string? ReadCommand(
        ShellState state,
        Func<int> getDetailScrollStep,
        Func<int, bool> tryAdjustDetailOffset,
        Action render)
    {
        var console = AnsiConsole.Console;

        try
        {
            var buffer = new PromptBuffer();
            state.ResetHistoryCursor();
            WritePrompt(buffer);

            while (true)
            {
                var keyInfo = console.Input.ReadKey(intercept: true);
                if (keyInfo is null)
                {
                    continue;
                }

                var key = keyInfo.Value;

                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.C)
                {
                    return null;
                }

                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.LeftArrow)
                {
                    buffer.MoveWordLeft();
                    WritePrompt(buffer);
                    continue;
                }

                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.RightArrow)
                {
                    buffer.MoveWordRight();
                    WritePrompt(buffer);
                    continue;
                }

                if ((key.Modifiers & ConsoleModifiers.Control) == 0 && key.Key == ConsoleKey.Home)
                {
                    buffer.MoveHome();
                    WritePrompt(buffer);
                    continue;
                }

                if ((key.Modifiers & ConsoleModifiers.Control) == 0 && key.Key == ConsoleKey.End)
                {
                    buffer.MoveEnd();
                    WritePrompt(buffer);
                    continue;
                }

                if (key.Key == ConsoleKey.LeftArrow)
                {
                    buffer.MoveLeft();
                    WritePrompt(buffer);
                    continue;
                }

                if (key.Key == ConsoleKey.RightArrow)
                {
                    buffer.MoveRight();
                    WritePrompt(buffer);
                    continue;
                }

                if ((key.Modifiers & ConsoleModifiers.Shift) != 0 && key.Key == ConsoleKey.UpArrow)
                {
                    var detailStep = getDetailScrollStep();
                    if (tryAdjustDetailOffset(-detailStep))
                    {
                        render();
                        WritePrompt(buffer);
                        continue;
                    }

                    if (state.DeviceSummary?.HasDetailRows == true)
                    {
                        continue;
                    }

                    var step = Math.Max(1, state.LogViewport / 4);
                    state.ScrollLogUp(step);
                    render();
                    WritePrompt(buffer);
                    continue;
                }

                if ((key.Modifiers & ConsoleModifiers.Shift) != 0 && key.Key == ConsoleKey.DownArrow)
                {
                    var detailStep = getDetailScrollStep();
                    if (tryAdjustDetailOffset(detailStep))
                    {
                        render();
                        WritePrompt(buffer);
                        continue;
                    }

                    if (state.DeviceSummary?.HasDetailRows == true)
                    {
                        continue;
                    }

                    var step = Math.Max(1, state.LogViewport / 4);
                    state.ScrollLogDown(step);
                    render();
                    WritePrompt(buffer);
                    continue;
                }

                if (key.Key == ConsoleKey.UpArrow && key.Modifiers == 0)
                {
                    if (state.TryHistoryPrevious(out var command))
                    {
                        buffer.Replace(command);
                        WritePrompt(buffer);
                    }
                    continue;
                }

                if (key.Key == ConsoleKey.DownArrow && key.Modifiers == 0)
                {
                    if (state.TryHistoryNext(out var command))
                    {
                        buffer.Replace(command);
                        WritePrompt(buffer);
                    }
                    continue;
                }

                // Alternate bindings for terminals that do not send Shift+Arrow modifiers
                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.UpArrow)
                {
                    var step = getDetailScrollStep();
                    if (tryAdjustDetailOffset(-step))
                    {
                        render();
                        WritePrompt(buffer);
                    }
                    continue;
                }

                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.DownArrow)
                {
                    var step = getDetailScrollStep();
                    if (tryAdjustDetailOffset(step))
                    {
                        render();
                        WritePrompt(buffer);
                    }
                    continue;
                }

                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.Home)
                {
                    state.ScrollLogHome();
                    render();
                    WritePrompt(buffer);
                    continue;
                }

                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.End)
                {
                    state.ScrollLogEnd();
                    render();
                    WritePrompt(buffer);
                    continue;
                }

                if (key.Key == ConsoleKey.Enter)
                {
                    console.WriteLine();
                    return buffer.Text;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    buffer.Backspace();
                    WritePrompt(buffer);
                    continue;
                }

                if (key.Key == ConsoleKey.Escape)
                {
                    buffer.Clear();
                    state.ResetHistoryCursor();
                    render();
                    WritePrompt(buffer);
                    continue;
                }

                var ch = key.KeyChar;
                if (!char.IsControl(ch))
                {
                    buffer.Insert(ch);
                    WritePrompt(buffer);
                }
            }
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void WritePrompt(PromptBuffer buffer)
    {
        ClearPromptLine();
        AnsiConsole.Markup("[green]cascode[/]> ");
        var text = buffer.Text;
        if (!string.IsNullOrEmpty(text))
        {
            AnsiConsole.Console.Write(text);
        }

        var tail = buffer.TailLength;
        if (tail > 0)
        {
            AnsiConsole.Console.Write($"\u001b[{tail}D");
        }
    }

    private static void ClearPromptLine()
    {
        const string ClearSequence = "\u001b[2K\r";
        try
        {
            AnsiConsole.Console.Write(ClearSequence);
        }
        catch
        {
            try
            {
                System.Console.Write('\r');
                var width = Math.Max(0, System.Console.BufferWidth - 1);
                if (width > 0)
                {
                    System.Console.Write(new string(' ', width));
                }
                System.Console.Write('\r');
            }
            catch
            {
                // ignored
            }
        }
    }
}
