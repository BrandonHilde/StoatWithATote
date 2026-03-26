namespace StoatTote;

/// <summary>
/// CLI operating modes.
/// </summary>
public enum CliMode
{
    Code,  // Code generation mode - analyzes files and generates code
    Chat   // Chat mode - conversational assistance without file operations
}

internal static class Config
{
    public static readonly string OllamaUrl =
        Environment.GetEnvironmentVariable("OLLAMA_URL") ?? "http://localhost:11434";

    public static string Model { get; set; } =
        Environment.GetEnvironmentVariable("STOAT_MODEL") ?? "qwen3:8b";

    public static string DefaultPersonality { get; set; } =
        Environment.GetEnvironmentVariable("STOAT_PERSONALITY") ?? "default";

    public static StoatPersonality CurrentPersonality { get; set; } = StoatPersonalityManager.GetPersonality(DefaultPersonality);

    public static CliMode CurrentMode { get; set; } = CliMode.Code;

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
}
