namespace Cascode.Render.OrTools;

internal static class OrToolsSolverDefaults
{
    internal static string BuildSolverParameters(double maxTimeSeconds)
    {
        var seed = 1;
        var seedText = Environment.GetEnvironmentVariable("CASCODE_SEED");
        if (!string.IsNullOrWhiteSpace(seedText) && int.TryParse(seedText, out var parsed))
        {
            seed = parsed;
        }

        return $"max_time_in_seconds:{maxTimeSeconds} random_seed:{seed} num_search_workers:1";
    }
}
