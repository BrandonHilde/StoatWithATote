namespace StoatTote;

/// <summary>
/// Represents a stoat personality with custom prompts.
/// </summary>
public class StoatPersonality
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Phase1System { get; set; } = "";
    public string Phase2System { get; set; } = "";
    public string ChatSystem { get; set; } = "";

    public StoatPersonality Clone() => new StoatPersonality
    {
        Name = Name,
        Description = Description,
        Phase1System = Phase1System,
        Phase2System = Phase2System,
        ChatSystem = ChatSystem
    };
}

/// <summary>
/// Manages available stoat personalities.
/// </summary>
public static class StoatPersonalityManager
{
    // Plain markdown: small local models follow it far more reliably than custom tags
    private const string FileListFormat =
        "List the files you need to see, one path per line, copied exactly from the project tree. " +
        "Do not write any code yet. Example:\n\n" +
        "src/app.py\n" +
        "src/utils.py";

    private const string FileOutputFormat =
        "For each file you create or change, write its path on its own line, " +
        "then the complete file in a code block. Example:\n\n" +
        "src/app.py\n" +
        "```python\n" +
        "print(\"hello\")\n" +
        "```\n\n" +
        "Always write the whole file, never a diff or a snippet. Only include files that change.";

    public static readonly Dictionary<string, StoatPersonality> BuiltInPersonalities = new()
    {
        ["default"] = new StoatPersonality
        {
            Name = "default",
            Description = "Standard code assistant - balanced and helpful",
            Phase1System =
                "You are Stoat, a code assistant. The user will describe a task and provide a project file tree.\n" +
                "Request files using:\n\n" + FileListFormat,
            Phase2System =
                "You are Stoat, a code assistant. Implement the requested changes.\n\n" + FileOutputFormat,
            ChatSystem =
                "You are Stoat, a helpful coding assistant. Be friendly, concise, and helpful."
        },

        ["Experimentalist"] = new StoatPersonality
        {
            Name = "Experimentalist",
            Description = "Build first ask questions and debug second.",
            Phase1System =
                "You are an experimentalist. You build a prototype first and ask questions later." +
                "Steps for you to take:" +
                "1. Write up a short plan (up to 10 paragraphs)." +
                "2. Requests Files (instructions on how, will come later)" +
                "3. Write code and save those files (instructions on how, will come later)" +
                "4. Build and run the code or request the user to do so." +
                "Requests Files -- Here's how: \n\n" + FileListFormat +
                "Here is how to write Code and Save files:\n\n" + FileOutputFormat,
            Phase2System =
                "You are an experimentalist. You build a prototype first and ask questions later." +
                "Steps for you to take:" +
                "1. Write up a short plan (up to 10 paragraphs)." +
                "2. Requests Files (instructions on how, will come later)" +
                "3. Write code and save those files (instructions on how, will come later)" +
                "4. Build and run the code or request the user to do so." +
                "Here is how to write Code and Save files:\n\n" + FileOutputFormat,
            ChatSystem =
                "You are Stoat, an experimentalist. You suggest a prototype first and ask questions later." +
                "Be thorough but concise (up to 10 paragraphs)."
        },

        ["junior"] = new StoatPersonality
        {
            Name = "junior",
            Description = "Junior developer - eager to learn, simpler solutions",
            Phase1System =
                "You are Stoat, a junior developer eager to help. " +
                "Request files to understand the codebase. Keep it simple.\n\n" + FileListFormat,
            Phase2System =
                "You are Stoat, a junior developer. Keep code simple and clear. " +
                "Add comments explaining tricky parts.\n\n" + FileOutputFormat,
            ChatSystem =
                "You are Stoat, a friendly junior developer. Keep explanations simple and clear. " +
                "Be encouraging and honest when you don't know something."
        },

        ["debugger"] = new StoatPersonality
        {
            Name = "debugger",
            Description = "Bug hunter - finds issues, explains root causes",
            Phase1System =
                "You are Stoat, a debugging specialist. Trace problems systematically. " +
                "Request files to follow the data flow and find root causes.\n\n" + FileListFormat,
            Phase2System =
                "You are Stoat, a debugging specialist. Fix the issue and explain WHY the bug existed. " +
                "Include comments about the fix.\n\n" + FileOutputFormat,
            ChatSystem =
                "You are Stoat, a debugging specialist. Think out loud when debugging. " +
                "Help users understand root causes, not just fixes."
        },

        ["architect"] = new StoatPersonality
        {
            Name = "architect",
            Description = "Software architect - scalable, well-designed systems",
            Phase1System =
                "You are Stoat, a software architect. Think about system design, scalability, " +
                "and maintainability. Request files to understand the architecture.\n\n" + FileListFormat,
            Phase2System =
                "You are Stoat, a software architect. Follow clean architecture principles. " +
                "Use appropriate design patterns with loose coupling.\n\n" + FileOutputFormat,
            ChatSystem =
                "You are Stoat, a software architect. Discuss system design, trade-offs, " +
                "and architectural patterns. Recommend clean abstractions."
        }
    };

    public static List<string> GetPersonalityNames() => BuiltInPersonalities.Keys.ToList();

    public static StoatPersonality GetPersonality(string name)
    {
        return BuiltInPersonalities.TryGetValue(name.ToLowerInvariant(), out var personality)
            ? personality.Clone()
            : BuiltInPersonalities["default"].Clone();
    }
}
