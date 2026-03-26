using System.Text;
using System.Text.Json;

namespace StoatTote;

internal static class ConversationLogger
{
    private static readonly string LogDir = ".stoat";
    private static string? _currentSession;

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

    /// <summary>Logs Phase 1 - file list request.</summary>
    public static void LogPhase1(string prompt, string response, List<string> fileList)
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
            FileList = fileList
        });

        WriteLog(log);
    }

    /// <summary>Logs Phase 2 - code generation.</summary>
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
            FilesGenerated = files.Select(f => new FileBlockLog
            {
                Path = f.FilePath,
                ContentLength = f.Content.Length
            }).ToList()
        });

        WriteLog(log);
    }

    /// <summary>Logs a chat mode turn.</summary>
    public static void LogChatTurn(string userMessage, string assistantResponse)
    {
        if (_currentSession == null) return;

        var log = ReadLog();
        if (log == null) return;

        log.Turns.Add(new ConversationTurn
        {
            Phase = 0,
            Timestamp = DateTime.Now,
            Prompt = userMessage,
            Response = assistantResponse
        });

        WriteLog(log);
    }

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
    public List<string>? FileList { get; set; }
    public List<FileBlockLog>? FilesGenerated { get; set; }
}

internal class FileBlockLog
{
    public string Path { get; set; } = "";
    public int ContentLength { get; set; }
}
