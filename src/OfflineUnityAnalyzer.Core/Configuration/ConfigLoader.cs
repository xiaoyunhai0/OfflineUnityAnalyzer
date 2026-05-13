using System.Text.Json;

namespace OfflineUnityAnalyzer.Core.Configuration;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    public static async Task<AnalyzerConfig> LoadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<AnalyzerConfig>(stream, Options, cancellationToken);
        return config ?? AnalyzerConfig.Empty;
    }

    public static string ToJson(AnalyzerConfig config)
    {
        return JsonSerializer.Serialize(config, Options);
    }
}
