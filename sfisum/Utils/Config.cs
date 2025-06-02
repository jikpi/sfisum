using System.Text.Json;

namespace sfisum.Utils;

internal class Config
{
    // public int HashThreads { get; init; }
    // public bool OrderDirectoryWalk { get; init; }
    public string? DirectoryWalkPattern { get; init; }
    public bool PrintToLog { get; init; }
    public bool SortDuplicatesBySize { get; init; }
    public bool AddPathPrefixToDigestFilename { get; init; }
    public bool FindMatchesInRefresh { get; init; }
    public bool SkipRefreshMatchesForSmallFiles { get; init; }

    private const string ConfigPath = "sfisum.config.json";

    private static Config Default => new()
    {
        // HashThreads = 1,
        // OrderDirectoryWalk = false,
        DirectoryWalkPattern = "*",
        PrintToLog = true,
        SortDuplicatesBySize = true,
        AddPathPrefixToDigestFilename = true,
        FindMatchesInRefresh = true,
        SkipRefreshMatchesForSmallFiles = true
    };

    public static Config Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string existingJson = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<Config>(existingJson) ?? Default;
            }

            var config = Default;
            string newJson = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(ConfigPath, newJson);
            return config;
        }
        catch (Exception)
        {
            return Default;
        }
    }
}