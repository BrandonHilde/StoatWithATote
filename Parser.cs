using System.Text.RegularExpressions;

namespace StoatTote;

internal static class Parser
{
    private static readonly Regex ThinkBlock = new(@"<(think|thinking)>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex ThinkClose = new(@"</think(?:ing)?>", RegexOptions.IgnoreCase);
    private static readonly Regex FileListRegion = new(@"\[\s*file\s*list\s*start\s*\](.*?)(?:\[\s*file\s*list\s*end\s*\]|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex DotSlashPrefix = new(@"(^|[^\w./-])\./");
    private static readonly Regex ListMarker = new(@"^(?:#+\s*|[-*+•]\s+|\d+[.)]\s+)");
    private static readonly Regex PathLabel = new(@"^(?:file\s*name|file|path)\s*[:=]\s*", RegexOptions.IgnoreCase);
    private static readonly Regex FileNameWithExt = new(@"^(?:[\w.-]*\w\.[A-Za-z0-9]{1,10}|\.[\w.-]+)$");
    private static readonly Regex WebRequestLine = new(@"^[\s>*+•-]*(?:\d+[.)]\s*)?[`*_]*\s*(web\s*search|search|fetch)\s*[`*_]*\s*:\s*(.+?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex UrlPattern = new(@"https?://[^\s<>""'`\])]+", RegexOptions.IgnoreCase);
    private static readonly Regex BareDomain = new(@"^(?:[a-z0-9-]+\.)+[a-z]{2,}(?:/\S*)?$", RegexOptions.IgnoreCase);

    /// <summary>Parses file blocks (tagged or markdown) from an LLM response.</summary>
    public static List<FileBlock> ParseFileBlocks(string response, IReadOnlyList<string> requestedFiles, IReadOnlyList<string> projectTree)
    {
        return FileBlock.ParseFiles(StripThinking(response), requestedFiles, projectTree);
    }

    /// <summary>
    /// Finds the files the LLM asked for. Small models rarely follow a strict list format,
    /// so any project-tree path mentioned in the reply counts, whatever markup surrounds it.
    /// </summary>
    public static List<string> ParseFileList(string response, IReadOnlyList<string> projectTree)
    {
        var found = FindFileList(StripThinking(response), projectTree);

        // The model may have only listed files while thinking
        return found.Count > 0 ? found : FindFileList(response, projectTree);
    }

    /// <summary>
    /// Finds the web requests in a reply: "SEARCH: terms" and "FETCH: url" lines, tolerating
    /// bullets, bold, backticks and code fences around them.
    /// </summary>
    public static List<WebRequest> ParseWebRequests(string response)
    {
        var requests = new List<WebRequest>();
        foreach (Match m in WebRequestLine.Matches(StripThinking(response).Replace("\r\n", "\n")))
        {
            var value = m.Groups[2].Value.Trim().Trim('`', '*', '"', '\'', '<', '>', '[', ']').Trim();
            if (value.Length == 0) continue;

            if (m.Groups[1].Value.Contains("search", StringComparison.OrdinalIgnoreCase))
            {
                requests.Add(new WebRequest(WebRequestKind.Search, value));
                continue;
            }

            var url = UrlPattern.Match(value) is { Success: true } u
                ? u.Value.TrimEnd('.', ',', ';', ':')
                : BareDomain.IsMatch(value) ? "https://" + value : null;
            if (url != null)
                requests.Add(new WebRequest(WebRequestKind.Fetch, url));
        }

        return requests.Distinct().ToList();
    }

    /// <summary>Removes reasoning sections so drafts inside them aren't parsed as answers.</summary>
    public static string StripThinking(string response)
    {
        var text = ThinkBlock.Replace(response, "");

        // Some chat templates open the think tag in the prompt, so only the close tag appears
        var close = ThinkClose.Match(text);
        return close.Success ? text.Substring(close.Index + close.Length) : text;
    }

    /// <summary>Strips list markers, headings, labels, quotes and markdown from a path-like string.</summary>
    public static string CleanPathText(string text)
    {
        var s = text.Trim().Replace('\\', '/');
        string previous;
        do
        {
            previous = s;
            s = ListMarker.Replace(s, "");
            s = PathLabel.Replace(s, "");
            s = s.Trim(' ', '\t', '`', '*', '"', '\'', '<', '>', ':', ',', ';').TrimEnd('.');
        } while (s != previous);

        while (s.StartsWith("./"))
            s = s.Substring(2);
        return s;
    }

    /// <summary>
    /// True if the string is a plausible relative file path inside the project.
    /// Absolute paths and ".." segments are rejected so replies can't write outside the project.
    /// </summary>
    public static bool LooksLikePath(string? s, ICollection<string>? knownFiles = null)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length > 260)
            return false;
        if (knownFiles != null && knownFiles.Contains(s))
            return true;
        if (s.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || "<>:\"|?*\\".Contains(c)))
            return false;
        if (s.StartsWith('/') || s.Contains("//") || s.Split('/').Any(seg => seg == ".."))
            return false;

        return FileNameWithExt.IsMatch(s.Split('/').Last());
    }

    private static List<string> FindFileList(string text, IReadOnlyList<string> projectTree)
    {
        // "SEARCH: Program.cs async main" is a web request, not a file request
        text = WebRequestLine.Replace(text.Replace("\r\n", "\n"), "");
        text = DotSlashPrefix.Replace(text.Replace('\\', '/'), "$1");

        var region = FileListRegion.Match(text);
        if (region.Success)
            text = region.Groups[1].Value;

        var files = projectTree.Where(p => !p.EndsWith('/')).ToList();
        var hits = new List<(int pos, string path)>();

        foreach (var path in files)
        {
            var pos = FindPath(text, path);
            if (pos >= 0)
                hits.Add((pos, path));
        }

        // Bare filenames like "Config.cs" when the file lives in a subfolder
        foreach (var group in files.GroupBy(p => p.Split('/').Last(), StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() != 1) continue;

            var path = group.First();
            if (path.Equals(group.Key, StringComparison.OrdinalIgnoreCase) || hits.Any(h => h.path == path)) continue;

            var pos = FindPath(text, group.Key);
            if (pos >= 0)
                hits.Add((pos, path));
        }

        // Inside explicit list tags, keep unknown paths too so the user sees them as "not found"
        if (region.Success)
        {
            foreach (var line in text.Split('\n'))
            {
                var candidate = CleanPathText(line);
                if (!LooksLikePath(candidate)) continue;
                if (hits.Any(h => h.path.Equals(candidate, StringComparison.OrdinalIgnoreCase)
                    || h.path.EndsWith("/" + candidate, StringComparison.OrdinalIgnoreCase))) continue;

                hits.Add((int.MaxValue, candidate));
            }
        }

        return hits
            .OrderBy(h => h.pos)
            .Select(h => h.path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Finds a path in text where it isn't part of a longer path, or -1.</summary>
    private static int FindPath(string text, string path)
    {
        int idx = text.IndexOf(path, StringComparison.OrdinalIgnoreCase);
        while (idx >= 0)
        {
            int end = idx + path.Length;
            bool startOk = idx == 0 || !IsPathChar(text[idx - 1]);
            bool endOk = end == text.Length || !IsPathChar(text[end])
                || (text[end] == '.' && (end + 1 == text.Length || !IsPathChar(text[end + 1])));

            if (startOk && endOk)
                return idx;
            idx = text.IndexOf(path, idx + 1, StringComparison.OrdinalIgnoreCase);
        }
        return -1;
    }

    private static bool IsPathChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or '/';
}
