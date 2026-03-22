using StoatTote;
using System.Runtime.InteropServices;
using System.Text;

// Enable ANSI colors on Windows
if (OperatingSystem.IsWindows())
{
    var handle = GetStdHandle(-11);
    GetConsoleMode(handle, out var mode);
    SetConsoleMode(handle, mode | 0x0004);
}

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine(Config.StoatArt);
Console.WriteLine(Ansi.Dim($"  Ollama: {Config.OllamaUrl}"));
Console.WriteLine(Ansi.Dim("  Set OLLAMA_URL env var to change.\n"));

// Check Ollama is running
if (!await OllamaClient.EnsureRunningAsync())
    Environment.Exit(1);

// Pick a model
var models = await OllamaClient.GetModelsAsync();

if (models.Count == 0)
{
    Console.WriteLine(Ansi.Yellow("  ⚠ No models found. Run: ollama pull qwen3:8b\n"));
}
else
{
    Console.WriteLine(Ansi.Bold("  🗂  Available models:"));
    for (int i = 0; i < models.Count; i++)
    {
        var marker = models[i] == Config.Model ? Ansi.Green("►") : " ";
        Console.WriteLine($"    {marker} [{i + 1}] {models[i]}");
    }

    Console.Write(Ansi.Cyan("\n  Select a model ") + Ansi.Dim($"[1-{models.Count}, Enter = {Config.Model}] ") + "> ");
    var pick = Console.ReadLine()?.Trim() ?? "";

    if (!string.IsNullOrEmpty(pick) && int.TryParse(pick, out var idx) && idx >= 1 && idx <= models.Count)
        Config.Model = models[idx - 1];
    else if (!models.Any(m => m.Equals(Config.Model, StringComparison.OrdinalIgnoreCase)))
        Config.Model = models[0];

    Console.WriteLine(Ansi.Green($"\n  ✓ Using model: {Config.Model}\n"));
}

// Main loop
while (true)
{
    Console.Write(Ansi.Cyan("\n🐾 What do you need? ") + Ansi.Dim("(quit to exit)\n> "));
    var task = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(task) ||
        task.Trim().Equals("quit", StringComparison.OrdinalIgnoreCase) ||
        task.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine(Ansi.Yellow("\n  Stoat scurries away! 🐾\n"));
        break;
    }

    try
    {
        await RunTaskAsync(task);
    }
    catch (Exception ex)
    {
        ConversationLogger.LogError(ex.Message);
        Console.WriteLine(Ansi.Red($"\n  ✗ Error: {ex.Message}\n"));
    }
}

async Task RunTaskAsync(string task)
{
    var cwd = Directory.GetCurrentDirectory();
    var filesToSend = new List<string>();
    var toolResults = new List<ToolResult>();
    var readContents = new Dictionary<string, string>();

    // Start conversation logging
    var logFile = ConversationLogger.StartSession(task);
    Console.WriteLine(Ansi.Dim($"  📝 Logging to {logFile}\n"));

    // Phase 1: Ask LLM what files/operations it needs
    Console.WriteLine(Ansi.Dim("\n  🧠 Stoat is assessing the task...\n"));
    
    var tree = FileUtils.WalkDir(cwd);
    
    for (int round = 0; round < 5; round++)
    {
        var context = BuildPhase1Context(task, toolResults, readContents, tree);
        var response = await OllamaClient.GenerateAsync(context, Config.Phase1System);
        
        var requests = Parser.ParseToolRequests(response);
        
        if (requests.Count == 0)
        {
            var fileList = Parser.ParseFileList(response);
            if (fileList.Count > 0)
                requests.Add(new ToolRequest { Tool = "files_needed", Files = fileList });
            else
            {
                Console.WriteLine(Ansi.Yellow("  Stoat didn't request anything.\n"));
                break;
            }
        }

        // Log Phase 1 turn
        ConversationLogger.LogPhase1(context, response, requests, toolResults);
        toolResults.Clear();

        ShowRequests(requests);

        Console.Write(Ansi.Cyan("\n  Execute these? ") + Ansi.Dim("[Y/n/edit/proceed] ") + "> ");
        var confirm = Console.ReadLine() ?? "";

        if (confirm.Trim().Equals("n", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine(Ansi.Dim("  Skipped.\n")); break; }
        if (confirm.Trim().Equals("edit", StringComparison.OrdinalIgnoreCase))
        {
            Console.Write(Ansi.Cyan("\n  Enter file paths (comma-separated):\n  > "));
            filesToSend = Console.ReadLine()?.Split(',').Select(f => f.Trim()).Where(f => !string.IsNullOrEmpty(f)).ToList() ?? new();
            break;
        }
        if (confirm.Trim().Equals("proceed", StringComparison.OrdinalIgnoreCase)) break;

        // Execute tools
        Console.WriteLine(Ansi.Dim("\n  🔧 Executing operations..."));
        
        foreach (var req in requests)
        {
            var result = await ToolExecutor.ExecuteAsync(req, cwd);
            toolResults.Add(result);
            ToolExecutor.DisplayResult(result);

            if (req.Tool == "read_file" && result.Success)
            {
                var content = Parser.ExtractReadFileContent(result);
                if (content != null && req.Path != null)
                    readContents[req.Path] = content;
            }

            if (req.Tool == "files_needed" && result.Success && result.Data is List<string> files)
                foreach (var f in files.Where(f => !filesToSend.Contains(f)))
                    filesToSend.Add(f);
        }

        Console.Write(Ansi.Cyan("\n  Continue exploring? ") + Ansi.Dim("[y/proceed] ") + "> ");
        if (!Console.ReadLine()?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ?? true)
            break;
    }

    if (filesToSend.Count == 0)
        Console.WriteLine(Ansi.Yellow("\n  No files collected - Stoat will create from scratch.\n"));
    else
        ShowCollectedFiles(filesToSend);

    // Phase 2: Get code changes
    Console.WriteLine(Ansi.Dim("\n  📦 Packing files..."));
    var packed = PackFiles(task, filesToSend, cwd);

    Console.WriteLine(Ansi.Cyan("\n  🐾 Stoat is working...\n"));
    Console.WriteLine(Ansi.Dim(new string('─', 60)));
    var response2 = await OllamaClient.GenerateStreamAsync(packed, Config.Phase2System);
    Console.WriteLine(Ansi.Dim(new string('─', 60)));

    // Parse and apply
    var blocks = Parser.ParseFileBlocks(response2);

    // Log Phase 2
    ConversationLogger.LogPhase2(packed, response2, blocks);

    if (blocks.Count == 0)
    {
        Console.WriteLine(Ansi.Yellow("\n  Stoat didn't return any files.\n"));
        return;
    }

    Console.WriteLine(Ansi.Bold($"\n  📝 {blocks.Count} file(s) ready:"));
    foreach (var fb in blocks)
    {
        var exists = File.Exists(Path.Combine(cwd, fb.Path));
        Console.WriteLine($"    {(exists ? Ansi.Yellow("~") : Ansi.Green("+"))} {fb.Path} {Ansi.Dim($"({fb.Content.Length} chars)")}");
    }

    Console.Write(Ansi.Cyan("\n  Apply changes? ") + Ansi.Dim("[Y/n] ") + "> ");
    if (Console.ReadLine()?.Trim().Equals("n", StringComparison.OrdinalIgnoreCase) ?? false)
    {
        Console.WriteLine(Ansi.Dim("  Changes discarded.\n"));
        return;
    }

    Console.WriteLine(Ansi.Dim("\n  💾 Backing up and writing files..."));
    foreach (var fb in blocks)
    {
        var fullPath = Path.Combine(cwd, fb.Path);
        var backup = FileUtils.BackupFile(fullPath);
        if (backup != null)
            Console.WriteLine(Ansi.Dim($"    ↩ Backed up: {fb.Path}"));

        FileUtils.EnsureDir(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, fb.Content, Encoding.UTF8);
        Console.WriteLine(Ansi.Green($"    ✓ Written: {fb.Path}"));
    }

    Console.WriteLine(Ansi.Green(Ansi.Bold("\n  ✅ Done!")));
    Console.WriteLine(Ansi.Dim($"  Backups in {Config.BackupDir}/\n"));
    Console.WriteLine(Config.StoatReturnArt);
}

string BuildPhase1Context(string task, List<ToolResult> results, Dictionary<string, string> readContents, List<string> tree)
{
    var ctx = new StringBuilder();
    ctx.AppendLine($"Task: {task.Trim()}");
    ctx.AppendLine();

    if (results.Count > 0)
    {
        ctx.AppendLine("Previous results:");
        foreach (var r in results)
        {
            ctx.AppendLine($"  [{r.ToolType}] {r.Message}");
            if (r.ToolType == "search_files" && r.Data is List<SearchResult> sr)
                foreach (var s in sr)
                    ctx.AppendLine($"    - {s.FilePath}:{s.LineNumber}");
            if (r.ToolType == "list_files" && r.Data is List<string> fl)
                foreach (var f in fl)
                    ctx.AppendLine($"    - {f}");
        }
        ctx.AppendLine();
    }

    if (readContents.Count > 0)
    {
        ctx.AppendLine("File contents:");
        foreach (var (path, content) in readContents)
        {
            ctx.AppendLine($"[Filename: {path}]");
            ctx.AppendLine("```");
            ctx.AppendLine(content);
            ctx.AppendLine("```");
            ctx.AppendLine("[End Filename]");
        }
        ctx.AppendLine();
    }

    ctx.AppendLine("Project files:");
    ctx.Append(string.Join("\n", tree));
    return ctx.ToString();
}

void ShowRequests(List<ToolRequest> requests)
{
    Console.WriteLine(Ansi.Bold($"\n  📋 Stoat requests:"));
    foreach (var req in requests)
    {
        var desc = req.Tool.ToLowerInvariant() switch
        {
            "files_needed" => $"View {req.Files?.Count ?? 0} file(s)",
            "create_file" => $"Create: {req.Path}",
            "rename_file" => $"Rename: {req.OldPath} → {req.NewPath}",
            "move_file" => $"Move: {req.Source} → {req.Destination}",
            "delete_file" => $"Delete: {req.Path}",
            "search_files" => $"Search: '{req.Query}'",
            "list_files" => $"List: {req.Directory ?? "."}",
            "read_file" => $"Read: {req.Path}",
            _ => $"Unknown: {req.Tool}"
        };
        Console.WriteLine($"    • {desc}");
    }
}

void ShowCollectedFiles(List<string> files)
{
    Console.WriteLine(Ansi.Bold($"\n  📁 {files.Count} file(s) ready:"));
    foreach (var f in files.Take(10))
        Console.WriteLine($"    • {f}");
    if (files.Count > 10)
        Console.WriteLine(Ansi.Dim($"    ... and {files.Count - 10} more"));
}

string PackFiles(string task, List<string> files, string cwd)
{
    var packed = new StringBuilder();
    packed.AppendLine($"Task: {task.Trim()}");
    packed.AppendLine();

    foreach (var f in files)
    {
        var fullPath = Path.Combine(cwd, f);
        var content = FileUtils.ReadFileContents(fullPath);
        packed.AppendLine($"[Filename: {f}]");
        packed.AppendLine("```");
        if (content != null)
            packed.AppendLine(content);
        else if (!File.Exists(fullPath))
            packed.AppendLine("(new file)");
        else
            packed.AppendLine("(binary - skipped)");
        packed.AppendLine("```");
        packed.AppendLine("[End Filename]");
        packed.AppendLine();
    }
    return packed.ToString();
}

// Windows ANSI support
[DllImport("kernel32.dll")]
static extern IntPtr GetStdHandle(int nStdHandle);

[DllImport("kernel32.dll")]
static extern bool GetConsoleMode(IntPtr h, out uint m);

[DllImport("kernel32.dll")]
static extern bool SetConsoleMode(IntPtr h, uint m);
