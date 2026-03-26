
# Stoat With A Tote

    Stoat is a small agentic cli, designed to reduce token use. Makes local LLM's viable.

## Requirements
    1. dotnet
    2. ollama

## Operating Modes

Stoat supports two operating modes:

### Code Mode (Default)
Analyze project files, make code changes, and generate new code. The CLI will:
- Explore your project structure
- Read relevant files
- Propose and execute code changes
- Create backups before modifying files

### Chat Mode
Conversational assistance without file operations. Great for:
- Explaining code concepts
- Debugging help
- General programming questions
- Getting recommendations

## Switching Modes

You can switch between modes by typing commands:

- `code` or `mode code` - Switch to Code Mode
- `chat` or `mode chat` - Switch to Chat Mode
- `help` or `?` - Show available commands
- `export` - Export conversation as markdown file
- `quit`, `exit`, or `q` - Exit the application

When you switch modes, the conversation context is preserved and passed to the LLM so it can maintain awareness of what you were discussing.

## Commands
Build:
```
dotnet build stoat.csproj 2>&1
```
Run:
```
dotnet run --project stoat.csproj
```
Publish:
```
dotnet publish stoat.csproj -c Release -o .\publish 2>&1
```
Start:
```
stoat
```
