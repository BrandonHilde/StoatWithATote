using System.Text.Json;
using System.Text.RegularExpressions;

namespace StoatTote;

internal record FileBlock(string Path, string Content);

internal static class Parser
{
    /// <summary>Parses file list from LLM response.</summary>
    public static List<string> ParseFileList(string response)
    {
        var files = new List<string>();

        // Try ```FILES ... ``` or <FILES>...</FILES> blocks
        var blockMatch = Regex.Match(response, @"```FILES\s*\n([\s\S]*?)```|<FILES>\s*\n([\s\S]*?)<\/FILES>", RegexOptions.IgnoreCase);
        var content = blockMatch.Success
            ? (blockMatch.Groups[1].Value.Length > 0 ? blockMatch.Groups[1].Value : blockMatch.Groups[2].Value)
            : response;

        foreach (var line in content.Split('\n'))
        {
            var trimmed = Regex.Replace(line, @"^[\s\-\*\d.]+", "").Trim();

            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#') || trimmed.Length > 200 || trimmed.Contains("  "))
                continue;

            // Path if it has dot, slash, or backslash
            if (trimmed.Contains('.') || trimmed.Contains('/') || trimmed.Contains('\\'))
            {
                var clean = trimmed.Trim('`', '\'', '"').Trim();
                if (!string.IsNullOrEmpty(clean))
                    files.Add(clean);
            }
        }

        return files.Distinct().ToList();
    }

    /// <summary>Parses JSON tool requests from response.</summary>
    public static List<ToolRequest> ParseToolRequests(string response)
    {
        var requests = new List<ToolRequest>();

        // Find ```JSON ... ``` blocks
        foreach (Match match in Regex.Matches(response, @"```JSON\s*\n([\s\S]*?)```", RegexOptions.IgnoreCase))
        {
            try
            {
                var req = JsonSerializer.Deserialize<ToolRequest>(match.Groups[1].Value.Trim());
                if (req?.Tool != null)
                    requests.Add(req);
            }
            catch { /* skip bad JSON */ }
        }

        // Fallback: find JSON objects with "tool" property
        foreach (Match match in Regex.Matches(response, @"\{\s*[""]tool[""]\s*:\s*[""]([^""]+)"""))
        {
            var start = match.Index;
            var braceCount = 0;
            var end = start;

            for (int i = start; i < response.Length; i++)
            {
                if (response[i] == '{') braceCount++;
                if (response[i] == '}') braceCount--;
                if (braceCount == 0) { end = i + 1; break; }
            }

            try
            {
                var json = response.Substring(start, end - start);
                var req = JsonSerializer.Deserialize<ToolRequest>(json);
                if (req?.Tool != null)
                    requests.Add(req);
            }
            catch { /* skip bad JSON */ }
        }

        return requests;
    }

    /// <summary>Parses file blocks from Phase 2 response.</summary>
    public static List<FileBlock> ParseFileBlocks(string response)
    {
        var files = new List<FileBlock>();

        // Primary: [Filename: path] ... [End Filename]
        var primary = new Regex(@"\[Filename:\s*(.+?)\]\s*\n```[^\n]*\n([\s\S]*?)```\s*\n?\[End Filename\]", RegexOptions.IgnoreCase);
        foreach (Match m in primary.Matches(response))
            files.Add(new FileBlock(m.Groups[1].Value.Trim(), m.Groups[2].Value));

        if (files.Count > 0) return files;

        // Fallback: [Filename: path] ... ``` without [End Filename]
        // This handles responses where LLM doesn't include the closing marker
        var noEndMarker = new Regex(@"\[Filename:\s*(.+?)\]\s*\n```[^\n]*\n([\s\S]*?)```(?=\s*(?:\[(?:End\s*Filename|Filename|---)|```|\z))", RegexOptions.IgnoreCase);
        foreach (Match m in noEndMarker.Matches(response))
            files.Add(new FileBlock(m.Groups[1].Value.Trim(), m.Groups[2].Value));

        if (files.Count > 0) return files;

        // Fallback: --- FILE: path --- / --- END FILE ---
        var block = new Regex(@"---\s*FILE:\s*(.+?)\s*---\s*\n([\s\S]*?)---\s*END\s*FILE\s*---", RegexOptions.IgnoreCase);
        foreach (Match m in block.Matches(response))
            files.Add(new FileBlock(m.Groups[1].Value.Trim(), m.Groups[2].Value));

        if (files.Count > 0) return files;

        // Fallback: ```language:filepath ...
        var code = new Regex(@"```[\w]*:?\s*([^\n`]+)\n([\s\S]*?)```");
        foreach (Match m in code.Matches(response))
        {
            var path = m.Groups[1].Value.Trim();
            if (path.Contains('.') || path.Contains('/') || path.Contains('\\'))
                files.Add(new FileBlock(path, m.Groups[2].Value));
        }

        return files;
    }

    /// <summary>Extracts content from read_file result.</summary>
    public static string? ExtractReadFileContent(ToolResult result)
    {
        if (result.Data is JsonElement elem && elem.TryGetProperty("content", out var prop))
            return prop.GetString();

        // Try reflection for anonymous objects
        if (result.Data != null)
        {
            var content = result.Data.GetType().GetProperty("content")?.GetValue(result.Data);
            return content?.ToString();
        }

        return null;
    }

    /// <summary>Extracts path from read_file result.</summary>
    public static string? ExtractReadFilePath(ToolResult result)
    {
        if (result.Data is JsonElement elem && elem.TryGetProperty("path", out var prop))
            return prop.GetString();

        if (result.Data != null)
        {
            var path = result.Data.GetType().GetProperty("path")?.GetValue(result.Data);
            return path?.ToString();
        }

        return null;
    }
}
