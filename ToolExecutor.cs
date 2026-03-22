using System.Text.RegularExpressions;

namespace StoatTote;

/// <summary>Executes tool requests from the LLM safely.</summary>
internal static class ToolExecutor
{
    public static Task<ToolResult> ExecuteAsync(ToolRequest request, string cwd)
    {
        return request.Tool.ToLowerInvariant() switch
        {
            "files_needed" => Task.FromResult(FilesNeeded(request)),
            "create_file" => Task.FromResult(CreateFile(request, cwd)),
            "rename_file" => Task.FromResult(RenameFile(request, cwd)),
            "move_file" => Task.FromResult(MoveFile(request, cwd)),
            "delete_file" => Task.FromResult(DeleteFile(request, cwd)),
            "search_files" => Task.FromResult(SearchFiles(request, cwd)),
            "list_files" => Task.FromResult(ListFiles(request, cwd)),
            "read_file" => Task.FromResult(ReadFile(request, cwd)),
            _ => Task.FromResult(new ToolResult(false, $"Unknown tool: {request.Tool}"))
        };
    }

    private static ToolResult FilesNeeded(ToolRequest req)
    {
        var files = req.Files ?? new List<string>();
        return files.Count == 0
            ? new ToolResult(true, "No files requested", "files_needed")
            : new ToolResult(true, $"Files requested: {files.Count}", "files_needed", files);
    }

    private static ToolResult CreateFile(ToolRequest req, string cwd)
    {
        if (string.IsNullOrWhiteSpace(req.Path))
            return new ToolResult(false, "Missing 'path'");
        if (req.Content is null)
            return new ToolResult(false, "Missing 'content'");

        var fullPath = Path.Combine(cwd, req.Path);
        if (File.Exists(fullPath))
            return new ToolResult(false, $"File exists: {req.Path}");

        FileUtils.EnsureDir(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, req.Content, System.Text.Encoding.UTF8);
        return new ToolResult(true, $"Created: {req.Path}", "create_file");
    }

    private static ToolResult RenameFile(ToolRequest req, string cwd)
    {
        if (string.IsNullOrWhiteSpace(req.OldPath) || string.IsNullOrWhiteSpace(req.NewPath))
            return new ToolResult(false, "Missing 'old_path' or 'new_path'");

        var oldPath = Path.Combine(cwd, req.OldPath);
        var newPath = Path.Combine(cwd, req.NewPath);

        if (!File.Exists(oldPath)) return new ToolResult(false, $"Not found: {req.OldPath}");
        if (File.Exists(newPath)) return new ToolResult(false, $"Exists: {req.NewPath}");

        FileUtils.BackupFile(oldPath);
        FileUtils.EnsureDir(Path.GetDirectoryName(newPath)!);
        File.Move(oldPath, newPath);
        return new ToolResult(true, $"Renamed: {req.OldPath} → {req.NewPath}", "rename_file");
    }

    private static ToolResult MoveFile(ToolRequest req, string cwd)
    {
        if (string.IsNullOrWhiteSpace(req.Source) || string.IsNullOrWhiteSpace(req.Destination))
            return new ToolResult(false, "Missing 'source' or 'destination'");

        var src = Path.Combine(cwd, req.Source);
        var dst = Path.Combine(cwd, req.Destination);

        if (!File.Exists(src)) return new ToolResult(false, $"Not found: {req.Source}");
        if (File.Exists(dst)) return new ToolResult(false, $"Exists: {req.Destination}");

        FileUtils.BackupFile(src);
        FileUtils.EnsureDir(Path.GetDirectoryName(dst)!);
        File.Move(src, dst);
        return new ToolResult(true, $"Moved: {req.Source} → {req.Destination}", "move_file");
    }

    private static ToolResult DeleteFile(ToolRequest req, string cwd)
    {
        if (string.IsNullOrWhiteSpace(req.Path))
            return new ToolResult(false, "Missing 'path'");

        var fullPath = Path.Combine(cwd, req.Path);
        if (!File.Exists(fullPath))
            return new ToolResult(false, $"Not found: {req.Path}");

        FileUtils.BackupFile(fullPath);
        File.Delete(fullPath);
        return new ToolResult(true, $"Deleted: {req.Path}", "delete_file");
    }

    private static ToolResult SearchFiles(ToolRequest req, string cwd)
    {
        if (string.IsNullOrWhiteSpace(req.Query))
            return new ToolResult(false, "Missing 'query'");

        var results = new List<SearchResult>();
        var pattern = req.FilePattern ?? "*";
        var regex = new Regex(Regex.Escape(req.Query), RegexOptions.IgnoreCase);

        try
        {
            var files = Directory.GetFiles(cwd, pattern, SearchOption.AllDirectories)
                .Where(f => !Config.Ignore.Any(i => f.Contains(i, StringComparison.OrdinalIgnoreCase)));

            foreach (var file in files)
            {
                try
                {
                    if (FileUtils.IsBinary(file)) continue;
                    var lines = File.ReadAllLines(file);
                    var rel = Path.GetRelativePath(cwd, file);

                    for (int i = 0; i < lines.Length; i++)
                        if (regex.IsMatch(lines[i]))
                            results.Add(new SearchResult(rel, i + 1, lines[i].Trim()));
                }
                catch { /* skip unreadable */ }
            }

            return results.Count > 0
                ? new ToolResult(true, $"Found {results.Count} matches", "search_files", results)
                : new ToolResult(true, $"No matches for '{req.Query}'", "search_files", results);
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"Search error: {ex.Message}");
        }
    }

    private static ToolResult ListFiles(ToolRequest req, string cwd)
    {
        var target = string.IsNullOrWhiteSpace(req.Directory) ? cwd : Path.Combine(cwd, req.Directory);
        if (!Directory.Exists(target))
            return new ToolResult(false, $"Not found: {req.Directory}");

        try
        {
            var tree = FileUtils.WalkDir(target, cwd);
            return new ToolResult(true, $"Found {tree.Count} items", "list_files", tree);
        }
        catch (Exception ex)
        {
            return new ToolResult(false, $"List error: {ex.Message}");
        }
    }

    private static ToolResult ReadFile(ToolRequest req, string cwd)
    {
        if (string.IsNullOrWhiteSpace(req.Path))
            return new ToolResult(false, "Missing 'path'");

        var fullPath = Path.Combine(cwd, req.Path);
        if (!File.Exists(fullPath))
            return new ToolResult(false, $"Not found: {req.Path}");

        var content = FileUtils.ReadFileContents(fullPath);
        if (content is null)
            return new ToolResult(false, $"Binary file: {req.Path}");

        return new ToolResult(true, $"Read: {req.Path}", "read_file", new { path = req.Path, content });
    }

    /// <summary>Displays tool result to console.</summary>
    public static void DisplayResult(ToolResult result)
    {
        Console.WriteLine($"    {(result.Success ? Ansi.Green("✓") : Ansi.Red("✗"))} {(result.Success ? Ansi.Green(result.Message) : Ansi.Yellow(result.Message))}");

        if (result.Data is List<SearchResult> sr && sr.Count > 0)
        {
            foreach (var s in sr.Take(20))
                Console.WriteLine(Ansi.Dim($"       {s.FilePath}:{s.LineNumber}: {(s.LineContent.Length > 60 ? s.LineContent[..60] : s.LineContent)}"));
            if (sr.Count > 20)
                Console.WriteLine(Ansi.Dim($"       ... and {sr.Count - 20} more"));
        }

        if (result.Data is List<string> files && files.Count > 0 && result.ToolType == "files_needed")
        {
            foreach (var f in files.Take(10))
                Console.WriteLine(Ansi.Dim($"       • {f}"));
            if (files.Count > 10)
                Console.WriteLine(Ansi.Dim($"       ... and {files.Count - 10} more"));
        }
    }
}
