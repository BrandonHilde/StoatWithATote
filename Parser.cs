namespace StoatTote;

internal static class Parser
{
    /// <summary>Parses file blocks from LLM response using Config tags.</summary>
    public static List<FileBlock> ParseFileBlocks(string response)
    {
        return FileBlock.ParseFiles(response);
    }

    /// <summary>Parses a file list from between FileListStart/FileListEnd tags.</summary>
    public static List<string> ParseFileList(string response)
    {
        var start = response.IndexOf(Config.FileListStart);
        if (start < 0) return new List<string>();

        var end = response.IndexOf(Config.FileListEnd, start + Config.FileListStart.Length);
        if (end < 0) return new List<string>();

        var content = response.Substring(
            start + Config.FileListStart.Length,
            end - start - Config.FileListStart.Length);

        return content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .ToList();
    }
}
