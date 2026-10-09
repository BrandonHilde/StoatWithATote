using StoatTote;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

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

await CheckVisionAsync();
Console.WriteLine(Ansi.Dim($"  🌐 Web access: {Config.WebMode.ToString().ToLowerInvariant()} (type 'web' for settings)\n"));

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
var pendingImages = new List<ImageAttachment>();

// Main loop
while (true)
{
    var modeIndicator = Config.CurrentMode == CliMode.Chat
        ? Ansi.Magenta("[CHAT] ")
        : Ansi.Green("[CODE] ");
    var imageIndicator = pendingImages.Count > 0 ? Ansi.Yellow($"[📎 {pendingImages.Count}] ") : "";

    Console.Write(Ansi.Cyan($"\n{modeIndicator}") + imageIndicator + Ansi.Cyan("Stoat is awaiting your request: ") + Ansi.Dim("(type 'help' for commands)\n> "));

    var input = Console.ReadLine() ?? "";

    var commandResult = HandleModeCommand(input, chatHistory, pendingImages);
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
            await RunChatModeAsync(input, chatHistory, pendingImages);
        else
            await RunCodeModeAsync(input, chatHistory, pendingImages);
    }
    catch (Exception ex)
    {
        ConversationLogger.LogError(ex.Message);
        Console.WriteLine(Ansi.Red($"\n  ✗ Error: {ex.Message}\n"));
    }
}

// === Helper Methods ===

(bool shouldExit, bool switchedMode, bool wasHelp) HandleModeCommand(string input, List<(string role, string content)> history, List<ImageAttachment> images)
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
        Console.WriteLine("    image <path>   " + Ansi.Dim("- Attach an image to your next request"));
        Console.WriteLine("    image          " + Ansi.Dim("- List attached images ('image clear' removes them)"));
        Console.WriteLine("    web            " + Ansi.Dim("- Show web access settings"));
        Console.WriteLine("    web <mode>     " + Ansi.Dim("- off, ask, whitelist, blacklist or full"));
        Console.WriteLine("    web allow|block|remove <domain> " + Ansi.Dim("- Edit the whitelist/blacklist"));
        Console.WriteLine("    web search <url> " + Ansi.Dim("- Search engine URL with {query}, or 'default'"));
        Console.WriteLine(Ansi.Dim("  (Or type your message to continue in current mode)\n"));
        return (false, false, true);
    }

    if (trimmed is "image" or "img" || trimmed.StartsWith("image ") || trimmed.StartsWith("img "))
    {
        // Use the original input so path casing survives; drag-and-drop paths arrive quoted
        var raw = input.Trim();
        var space = raw.IndexOf(' ');
        var arg = space < 0 ? "" : raw.Substring(space + 1).Trim().Trim('"', '\'');

        // "image processing in C#?" is a message, not a command
        if (arg.Length == 0 || arg.Equals("clear", StringComparison.OrdinalIgnoreCase)
            || ImageAttachment.IsImageFile(arg) || File.Exists(arg))
        {
            HandleImageCommand(arg, images);
            return (false, false, true);
        }
    }

    if (trimmed == "web" || trimmed.StartsWith("web "))
    {
        if (HandleWebCommand(input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray()))
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
    Config.ModelSupportsVision = null;
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
    await CheckVisionAsync();
    history.Add(("system", $"[Switched to {Config.CurrentServer.Name} server]"));
}

async Task CheckVisionAsync()
{
    Config.ModelSupportsVision = await LlmClient.SupportsVisionAsync();
    var status = Config.ModelSupportsVision switch
    {
        true => Ansi.Green("yes"),
        false => Ansi.Dim("no"),
        null => Ansi.Yellow("unknown")
    };
    Console.WriteLine(Ansi.Dim("  👁  Image support: ") + status + "\n");
}

void HandleImageCommand(string arg, List<ImageAttachment> images)
{
    if (arg.Equals("clear", StringComparison.OrdinalIgnoreCase))
    {
        images.Clear();
        Console.WriteLine(Ansi.Dim("\n  Cleared attached images.\n"));
        return;
    }

    if (string.IsNullOrEmpty(arg))
    {
        if (images.Count == 0)
        {
            Console.WriteLine(Ansi.Dim("\n  No images attached. Usage: image <path>\n"));
            return;
        }

        Console.WriteLine(Ansi.Bold($"\n  📎 {images.Count} image(s) attached to your next request:"));
        foreach (var img in images)
            Console.WriteLine($"    {img.Name} {Ansi.Dim($"({img.Base64.Length * 3 / 4 / 1024:N0} KB)")}");
        Console.WriteLine();
        return;
    }

    if (Config.ModelSupportsVision == false)
    {
        Console.WriteLine(Ansi.Yellow($"\n  ⚠ {Config.Model} doesn't support images. Switch to a vision model to attach them.\n"));
        return;
    }

    try
    {
        var fullPath = Path.GetFullPath(arg);
        images.Add(ImageAttachment.Load(fullPath, Path.GetFileName(fullPath)));
        Console.WriteLine(Ansi.Green($"\n  ✓ Attached {Path.GetFileName(fullPath)}") + Ansi.Dim(" - it will be sent with your next request"));
        if (Config.ModelSupportsVision == null)
            Console.WriteLine(Ansi.Yellow($"  ⚠ Couldn't confirm {Config.Model} supports images - the server may reject it."));
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine(Ansi.Red($"\n  ✗ {ex.Message}\n"));
    }
}

/// <summary>
/// Handles "web ..." commands. Returns false for input that isn't a web command,
/// so "web development tips?" goes to the model as a message.
/// </summary>
bool HandleWebCommand(string[] args)
{
    if (args.Length == 0)
    {
        ShowWebSettings();
        return true;
    }

    var sub = args[0].ToLowerInvariant();

    if (args.Length == 1 && Enum.TryParse<WebMode>(sub, ignoreCase: true, out var mode) && !int.TryParse(sub, out _))
    {
        Config.WebMode = mode;
        Config.Save();
        Console.WriteLine(Ansi.Green($"\n  ✓ Web access: {mode.ToString().ToLowerInvariant()}\n"));
        return true;
    }

    if (args.Length == 2 && sub is "allow" or "block" or "remove")
    {
        var domain = WebAccess.NormalizeDomain(args[1]);
        if (!domain.Contains('.') && domain != "localhost")
            return false;

        if (sub == "allow")
            WebAccess.AddDomain(Config.WebWhitelist, Config.WebBlacklist, domain);
        else if (sub == "block")
            WebAccess.AddDomain(Config.WebBlacklist, Config.WebWhitelist, domain);
        else
        {
            Config.WebWhitelist.RemoveAll(d => d.Equals(domain, StringComparison.OrdinalIgnoreCase));
            Config.WebBlacklist.RemoveAll(d => d.Equals(domain, StringComparison.OrdinalIgnoreCase));
            Config.Save();
        }

        var verb = sub switch { "allow" => "whitelisted", "block" => "blacklisted", _ => "removed from both lists" };
        Console.WriteLine(Ansi.Green($"\n  ✓ {domain} {verb}\n"));
        return true;
    }

    if (args.Length == 2 && sub == "search" && (args[1].Contains("{query}") || args[1].Equals("default", StringComparison.OrdinalIgnoreCase)))
    {
        Config.WebSearchUrl = args[1].Equals("default", StringComparison.OrdinalIgnoreCase) ? Config.DefaultSearchUrl : args[1];
        Config.Save();
        Console.WriteLine(Ansi.Green($"\n  ✓ Search engine: {Config.WebSearchUrl}\n"));
        return true;
    }

    return false;
}

void ShowWebSettings()
{
    var modeHelp = Config.WebMode switch
    {
        WebMode.Off => "the model can't use the web",
        WebMode.Ask => "whitelist allowed, blacklist blocked, anything else asks you",
        WebMode.Whitelist => "only whitelisted sites (plus the search engine)",
        WebMode.Blacklist => "any site except blacklisted ones",
        _ => "any site",
    };
    string List(List<string> domains) => domains.Count == 0 ? Ansi.Dim("(none)") : string.Join(", ", domains);

    Console.WriteLine(Ansi.Bold("\n  🌐 Web access:"));
    Console.WriteLine($"    Mode:      {Config.WebMode.ToString().ToLowerInvariant()} {Ansi.Dim($"- {modeHelp}")}");
    Console.WriteLine($"    Whitelist: {List(Config.WebWhitelist)}");
    Console.WriteLine($"    Blacklist: {List(Config.WebBlacklist)}");
    Console.WriteLine($"    Search:    {Config.WebSearchUrl}");
    Console.WriteLine(Ansi.Dim("  Change with: web <off|ask|whitelist|blacklist|full>, web allow|block|remove <domain>\n"));
}

/// <summary>Copies the attached images to send, dropping them if the model is known not to accept images.</summary>
List<ImageAttachment> ImagesToSend(List<ImageAttachment> pending)
{
    if (pending.Count > 0 && Config.ModelSupportsVision == false)
    {
        Console.WriteLine(Ansi.Yellow($"  ⚠ {Config.Model} doesn't support images - dropping {pending.Count} attached image(s).\n"));
        pending.Clear();
    }
    return pending.ToList();
}

string ImageNote(List<ImageAttachment> images) =>
    images.Count > 0 ? $"\n\nAttached images: {string.Join(", ", images.Select(i => i.Name))}" : "";

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

async Task RunChatModeAsync(string task, List<(string role, string content)> history, List<ImageAttachment> pendingImages)
{
    var images = ImagesToSend(pendingImages);

    // Later turns only carry the image names; the image itself is sent once
    history.Add(("user", task + ImageNote(images)));

    var logFile = ConversationLogger.StartSession(task);
    Console.WriteLine(Ansi.Dim($"  📝 Logging to {logFile}\n"));

    var prompt = BuildChatPrompt(history);
    var system = Config.CurrentPersonality.ChatSystem + WebAccess.PromptInstructions;

    Console.WriteLine(Ansi.Dim("\n  💬 Stoat:\n"));
    Console.WriteLine(Ansi.Dim(new string('─', 60)));

    var response = await LlmClient.GenerateStreamAsync(prompt, system, images);

    // The model may ask for searches or pages; run them and let it answer with the results
    var lookups = new List<string>();
    var done = new HashSet<WebRequest>();
    for (int round = 0; round < Config.MaxWebRounds && Config.WebMode != WebMode.Off; round++)
    {
        var requests = Parser.ParseWebRequests(response).Where(done.Add).ToList();
        if (requests.Count == 0) break;

        var web = await WebAccess.RunAsync(requests);
        lookups.Add(web.Summary);
        prompt += $"{response}\n\nUser: {web.Text}\nUse these results to answer my last message.\nAssistant: ";

        Console.WriteLine(Ansi.Dim("\n  💬 Stoat:\n"));
        response = await LlmClient.GenerateStreamAsync(prompt, system, images);
    }
    pendingImages.Clear();

    Console.WriteLine(Ansi.Dim(new string('─', 60)));

    // Later turns get a note of what was looked up, not the full page text
    history.Add(("assistant", lookups.Count > 0 ? $"[Looked up: {string.Join("; ", lookups)}]\n{response}" : response));
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

async Task RunCodeModeAsync(string task, List<(string role, string content)> history, List<ImageAttachment> pendingImages)
{
    var cwd = Directory.GetCurrentDirectory();
    var images = ImagesToSend(pendingImages);

    var logFile = ConversationLogger.StartSession(task);
    Console.WriteLine(Ansi.Dim($"  📝 Logging to {logFile}\n"));

    // Phase 1: Send file tree, ask LLM which files it needs

    var tree = FileUtils.WalkDir(cwd);
    var phase1Prompt = $"Task: {task.Trim()}{ImageNote(images)}\n\nProject files:\n{string.Join("\n", tree)}";
    var phase1System = Config.CurrentPersonality.Phase1System + WebAccess.PromptInstructions;

    var fileList = new List<string>();
    var webResults = new StringBuilder();
    var done = new HashSet<WebRequest>();

    // The model may ask for web results before choosing files, e.g. search, then fetch a result.
    // Web results go to Phase 2 as well, so no extra call is needed there.
    for (int round = 1; ; round++)
    {
        var phase1Response = await LlmClient.GenerateStreamAsync(phase1Prompt, phase1System, images);
        var roundFiles = Parser.ParseFileList(phase1Response, tree);
        fileList = fileList.Union(roundFiles, StringComparer.OrdinalIgnoreCase).ToList();
        ConversationLogger.LogPhase1(phase1Prompt, phase1Response, roundFiles);

        var requests = Config.WebMode == WebMode.Off
            ? new List<WebRequest>()
            : Parser.ParseWebRequests(phase1Response).Where(done.Add).ToList();
        if (requests.Count == 0) break;

        var web = await WebAccess.RunAsync(requests);
        webResults.Append(web.Text);
        if (round >= Config.MaxWebRounds) break;

        Console.WriteLine(Ansi.Dim("\n  🌐 Sending web results back to Stoat...\n"));
        phase1Prompt += $"\n\n{web.Text}\nNow list the files you need. Add more SEARCH or FETCH lines only if you still need information.";
    }

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
    var (packed, phase2Images) = PackFiles(task, fileList, cwd, images, webResults.ToString());

    Console.WriteLine(Ansi.Cyan("\n  🐾 Stoat is working...\n"));
    Console.WriteLine(Ansi.Dim(new string('─', 60)));
    var response = await LlmClient.GenerateStreamAsync(packed, Config.CurrentPersonality.Phase2System, phase2Images);
    pendingImages.Clear();
    Console.WriteLine(Ansi.Dim(new string('─', 60)));

    FileBlock.SaveFileMD(response);

    var blocks = Parser.ParseFileBlocks(response, fileList, tree);

    // Models can look at images but can't write them - text would overwrite the image
    var imageBlocks = blocks.RemoveAll(b => ImageAttachment.IsImageFile(b.FilePath));
    if (imageBlocks > 0)
        Console.WriteLine(Ansi.Yellow($"\n  ⚠ Ignored {imageBlocks} image file(s) in the reply - Stoat can't write images."));

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

        // Keep the file's existing line endings
        var newline = Environment.NewLine;
        if (File.Exists(fullPath))
            newline = File.ReadAllText(fullPath).Contains("\r\n") ? "\r\n" : "\n";

        var backup = FileUtils.BackupFile(fullPath);
        if (backup != null)
            Console.WriteLine(Ansi.Dim($"    ↩ Backed up: {fb.FilePath}"));

        FileUtils.EnsureDir(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, fb.Content.Replace("\n", newline), Encoding.UTF8);
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

/// <summary>Builds the Phase 2 prompt. Image files the model asked for are attached rather than skipped.</summary>
(string prompt, List<ImageAttachment> images) PackFiles(string task, List<string> files, string cwd, List<ImageAttachment> attached, string webResults)
{
    var images = new List<ImageAttachment>(attached);
    var packed = new StringBuilder();
    packed.AppendLine($"Task: {task.Trim()}{ImageNote(attached)}");
    packed.AppendLine();

    if (webResults.Length > 0)
        packed.AppendLine(webResults);

    // Files are shown in the same markdown shape the model should reply with
    foreach (var f in files)
    {
        var fullPath = Path.Combine(cwd, f);

        if (ImageAttachment.IsImageFile(f) && File.Exists(fullPath))
        {
            if (Config.ModelSupportsVision == false)
            {
                packed.AppendLine($"{f} (image - this model can't view images)");
            }
            else
            {
                try
                {
                    images.Add(ImageAttachment.Load(fullPath, f));
                    packed.AppendLine($"{f} (image attached)");
                }
                catch (Exception ex)
                {
                    packed.AppendLine($"{f} (image - skipped: {ex.Message})");
                }
            }
            packed.AppendLine();
            continue;
        }

        var content = FileUtils.ReadFileContents(fullPath);

        if (content == null)
        {
            packed.AppendLine(File.Exists(fullPath) ? $"{f} (binary - skipped)" : $"{f} (new file - does not exist yet)");
            packed.AppendLine();
            continue;
        }

        // Use a fence longer than any backtick run in the file
        var longestRun = Regex.Matches(content, "`+").Select(m => m.Length).DefaultIfEmpty(0).Max();
        var fence = new string('`', Math.Max(3, longestRun + 1));

        packed.AppendLine(f);
        packed.AppendLine(fence + Path.GetExtension(f).TrimStart('.'));
        packed.AppendLine(content.TrimEnd('\r', '\n'));
        packed.AppendLine(fence);
        packed.AppendLine();
    }

    packed.AppendLine("Reply with each file you change: its path on its own line, then the complete file in a code block.");
    return (packed.ToString(), images);
}

// Windows ANSI support
[DllImport("kernel32.dll")]
static extern IntPtr GetStdHandle(int nStdHandle);

[DllImport("kernel32.dll")]
static extern bool GetConsoleMode(IntPtr h, out uint m);

[DllImport("kernel32.dll")]
static extern bool SetConsoleMode(IntPtr h, uint m);
