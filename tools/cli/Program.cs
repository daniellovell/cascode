using Spectre.Console;

namespace Cascode.Cli;

internal static class Program
{
    private const string AppName = "cascode";

    internal static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            var (workspace, remaining) = ParseWorkspaceOption(args);
            var shell = new CascodeShell(workspace);

            if (remaining.Length > 0)
            {
                return shell.RunOnce(remaining);
            }

            return shell.RunInteractive();
        }
        catch (Exception ex)
        {
            WriteFatalError(ex);
            return 1;
        }
    }

    private static (string Workspace, string[] Remaining) ParseWorkspaceOption(string[] args)
    {
        var workspace = Directory.GetCurrentDirectory();
        var remaining = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException("--workspace requires a path argument");
                }

                workspace = Path.GetFullPath(args[++i]);
                continue;
            }

            remaining.Add(args[i]);
        }

        return (workspace, remaining.ToArray());
    }

    private static void WriteFatalError(Exception ex)
    {
        var previousColor = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"{AppName}: fatal error: {ex.Message}");
        Console.ForegroundColor = previousColor;

        if (Environment.GetEnvironmentVariable("CASCODE_DEBUG")?.Equals("1", StringComparison.Ordinal) == true)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(ex);
        }
    }
}
