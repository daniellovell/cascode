using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cascode.Workspace;

public sealed class WorkspaceScanStorage
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public void Save(WorkspaceScanResult result, string outputPath)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        var dto = WorkspaceScanDto.FromResult(result);
        var json = JsonSerializer.Serialize(dto, SerializerOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, json);
    }

    public WorkspaceScanResult Load(string inputPath)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException("Scan file not found", inputPath);
        }

        var json = File.ReadAllText(inputPath);
        var dto = JsonSerializer.Deserialize<WorkspaceScanDto>(json, SerializerOptions);
        if (dto is null)
        {
            throw new InvalidOperationException("Failed to deserialize workspace scan data.");
        }

        return dto.ToResult();
    }

    private sealed record WorkspaceScanDto(
        string WorkspaceRoot,
        List<WorkspaceLibrary> Libraries,
        List<ModelDeckRecord> ModelDecks,
        List<SpectreModel>? Models,
        List<string> Warnings)
    {
        public static WorkspaceScanDto FromResult(WorkspaceScanResult result)
            => new(
                result.WorkspaceRoot,
                result.Libraries.ToList(),
                result.ModelDecks.ToList(),
                result.Models.ToList(),
                result.Warnings.ToList());

        public WorkspaceScanResult ToResult()
            => new(WorkspaceRoot, Libraries, ModelDecks, Models ?? new List<SpectreModel>(), Warnings);
    }
}
