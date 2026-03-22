using System.Text;
using System.Text.Json;

namespace StoatTote;

internal static class ConversationLogger
{
    private static readonly string LogDir = ".stoat";
    private static string? _currentSession;

    /// <summary>Starts a new conversation log session.</summary>
    public static string StartSession(string task)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var safeTask = SanitizeFilename(task);
        _currentSession = Path.Combine(LogDir, $"{timestamp}_{safeTask}.json");

        FileUtils.EnsureDir(LogDir);

        var log = new ConversationLog
        {
            StartTime = DateTime.Now,
            Task = task,
            Model = Config.Model,
            Turns = new List<ConversationTurn>()
        };

        WriteLog(log);
        return _currentSession;
    }

    /// <summary>Logs a Phase 1 turn (LLM request and tool results).</summary>
    public static void LogPhase1(string prompt, string response, List<ToolRequest> requests, List<ToolResult> results)
    {
        if (_currentSession == null) return;

        var log = ReadLog();
        if (log == null) return;

        log.Turns.Add(new ConversationTurn
        {
            Phase = 1,
            Timestamp = DateTime.Now,
            Prompt = prompt,
            Response = response,
            ToolRequests = requests,
            ToolResults = results.Select(r => new ToolResultLog
            {
                Success = r.Success,
                Message = r.Message,
                ToolType = r.ToolType
            }).ToList()
        });

        WriteLog(log);
    }

    /// <summary>Logs the Phase 2 turn (code generation).</summary>
    public static void LogPhase2(string prompt, string response, List<FileBlock> files)
    {
        if (_currentSession == null) return;

        var log = ReadLog();
        if (log == null) return;

        log.Turns.Add(new ConversationTurn
        {
            Phase = 2,
            Timestamp = DateTime.Now,
            Prompt = prompt,
            Response = response,
            FilesGenerated = files.Select(f => new FileBlockLog { Path = f.Path, ContentLength = f.Content.Length }).ToList()
        });

        WriteLog(log);
    }

    /// <summary>Logs any exception that occurred.</summary>
    public static void LogError(string message)
    {
        if (_currentSession == null) return;

        var log = ReadLog();
        if (log == null) return;

        log.Error = message;
        WriteLog(log);
    }

    private static ConversationLog? ReadLog()
    {
        if (_currentSession == null || !File.Exists(_currentSession))
            return null;

        try
        {
            var json = File.ReadAllText(_currentSession);
            return JsonSerializer.Deserialize<ConversationLog>(json);
        }
        catch { return null; }
    }

    private static void WriteLog(ConversationLog log)
    {
        if (_currentSession == null) return;

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(log, options);
        File.WriteAllText(_currentSession, json);
    }

    private static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var result = new StringBuilder();
        foreach (var c in name)
        {
            if (!invalid.Contains(c) && result.Length < 30)
                result.Append(char.IsLetterOrDigit(c) || c == ' ' ? c : '_');
        }
        return result.ToString().Replace(' ', '_').Trim('_');
    }
}

// Log data structures
internal class ConversationLog
{
    public DateTime StartTime { get; set; }
    public string? EndTime { get; set; }
    public string Task { get; set; } = "";
    public string Model { get; set; } = "";
    public string? Error { get; set; }
    public List<ConversationTurn> Turns { get; set; } = new();
}

internal class ConversationTurn
{
    public int Phase { get; set; }
    public DateTime Timestamp { get; set; }
    public string Prompt { get; set; } = "";
    public string Response { get; set; } = "";
    public List<ToolRequest>? ToolRequests { get; set; }
    public List<ToolResultLog>? ToolResults { get; set; }
    public List<FileBlockLog>? FilesGenerated { get; set; }
}

internal class ToolResultLog
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public string? ToolType { get; set; }
}

internal class FileBlockLog
{
    public string Path { get; set; } = "";
    public int ContentLength { get; set; }
}
