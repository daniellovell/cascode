using Spectre.Console;
using System;
using System.Text;

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
            var buffer = new StringBuilder();
            state.ResetHistoryCursor();
            WritePrompt(buffer.ToString());

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

                if ((key.Modifiers & ConsoleModifiers.Shift) != 0 && key.Key == ConsoleKey.UpArrow)
                {
                    var detailStep = getDetailScrollStep();
                    if (tryAdjustDetailOffset(-detailStep))
                    {
                        render();
                        WritePrompt(buffer.ToString());
                        continue;
                    }

                    if (state.ModelSummary?.HasDetailRows == true)
                    {
                        continue;
                    }

                    var step = Math.Max(1, state.LogViewport / 4);
                    state.ScrollLogUp(step);
                    render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if ((key.Modifiers & ConsoleModifiers.Shift) != 0 && key.Key == ConsoleKey.DownArrow)
                {
                    var detailStep = getDetailScrollStep();
                    if (tryAdjustDetailOffset(detailStep))
                    {
                        render();
                        WritePrompt(buffer.ToString());
                        continue;
                    }

                    if (state.ModelSummary?.HasDetailRows == true)
                    {
                        continue;
                    }

                    var step = Math.Max(1, state.LogViewport / 4);
                    state.ScrollLogDown(step);
                    render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if (key.Key == ConsoleKey.UpArrow)
                {
                    if (state.TryHistoryPrevious(out var command))
                    {
                        buffer.Clear();
                        buffer.Append(command);
                        WritePrompt(buffer.ToString());
                    }
                    continue;
                }

                if (key.Key == ConsoleKey.DownArrow)
                {
                    if (state.TryHistoryNext(out var command))
                    {
                        buffer.Clear();
                        buffer.Append(command);
                        WritePrompt(buffer.ToString());
                    }
                    continue;
                }

                if (key.Key == ConsoleKey.PageUp)
                {
                    state.ScrollLogUp(state.LogViewport);
                    render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if (key.Key == ConsoleKey.PageDown)
                {
                    state.ScrollLogDown(state.LogViewport);
                    render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.Home)
                {
                    state.ScrollLogHome();
                    render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.End)
                {
                    state.ScrollLogEnd();
                    render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                if (key.Key == ConsoleKey.Enter)
                {
                    console.WriteLine();
                    return buffer.ToString();
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Length--;
                        WritePrompt(buffer.ToString());
                    }
                    continue;
                }

                if (key.Key == ConsoleKey.Escape)
                {
                    buffer.Clear();
                    state.ResetHistoryCursor();
                    render();
                    WritePrompt(buffer.ToString());
                    continue;
                }

                var ch = key.KeyChar;
                if (!char.IsControl(ch))
                {
                    buffer.Append(ch);
                    WritePrompt(buffer.ToString());
                }
            }
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void WritePrompt(string buffer)
    {
        ClearPromptLine();
        AnsiConsole.Markup("[green]cascode[/]> ");
        if (!string.IsNullOrEmpty(buffer))
        {
            AnsiConsole.Console.Write(buffer);
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
