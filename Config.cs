namespace StoatTote;

internal static class Config
{
    public static readonly string OllamaUrl =
        Environment.GetEnvironmentVariable("OLLAMA_URL") ?? "http://localhost:11434";

    public static string Model { get; set; } =
        Environment.GetEnvironmentVariable("STOAT_MODEL") ?? "qwen3:8b";

    public const string BackupDir = ".stoat-backups";

    public static readonly HashSet<string> Ignore = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", BackupDir, "dist", "build",
        ".next", "__pycache__", ".venv", "venv", ".env", ".stoat"
    };

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

    /// <summary>
    /// Phase 1 system prompt - LLM decides which files it needs and what operations to perform.
    /// The LLM can request files AND/OR request file operations through JSON tool requests.
    /// </summary>
    public const string Phase1System =
        "You are Stoat, a code assistant that helps with file operations. " +
        "The user will describe a task. You can request files AND/OR request file operations.\n\n" +
        "Available tools (reply with JSON inside ```JSON ... ``` block):\n\n" +
        "1. files_needed - List files you want to see:\n" +
        "   {\"tool\": \"files_needed\", \"files\": [\"path/to/file1\", \"path/to/file2\"]}\n\n" +
        "2. create_file - Create a new file:\n" +
        "   {\"tool\": \"create_file\", \"path\": \"path/to/file\", \"content\": \"file content here\"}\n\n" +
        "3. rename_file - Rename an existing file:\n" +
        "   {\"tool\": \"rename_file\", \"old_path\": \"old/name\", \"new_path\": \"new/name\"}\n\n" +
        "4. move_file - Move a file to a new location:\n" +
        "   {\"tool\": \"move_file\", \"source\": \"source/path\", \"destination\": \"dest/path\"}\n\n" +
        "5. delete_file - Delete a file:\n" +
        "   {\"tool\": \"delete_file\", \"path\": \"path/to/file\"}\n\n" +
        "6. search_files - Search for text in project files:\n" +
        "   {\"tool\": \"search_files\", \"query\": \"search text\", \"file_pattern\": \"*.cs\"}\n\n" +
        "7. list_files - Get project file tree:\n" +
        "   {\"tool\": \"list_files\", \"directory\": \".\"}\n\n" +
        "8. read_file - Read a specific file's contents:\n" +
        "   {\"tool\": \"read_file\", \"path\": \"path/to/file\"}\n\n" +
        "You can request multiple operations. Each must be in its own ```JSON block.\n" +
        "If you only need files for a task, just use files_needed.\n" +
        "The user will confirm each operation before it's executed.\n" +
        "Project files will be provided after your request.";

    public const string Phase2System =
        "You are a code assistant. Implement the requested changes. Return EVERY file you modify or create in full using this exact format for each file:\n\n" +
        "[Filename: path/to/file]\n```\n<full file content>\n```\n[End Filename]\n\n" +
        "Return complete files, not diffs. Only include files that need changes.";
}
