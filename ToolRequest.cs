using System.Text.Json.Serialization;

namespace StoatTote;

/// <summary>
/// Represents a tool request from the LLM in JSON format.
/// </summary>
internal class ToolRequest
{
    [JsonPropertyName("tool")]
    public string Tool { get; set; } = string.Empty;

    // files_needed
    [JsonPropertyName("files")]
    public List<string>? Files { get; set; }

    // create_file
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    // rename_file
    [JsonPropertyName("old_path")]
    public string? OldPath { get; set; }

    [JsonPropertyName("new_path")]
    public string? NewPath { get; set; }

    // move_file
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("destination")]
    public string? Destination { get; set; }

    // search_files
    [JsonPropertyName("query")]
    public string? Query { get; set; }

    [JsonPropertyName("file_pattern")]
    public string? FilePattern { get; set; }

    // list_files
    [JsonPropertyName("directory")]
    public string? Directory { get; set; }
}

/// <summary>
/// Represents the result of a tool execution.
/// </summary>
internal record ToolResult(
    bool Success,
    string Message,
    string? ToolType = null,
    object? Data = null
);

/// <summary>
/// Represents a search result from the search_files tool.
/// </summary>
internal record SearchResult(
    string FilePath,
    int LineNumber,
    string LineContent
);
