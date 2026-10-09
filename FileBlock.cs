using System.Text.RegularExpressions;

namespace StoatTote;

/// <summary>
/// Represents a parsed file block from the LLM response.
/// </summary>
public class FileBlock
{
    public string FilePath = "";
    public string Content = "";

    public FileBlock(string path, string content)
    {
        FilePath = path;
        Content = content;
    }

    // Tags are matched loosely: small models change case, add spaces or slashes, or shorten them
    private static readonly Regex NameTag = new(@"\[\s*/?\s*file\s*_?\s*name\s*\]", RegexOptions.IgnoreCase);
    private static readonly Regex StartTag = new(@"\[\s*start\s+(?:of\s+)?file(?:\s+contents?)?\s*\]", RegexOptions.IgnoreCase);
    private static readonly Regex EndTag = new(@"\[\s*/?\s*end\s+(?:of\s+)?file(?:\s+contents?)?\s*\]", RegexOptions.IgnoreCase);

    private static readonly Regex Fence = new(@"^\s*(`{3,}|~{3,})\s*(.*?)\s*$");
    private static readonly Regex BareFence = new(@"^\s*(`{3,}|~{3,})\s*$");
    private static readonly Regex InlineCode = new(@"`([^`]+)`");
    private static readonly Regex BoldText = new(@"\*\*([^*]+)\*\*");
    private static readonly Regex PathComment = new(@"^\s*(?://|#|--|;|/\*|<!--)\s*(.+?)\s*(?:\*/|-->)?\s*$");

    private static readonly HashSet<string> ShellLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "bash", "sh", "shell", "zsh", "console", "terminal", "powershell", "ps", "ps1", "pwsh", "cmd"
    };

    /// <summary>
    /// Parses files from an LLM response. Accepts the tag format, the markdown format
    /// (path on its own line followed by a code block), and common mixes of the two.
    /// Content is returned with \n line endings.
    /// </summary>
    public static List<FileBlock> ParseFiles(string text, IReadOnlyList<string> requestedFiles, IReadOnlyList<string> projectTree)
    {
        text = text.Replace("\r\n", "\n");
        var known = new HashSet<string>(
            projectTree.Where(p => !p.EndsWith('/')).Concat(requestedFiles),
            StringComparer.OrdinalIgnoreCase);

        var blocks = ParseTaggedFiles(text);
        if (blocks.Count == 0)
            blocks = ParseMarkdownFiles(text, requestedFiles, known);

        // Use the project's spelling for existing files, so "program.cs" doesn't create a second file on Linux
        foreach (var b in blocks)
            if (known.TryGetValue(b.FilePath, out var actual))
                b.FilePath = actual;

        // Drop invalid paths, and keep the last version when a file appears twice
        return blocks
            .Where(b => Parser.LooksLikePath(b.FilePath, known))
            .GroupBy(b => b.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();
    }

    private static List<FileBlock> ParseTaggedFiles(string text)
    {
        var blocks = new List<FileBlock>();
        int prevEnd = 0;

        foreach (Match start in StartTag.Matches(text))
        {
            // Tag text inside an earlier file's contents
            if (start.Index < prevEnd) continue;

            var path = PathFromHeader(text.Substring(prevEnd, start.Index - prevEnd));
            int bodyStart = start.Index + start.Length;
            int bodyEnd;

            var end = EndTag.Match(text, bodyStart);
            if (end.Success)
            {
                bodyEnd = end.Index;
                prevEnd = end.Index + end.Length;
            }
            else
            {
                // Missing end tag: stop at the next file's name tag, or the end of the reply
                var next = NameTag.Match(text, bodyStart);
                bodyEnd = next.Success ? next.Index : text.Length;
                prevEnd = bodyEnd;
            }

            if (path != null)
                blocks.Add(new FileBlock(path, CleanTaggedContent(text.Substring(bodyStart, bodyEnd - bodyStart))));
        }

        return blocks;
    }

    /// <summary>Finds the path in the text before a start tag: after a name tag, or on the last line.</summary>
    private static string? PathFromHeader(string header)
    {
        var tags = NameTag.Matches(header);
        for (int i = tags.Count - 1; i >= 0; i--)
        {
            int from = tags[i].Index + tags[i].Length;
            int to = i + 1 < tags.Count ? tags[i + 1].Index : header.Length;
            int newline = header.IndexOf('\n', from);
            if (newline >= 0 && newline < to) to = newline;

            var candidate = Parser.CleanPathText(header.Substring(from, to - from));
            if (Parser.LooksLikePath(candidate))
                return candidate;
        }

        var lastLine = header.Split('\n').LastOrDefault(l => l.Trim().Length > 0);
        var fallback = lastLine == null ? null : Parser.CleanPathText(lastLine);
        return Parser.LooksLikePath(fallback) ? fallback : null;
    }

    /// <summary>Trims the blank line after the start tag and any code fence the model wrapped the file in.</summary>
    private static string CleanTaggedContent(string body)
    {
        var lines = body.Split('\n').ToList();
        if (lines.Count > 0 && lines[0].Trim().Length == 0)
            lines.RemoveAt(0);
        TrimTrailingBlankLines(lines);

        if (lines.Count > 0 && Fence.IsMatch(lines[0]))
        {
            if (lines.Count >= 2 && BareFence.IsMatch(lines[^1]))
            {
                lines.RemoveAt(lines.Count - 1);
                lines.RemoveAt(0);
            }
            else if (!lines.Skip(1).Any(l => BareFence.IsMatch(l)))
            {
                // Opening fence inside the tags, closing fence after the end tag
                lines.RemoveAt(0);
            }
        }

        return JoinContent(lines);
    }

    private static List<FileBlock> ParseMarkdownFiles(string text, IReadOnlyList<string> requestedFiles, HashSet<string> known)
    {
        var lines = text.Split('\n');
        var blocks = new List<FileBlock>();
        var unnamed = new List<string>();
        int afterLastBlock = 0;

        for (int i = 0; i < lines.Length; i++)
        {
            var open = Fence.Match(lines[i]);
            if (!open.Success) continue;

            int close = FindClosingFence(lines, i, open.Groups[1].Value);
            var body = lines[(i + 1)..close].ToList();
            var info = open.Groups[2].Value;

            var path = PathFromInfo(info, known)
                ?? PathFromLinesAbove(lines, afterLastBlock, i, known)
                ?? PathFromComment(body, known);

            TrimTrailingBlankLines(body);
            var content = JoinContent(body);

            if (path != null)
                blocks.Add(new FileBlock(path, content));
            else if (!ShellLanguages.Contains(info.Split(' ')[0]))
                unnamed.Add(content);

            i = close;
            afterLastBlock = close + 1;
        }

        // A lone unnamed code block answering a single-file request is almost certainly that file
        if (blocks.Count == 0 && unnamed.Count == 1 && requestedFiles.Count == 1)
            blocks.Add(new FileBlock(requestedFiles[0], unnamed[0]));

        return blocks;
    }

    /// <summary>
    /// Finds the fence closing the block opened at openIndex, or lines.Length if unclosed.
    /// Fences with a language inside the block (e.g. in a README) are treated as nested.
    /// </summary>
    private static int FindClosingFence(string[] lines, int openIndex, string fence)
    {
        int depth = 0;
        for (int j = openIndex + 1; j < lines.Length; j++)
        {
            var m = Fence.Match(lines[j]);
            if (!m.Success) continue;

            var marker = m.Groups[1].Value;
            if (marker[0] != fence[0] || marker.Length < fence.Length) continue;

            if (m.Groups[2].Value.Length > 0)
                depth++;
            else if (depth > 0)
                depth--;
            else
                return j;
        }
        return lines.Length;
    }

    /// <summary>Path in the fence's info string: ```cs src/app.cs, ```src/app.cs, ```python:app.py, ```js title="app.js"</summary>
    private static string? PathFromInfo(string info, HashSet<string> known)
    {
        foreach (var token in Regex.Split(info, @"[\s:=""',]+"))
        {
            var candidate = Parser.CleanPathText(token);
            if (Parser.LooksLikePath(candidate, known))
                return candidate;
        }
        return null;
    }

    /// <summary>Path on one of the two non-blank lines above the fence: "src/app.py", "### `src/app.py`", "File: **app.py**", ...</summary>
    private static string? PathFromLinesAbove(string[] lines, int floor, int fenceIndex, HashSet<string> known)
    {
        int seen = 0;
        for (int j = fenceIndex - 1; j >= floor && seen < 2; j--)
        {
            if (lines[j].Trim().Length == 0) continue;
            seen++;

            var path = PathFromProse(lines[j], known);
            if (path != null)
                return path;
        }
        return null;
    }

    private static string? PathFromProse(string line, HashSet<string> known)
    {
        var whole = Parser.CleanPathText(line);
        if (Parser.LooksLikePath(whole, known))
            return whole;

        // "Here is the updated `src/app.py`:" - prefer marked-up paths, then known files, then anything path-like
        var marked = InlineCode.Matches(line).Concat(BoldText.Matches(line))
            .Select(m => Parser.CleanPathText(m.Groups[1].Value))
            .LastOrDefault(c => Parser.LooksLikePath(c, known));
        if (marked != null)
            return marked;

        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Parser.CleanPathText).ToList();
        return words.LastOrDefault(known.Contains) ?? words.LastOrDefault(w => Parser.LooksLikePath(w));
    }

    /// <summary>Path in a comment on the first line of the block: "// File: src/app.cs", "# app.py", "&lt;!-- index.html --&gt;"</summary>
    private static string? PathFromComment(List<string> body, HashSet<string> known)
    {
        var first = body.FirstOrDefault(l => l.Trim().Length > 0);
        if (first == null) return null;

        var m = PathComment.Match(first);
        if (!m.Success) return null;

        var candidate = Parser.CleanPathText(m.Groups[1].Value);
        return Parser.LooksLikePath(candidate, known) ? candidate : null;
    }

    private static void TrimTrailingBlankLines(List<string> lines)
    {
        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
            lines.RemoveAt(lines.Count - 1);
    }

    private static string JoinContent(List<string> lines) => lines.Count == 0 ? "" : string.Join("\n", lines) + "\n";

    public static bool SaveFileMD(string content)
    {
        File.WriteAllText(".stoat/" + new Random().Next(100000, 999999) + ".md", content);

        return true;
    }
}
