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

Config.Load();

// Handle command line arguments
if (args.Length >= 2 && args[0].Equals("server", StringComparison.OrdinalIgnoreCase))
{
    var serverArg = args[1].Trim().ToLowerInvariant();
    if (Config.Servers.ContainsKey(serverArg))
    {
        Config.CurrentServerName = serverArg;
        Config.Save();
    }
    else
    {
        Console.WriteLine(Ansi.Red($"✗ Unknown server: {serverArg}"));
        Console.WriteLine(Ansi.Dim($"Available: {string.Join(", ", Config.Servers.Keys)}"));
        Environment.Exit(1);
    }
}

Console.WriteLine(Config.StoatArt);
Console.WriteLine(Ansi.Dim($"  Server: {Config.CurrentServer.Name} ({Config.CurrentServer.Url})"));
Console.WriteLine(Ansi.Dim($"  Config: {Config.ConfigFilePath}"));

if (Config.CurrentServer.Type == ServerType.OpenRouter && string.IsNullOrEmpty(Config.OpenRouterApiKey) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")))
{
    Console.WriteLine(Ansi.Yellow("  ⚠ OPENROUTER_API_KEY not set."));
    Console.Write(Ansi.Cyan("  Enter your OpenRouter API key ") + Ansi.Dim("(or press Enter to cancel)") + "> ");
    var keyInput = Console.ReadLine()?.Trim() ?? "";
    if (string.IsNullOrEmpty(keyInput))
    {
        Console.WriteLine(Ansi.Dim("  Cancelled.\n"));
        Environment.Exit(1);
    }
    Config.OpenRouterApiKey = keyInput;
    Config.Save();
    Console.WriteLine(Ansi.Green("  ✓ API key saved.\n"));
}
else
{
    Console.WriteLine();
}

// Check server is running
if (!await LlmClient.EnsureRunningAsync())
    Environment.Exit(1);

// Pick a model
var models = await LlmClient.GetModelsAsync();

var isOpenRouter = Config.CurrentServer.Type == ServerType.OpenRouter;
if (isOpenRouter)
{
    models = Config.PreferredModels
        .Where(p => !string.IsNullOrEmpty(p))
        .ToList();
}

if (models.Count == 0)
{
    if (isOpenRouter)
    {
        Console.Write(Ansi.Cyan("  Enter an OpenRouter model ID ") + Ansi.Dim("(e.g., anthropic/claude-3.5-sonnet)") + "> ");
        var customModel = Console.ReadLine()?.Trim() ?? "";
        if (string.IsNullOrEmpty(customModel))
        {
            Console.WriteLine(Ansi.Dim("  Cancelled.\n"));
            Environment.Exit(1);
        }
        Config.Model = customModel;
        Config.PreferredModels.Insert(0, customModel);
        Config.Save();
        Console.WriteLine(Ansi.Green($"\n  ✓ Using model: {Config.Model}\n"));
    }
    else
    {
        Console.WriteLine(Ansi.Yellow("  ⚠ No models found.\n"));
    }
}
else
{
    Console.WriteLine(Ansi.Bold("  🗂  Available models:"));
    for (int i = 0; i < models.Count; i++)
    {
        var marker = models[i] == Config.Model ? Ansi.Green("►") : " ";
        Console.WriteLine($"    {marker} [{i + 1}] {models[i]}");
    }

    var promptExtra = isOpenRouter ? ", or type a new model ID" : "";
    Console.Write(Ansi.Cyan("\n  Select a model ") + Ansi.Dim($"[1-{models.Count}, Enter = {Config.Model}{promptExtra}] ") + "> ");
    var pick = Console.ReadLine()?.Trim() ?? "";

    if (!string.IsNullOrEmpty(pick) && int.TryParse(pick, out var idx) && idx >= 1 && idx <= models.Count)
    {
        Config.Model = models[idx - 1];
    }
    else if (!string.IsNullOrEmpty(pick) && isOpenRouter)
    {
        Config.Model = pick;
        if (!Config.PreferredModels.Any(p => p.Equals(pick, StringComparison.OrdinalIgnoreCase)))
        {
            Config.PreferredModels.Insert(0, pick);
        }
    }
    else if (!models.Any(m => m.Equals(Config.Model, StringComparison.OrdinalIgnoreCase)))
    {
        Config.Model = models[0];
    }

    if (isOpenRouter && !Config.PreferredModels.Any(p => p.Equals(Config.Model, StringComparison.OrdinalIgnoreCase)))
    {
        Config.PreferredModels.Insert(0, Config.Model);
    }

    Console.WriteLine(Ansi.Green($"\n  ✓ Using model: {Config.Model}\n"));
    Config.Save();
}

// Personality selection
var personalities = StoatPersonalityManager.GetPersonalityNames();

Console.WriteLine(Ansi.Bold("  🎭 Available personalities:"));
for (int i = 0; i < personalities.Count; i++)
{
    var pName = personalities[i];
    var personality = StoatPersonalityManager.GetPersonality(pName);
    var marker = pName == Config.DefaultPersonality ? Ansi.Green("►") : " ";
    Console.WriteLine($"    {marker} [{i + 1}] {pName,-12} {Ansi.Dim(personality.Description)}");
}

Console.Write(Ansi.Cyan("\n  Select a personality ") + Ansi.Dim($"[1-{personalities.Count}, Enter = {Config.DefaultPersonality}] ") + "> ");
var personalityPick = Console.ReadLine()?.Trim() ?? "";

if (!string.IsNullOrEmpty(personalityPick) && int.TryParse(personalityPick, out var pIdx) && pIdx >= 1 && pIdx <= personalities.Count)
    Config.DefaultPersonality = personalities[pIdx - 1];

Config.CurrentPersonality = StoatPersonalityManager.GetPersonality(Config.DefaultPersonality);
Console.WriteLine(Ansi.Green($"\n  ✓ Using personality: {Config.DefaultPersonality}\n"));
Config.Save();

// Mode selection
Console.WriteLine(Ansi.Bold("  🎯 Select Mode:"));
Console.WriteLine("    [1] Code Mode  " + Ansi.Dim("- Analyze files, generate code changes"));
Console.WriteLine("    [2] Chat Mode  " + Ansi.Dim("- Conversational assistance (type 'code' or 'chat' to switch)"));
Console.Write(Ansi.Cyan("\n  Choose mode ") + Ansi.Dim("[1/2, Enter = 1] ") + "> ");
var modePick = Console.ReadLine()?.Trim() ?? "1";

if (modePick == "2")
    Config.CurrentMode = CliMode.Chat;
else
    Config.CurrentMode = CliMode.Code;

Console.WriteLine(Ansi.Green($"\n  ✓ Starting in {Config.CurrentMode} Mode\n"));
Config.Save();
Console.WriteLine(Ansi.Dim("  Type 'help' for commands, 'code' or 'chat' to switch modes.\n"));

var chatHistory = new List<(string role, string content)>();

// Main loop
while (true)
{
    var modeIndicator = Config.CurrentMode == CliMode.Chat
        ? Ansi.Magenta("[CHAT] ")
        : Ansi.Green("[CODE] ");

    Console.Write(Ansi.Cyan($"\n{modeIndicator}Stoat is awaiting your request: ") + Ansi.Dim("(type 'help' for commands)\n> "));

    var input = Console.ReadLine() ?? "";

    var commandResult = HandleModeCommand(input, chatHistory);
    if (commandResult.shouldExit)
    {
        Console.WriteLine(Ansi.Yellow("\n  Stoat scurries away! 🐾\n"));
        break;
    }
    if (commandResult.switchedMode || commandResult.wasHelp)
        continue;

    if (string.IsNullOrWhiteSpace(input))
        continue;

    try
    {
        if (Config.CurrentMode == CliMode.Chat)
            await RunChatModeAsync(input, chatHistory);
        else
            await RunCodeModeAsync(input, chatHistory);
    }
    catch (Exception ex)
    {
        ConversationLogger.LogError(ex.Message);
        Console.WriteLine(Ansi.Red($"\n  ✗ Error: {ex.Message}\n"));
    }
}

// === Helper Methods ===

(bool shouldExit, bool switchedMode, bool wasHelp) HandleModeCommand(string input, List<(string role, string content)> history)
{
    var trimmed = input.Trim().ToLowerInvariant();

    if (trimmed is "quit" or "exit" or "q")
        return (true, false, false);

    if (trimmed is "help" or "?")
    {
        Console.WriteLine(Ansi.Bold("\n  📋 Available Commands:"));
        Console.WriteLine("    help, ?        " + Ansi.Dim("- Show this help message"));
        Console.WriteLine("    quit, exit, q  " + Ansi.Dim("- Exit the application"));
        Console.WriteLine("    code           " + Ansi.Dim("- Switch to Code Mode"));
        Console.WriteLine("    chat           " + Ansi.Dim("- Switch to Chat Mode"));
        Console.WriteLine("    server [name]  " + Ansi.Dim("- Switch server (ollama, llamacpp, openrouter)"));
        Console.WriteLine("    export         " + Ansi.Dim("- Export conversation as markdown file"));
        Console.WriteLine(Ansi.Dim("  (Or type your message to continue in current mode)\n"));
        return (false, false, true);
    }

    if (trimmed == "export")
    {
        ExportConversationAsMarkdown(history);
        return (false, false, true);
    }

    if (trimmed == "server" || trimmed.StartsWith("server "))
    {
        var arg = trimmed.Length > 7 ? trimmed.Substring(7).Trim() : "";
        if (string.IsNullOrEmpty(arg))
        {
            Console.WriteLine(Ansi.Bold("\n  🖥  Servers:"));
            foreach (var kvp in Config.Servers)
            {
                var marker = kvp.Key.Equals(Config.CurrentServerName, StringComparison.OrdinalIgnoreCase) ? Ansi.Green("►") : " ";
                Console.WriteLine($"    {marker} {kvp.Key,-12} {Ansi.Dim(kvp.Value.Url)}");
            }
            Console.WriteLine();
            return (false, false, true);
        }

        Task.Run(async () => await SwitchServerAsync(arg, history)).GetAwaiter().GetResult();
        return (false, true, false);
    }

    bool isCodeCommand = trimmed is "mode code" or "code" or "/code";
    bool isChatCommand = trimmed is "mode chat" or "chat" or "/chat";

    if (isCodeCommand && Config.CurrentMode != CliMode.Code)
    {
        Config.CurrentMode = CliMode.Code;
        Config.Save();
        history.Add(("system", "[Switched to Code Mode]"));
        Console.WriteLine(Ansi.Cyan("\n  ↔ Switched to Code Mode\n"));
        return (false, true, false);
    }

    if (isChatCommand && Config.CurrentMode != CliMode.Chat)
    {
        Config.CurrentMode = CliMode.Chat;
        Config.Save();
        history.Add(("system", "[Switched to Chat Mode]"));
        Console.WriteLine(Ansi.Cyan("\n  ↔ Switched to Chat Mode\n"));
        return (false, true, false);
    }

    return (false, false, false);
}

async Task SwitchServerAsync(string serverName, List<(string role, string content)> history)
{
    if (!Config.Servers.ContainsKey(serverName))
    {
        Console.WriteLine(Ansi.Red($"\n  ✗ Unknown server: {serverName}"));
        Console.WriteLine(Ansi.Dim($"  Available: {string.Join(", ", Config.Servers.Keys)}\n"));
        return;
    }

    if (Config.CurrentServerName.Equals(serverName, StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine(Ansi.Yellow($"\n  Already using {serverName}\n"));
        return;
    }

    Config.CurrentServerName = serverName;
    Console.WriteLine(Ansi.Cyan($"\n  ↔ Switched to {Config.CurrentServer.Name} ({Config.CurrentServer.Url})\n"));

    if (Config.CurrentServer.Type == ServerType.OpenRouter && string.IsNullOrEmpty(Config.OpenRouterApiKey) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")))
    {
        Console.Write(Ansi.Cyan("  Enter your OpenRouter API key ") + Ansi.Dim("(or press Enter to cancel)") + "> ");
        var keyInput = Console.ReadLine()?.Trim() ?? "";
        if (string.IsNullOrEmpty(keyInput))
        {
            Console.WriteLine(Ansi.Dim("  Cancelled.\n"));
            Config.CurrentServerName = "ollama";
            Console.WriteLine(Ansi.Cyan("  ↔ Reverted to ollama\n"));
            return;
        }
        Config.OpenRouterApiKey = keyInput;
        Config.Save();
        Console.WriteLine(Ansi.Green("  ✓ API key saved.\n"));
    }

    if (!await LlmClient.EnsureRunningAsync())
    {
        Console.WriteLine(Ansi.Yellow("  ⚠ Server not reachable. You may need to start it manually.\n"));
        return;
    }

    var models = await LlmClient.GetModelsAsync();
    var isOpenRouter = Config.CurrentServer.Type == ServerType.OpenRouter;
    if (isOpenRouter)
    {
        models = Config.PreferredModels
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();
    }

    if (models.Count == 0)
    {
        if (isOpenRouter)
        {
            Console.Write(Ansi.Cyan("  Enter an OpenRouter model ID ") + Ansi.Dim("(e.g., anthropic/claude-3.5-sonnet)") + "> ");
            var customModel = Console.ReadLine()?.Trim() ?? "";
            if (string.IsNullOrEmpty(customModel))
            {
                Console.WriteLine(Ansi.Dim("  Cancelled.\n"));
                Config.CurrentServerName = "ollama";
                Console.WriteLine(Ansi.Cyan("  ↔ Reverted to ollama\n"));
                return;
            }
            Config.Model = customModel;
            Config.PreferredModels.Insert(0, customModel);
            Config.Save();
            Console.WriteLine(Ansi.Green($"\n  ✓ Using model: {Config.Model}\n"));
        }
        else
        {
            Console.WriteLine(Ansi.Yellow("  ⚠ No models found on this server.\n"));
        }
    }
    else
    {
        var currentExists = models.Any(m => m.Equals(Config.Model, StringComparison.OrdinalIgnoreCase));
        if (!currentExists)
        {
            Console.WriteLine(Ansi.Yellow($"  ⚠ Current model '{Config.Model}' not found on this server."));
            Console.WriteLine(Ansi.Bold("  🗂  Available models:"));
            for (int i = 0; i < models.Count; i++)
            {
                Console.WriteLine($"    [{i + 1}] {models[i]}");
            }
            var promptExtra = isOpenRouter ? ", or type a new model ID" : "";
            Console.Write(Ansi.Cyan("\n  Select a model ") + Ansi.Dim($"[1-{models.Count}{promptExtra}] ") + "> ");
            var pick = Console.ReadLine()?.Trim() ?? "";
            if (int.TryParse(pick, out var idx) && idx >= 1 && idx <= models.Count)
            {
                Config.Model = models[idx - 1];
            }
            else if (!string.IsNullOrEmpty(pick) && isOpenRouter)
            {
                Config.Model = pick;
                if (!Config.PreferredModels.Any(p => p.Equals(pick, StringComparison.OrdinalIgnoreCase)))
                {
                    Config.PreferredModels.Insert(0, pick);
                }
            }
            else
            {
                Config.Model = models[0];
            }

            if (isOpenRouter && !Config.PreferredModels.Any(p => p.Equals(Config.Model, StringComparison.OrdinalIgnoreCase)))
            {
                Config.PreferredModels.Insert(0, Config.Model);
            }

            Console.WriteLine(Ansi.Green($"\n  ✓ Using model: {Config.Model}\n"));
        }
        else
        {
            Console.WriteLine(Ansi.Green($"  ✓ Model {Config.Model} is available.\n"));
        }
    }

    Config.Save();
    history.Add(("system", $"[Switched to {Config.CurrentServer.Name} server]"));
}

void ExportConversationAsMarkdown(List<(string role, string content)> history)
{
    if (history.Count == 0)
    {
        Console.WriteLine(Ansi.Yellow("\n  No conversation to export.\n"));
        return;
    }

    var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
    var filename = $"conversation_{timestamp}.md";

    var md = new StringBuilder();
    md.AppendLine("# Stoat Conversation Export");
    md.AppendLine();
    md.AppendLine($"**Exported:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    md.AppendLine($"**Server:** {Config.CurrentServer.Name}");
    md.AppendLine($"**Model:** {Config.Model}");
    md.AppendLine($"**Personality:** {Config.DefaultPersonality}");
    md.AppendLine();
    md.AppendLine("---");
    md.AppendLine();

    foreach (var (role, content) in history)
    {
        switch (role)
        {
            case "user":
                md.AppendLine("## User\n");
                md.AppendLine(content + "\n");
                break;
            case "assistant":
                md.AppendLine("## Stoat\n");
                md.AppendLine(content + "\n");
                break;
            case "system":
                md.AppendLine($"> **System:** {content}\n");
                break;
        }
    }

    File.WriteAllText(filename, md.ToString(), Encoding.UTF8);
    Console.WriteLine(Ansi.Green($"\n  ✓ Exported to {filename}\n"));
}

async Task RunChatModeAsync(string task, List<(string role, string content)> history)
{
    history.Add(("user", task));

    var logFile = ConversationLogger.StartSession(task);
    Console.WriteLine(Ansi.Dim($"  📝 Logging to {logFile}\n"));

    var prompt = BuildChatPrompt(history);

    Console.WriteLine(Ansi.Dim("\n  💬 Stoat:\n"));
    Console.WriteLine(Ansi.Dim(new string('─', 60)));

    var response = await LlmClient.GenerateStreamAsync(prompt, Config.CurrentPersonality.ChatSystem);

    Console.WriteLine(Ansi.Dim(new string('─', 60)));

    history.Add(("assistant", response));
    ConversationLogger.LogChatTurn(task, response);
}

string BuildChatPrompt(List<(string role, string content)> history)
{
    var prompt = new StringBuilder();
    foreach (var (role, content) in history.TakeLast(10))
    {
        if (role == "user")
            prompt.AppendLine($"User: {content}");
        else
            prompt.AppendLine($"Assistant: {content}");
    }
    prompt.Append("Assistant: ");
    return prompt.ToString();
}

async Task RunCodeModeAsync(string task, List<(string role, string content)> history)
{
    var cwd = Directory.GetCurrentDirectory();

    var logFile = ConversationLogger.StartSession(task);
    Console.WriteLine(Ansi.Dim($"  📝 Logging to {logFile}\n"));

    // Phase 1: Send file tree, ask LLM which files it needs

    var tree = FileUtils.WalkDir(cwd);
    var phase1Prompt = $"Task: {task.Trim()}\n\nProject files:\n{string.Join("\n", tree)}";

    var phase1Response = await LlmClient.GenerateStreamAsync(phase1Prompt, Config.CurrentPersonality.Phase1System);
    var fileList = Parser.ParseFileList(phase1Response);

    ConversationLogger.LogPhase1(phase1Prompt, phase1Response, fileList);

    if (fileList.Count == 0)
    {
        Console.WriteLine(Ansi.Yellow("  No files requested - Stoat will create from scratch.\n"));
    }
    else
    {
        ShowCollectedFiles(fileList, cwd);

        Console.Write(Ansi.Cyan("\n  Send these files? ") + Ansi.Dim("[Y/n/edit] ") + "> ");
        var confirm = Console.ReadLine()?.Trim() ?? "";

        if (confirm.Equals("n", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(Ansi.Dim("  Cancelled.\n"));
            return;
        }

        if (confirm.Equals("edit", StringComparison.OrdinalIgnoreCase))
        {
            Console.Write(Ansi.Cyan("\n  Enter file paths (comma-separated):\n  > "));
            fileList = Console.ReadLine()?.Split(',')
                .Select(f => f.Trim())
                .Where(f => !string.IsNullOrEmpty(f))
                .ToList() ?? new();
        }
    }

    // Phase 2: Pack files and get code changes
    Console.WriteLine(Ansi.Dim("\n  📦 Packing files..."));
    var packed = PackFiles(task, fileList, cwd);

    Console.WriteLine(Ansi.Cyan("\n  🐾 Stoat is working...\n"));
    Console.WriteLine(Ansi.Dim(new string('─', 60)));
    var response = await LlmClient.GenerateStreamAsync(packed, Config.CurrentPersonality.Phase2System);
    Console.WriteLine(Ansi.Dim(new string('─', 60)));

    FileBlock.SaveFileMD(response);

    var blocks = Parser.ParseFileBlocks(response);
    ConversationLogger.LogPhase2(packed, response, blocks);

    if (blocks.Count == 0)
    {
        Console.WriteLine(Ansi.Yellow("\n  Stoat didn't return any files.\n"));
        return;
    }

    // Show proposed changes
    Console.WriteLine(Ansi.Bold($"\n  📝 {blocks.Count} file(s) ready:"));
    foreach (var fb in blocks)
    {
        var exists = File.Exists(Path.Combine(cwd, fb.FilePath));
        Console.WriteLine($"    {(exists ? Ansi.Yellow("~") : Ansi.Green("+"))} {fb.FilePath} {Ansi.Dim($"({fb.Content.Length} chars)")}");
    }

    Console.Write(Ansi.Cyan("\n  Apply changes? ") + Ansi.Dim("[Y/n] ") + "> ");
    if (Console.ReadLine()?.Trim().Equals("n", StringComparison.OrdinalIgnoreCase) ?? false)
    {
        Console.WriteLine(Ansi.Dim("  Changes discarded.\n"));
        return;
    }

    // Backup and write
    Console.WriteLine(Ansi.Dim("\n  💾 Backing up and writing files..."));
    foreach (var fb in blocks)
    {
        var fullPath = Path.Combine(cwd, fb.FilePath);
        var backup = FileUtils.BackupFile(fullPath);
        if (backup != null)
            Console.WriteLine(Ansi.Dim($"    ↩ Backed up: {fb.FilePath}"));

        FileUtils.EnsureDir(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, fb.Content, Encoding.UTF8);
        Console.WriteLine(Ansi.Green($"    ✓ Written: {fb.FilePath}"));
    }

    Console.WriteLine(Ansi.Green(Ansi.Bold("\n  ✅ Done!")));
    Console.WriteLine(Ansi.Dim($"  Backups in {Config.BackupDir}/\n"));
    Console.WriteLine(Config.StoatReturnArt);
}

void ShowCollectedFiles(List<string> files, string cwd)
{
    Console.WriteLine(Ansi.Bold($"\n  📎 {files.Count} file(s) requested:"));
    foreach (var f in files.Take(20))
    {
        var exists = File.Exists(Path.Combine(cwd, f));
        var marker = exists ? Ansi.Green("✓") : Ansi.Yellow("✗");
        var status = exists ? "" : Ansi.Dim(" (not found)");
        Console.WriteLine($"    {marker} {f}{status}");
    }
    if (files.Count > 20)
        Console.WriteLine(Ansi.Dim($"    ... and {files.Count - 20} more"));
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

        packed.Append(Config.FileTag);
        packed.Append(f);
        packed.Append(Config.FileTag);
        packed.Append(Config.FileStartTag);
        if (content != null)
            packed.Append(content);
        else if (!File.Exists(fullPath))
            packed.Append("(new file)");
        else
            packed.Append("(binary - skipped)");
        packed.Append(Config.FileEndTag);
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
