namespace StoatTote;

using System.Text.Json;

/// <summary>
/// CLI operating modes.
/// </summary>
public enum CliMode
{
    Code,  // Code generation mode - analyzes files and generates code
    Chat   // Chat mode - conversational assistance without file operations
}

/// <summary>
/// Supported LLM server backends.
/// </summary>
public enum ServerType
{
    Ollama,
    LlamaCpp,
    OpenRouter
}

/// <summary>
/// Preset configuration for a server backend.
/// </summary>
public record ServerPreset(string Name, string Url, ServerType Type);

/// <summary>
/// Persisted user configuration.
/// </summary>
public class SavedConfig
{
    public string Server { get; set; } = "ollama";
    public string Model { get; set; } = "qwen3:8b";
    public string Personality { get; set; } = "default";
    public CliMode Mode { get; set; } = CliMode.Code;
    public string? OpenRouterApiKey { get; set; }
    public List<string> PreferredModels { get; set; } = [];
}

internal static class Config
{
    private static readonly string ConfigDir = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "stoat")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "stoat");

    public static readonly string ConfigFilePath = Path.Combine(ConfigDir, "config.json");

    public static readonly Dictionary<string, ServerPreset> Servers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ollama"] = new ServerPreset("ollama",
            Environment.GetEnvironmentVariable("OLLAMA_URL") ?? "http://localhost:11434",
            ServerType.Ollama),
        ["llamacpp"] = new ServerPreset("llamacpp",
            Environment.GetEnvironmentVariable("LLAMACPP_URL") ?? "http://localhost:8080",
            ServerType.LlamaCpp),
        ["openrouter"] = new ServerPreset("openrouter",
            Environment.GetEnvironmentVariable("OPENROUTER_URL") ?? "https://openrouter.ai/api/v1",
            ServerType.OpenRouter),
    };

    public static string CurrentServerName { get; set; } = "ollama";

    public static ServerPreset CurrentServer =>
        Servers.GetValueOrDefault(CurrentServerName) ?? Servers["ollama"];

    public static string Model { get; set; } = "qwen3:8b";

    public static string DefaultPersonality { get; set; } = "default";

    public static StoatPersonality CurrentPersonality { get; set; } = StoatPersonalityManager.GetPersonality("default");

    public static CliMode CurrentMode { get; set; } = CliMode.Code;

    public static string? OpenRouterApiKey { get; set; }

    public static List<string> PreferredModels { get; set; } = [];

    public const string BackupDir = ".stoat-backups";

    public static readonly HashSet<string> Ignore = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", BackupDir, "dist", "build",
        ".next", "__pycache__", ".venv", "venv", ".env", ".stoat"
    };

    // File content tags
    public const string FileTag = "[FILENAME]";
    public const string FileStartTag = "[START FILE CONTENTS]";
    public const string FileEndTag = "[END FILE CONTENTS]";

    // File list tags
    public const string FileListStart = "[File List Start]";
    public const string FileListEnd = "[File List End]";

    public const string StoatArt = """

        
                     ╱|、           
                    (˚ˎ 。7       
                     |、˜〵        
                     じしˍ,)ノ     
                             

        ==============================
             S T O A T  &  T O T E   
        ==============================

        """;

    public const string StoatReturnArt = """

        
               ╱|、            ___
              (˚ˎ 。7        /     \
               |、˜〵        |======|
               じしˍ,)ノ     |______|
                             

        ==============================
             S T O A T  &  T O T E   
        ==============================

        """;

    public static void Load()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
                return;

            var json = File.ReadAllText(ConfigFilePath);
            var saved = JsonSerializer.Deserialize<SavedConfig>(json);
            if (saved == null)
                return;

            if (!string.IsNullOrEmpty(saved.Server))
                CurrentServerName = saved.Server;
            if (!string.IsNullOrEmpty(saved.Model))
                Model = saved.Model;
            if (!string.IsNullOrEmpty(saved.Personality))
                DefaultPersonality = saved.Personality;
            CurrentMode = saved.Mode;
            CurrentPersonality = StoatPersonalityManager.GetPersonality(DefaultPersonality);
            OpenRouterApiKey = saved.OpenRouterApiKey;
            if (saved.PreferredModels != null)
                PreferredModels = saved.PreferredModels;
        }
        catch
        {
            // ignore corrupted config
        }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var saved = new SavedConfig
            {
                Server = CurrentServerName,
                Model = Model,
                Personality = DefaultPersonality,
                Mode = CurrentMode,
                OpenRouterApiKey = OpenRouterApiKey,
                PreferredModels = PreferredModels
            };
            var json = JsonSerializer.Serialize(saved, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFilePath, json);
        }
        catch
        {
            // ignore save failures
        }
    }
}
