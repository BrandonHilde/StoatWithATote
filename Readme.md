
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
- `image <path>` - Attach an image (png, jpg, gif, webp) to your next request
- `image` - List attached images, `image clear` to remove them
- `web` - Show web access settings
- `web <off|ask|whitelist|blacklist|full>` - Set what the model may do on the web
- `web allow|block|remove <domain>` - Edit the whitelist / blacklist
- `web search <url>` - Search engine URL containing `{query}`, or `default`
- `quit`, `exit`, or `q` - Exit the application

When you switch modes, the conversation context is preserved and passed to the LLM so it can maintain awareness of what you were discussing.

## Images

After you pick a model, Stoat asks the server whether it accepts images and shows `Image support: yes / no / unknown`.

- **Ollama**: vision models such as `qwen2.5vl` or `gemma3` report it automatically.
- **llama.cpp**: start the server with `--mmproj <projector.gguf>` for the model.
- **OpenRouter**: read from the model's listed input types.

Attach an image with `image <path>` (dragging a file into the terminal works). It is sent with your next request, then cleared. In code mode, image files the model asks for from the project are sent as images too. Images use a lot of tokens, so Stoat only sends them when you attach or approve them.

## Web Access

The model decides when it needs the web. It writes lines like these in its reply:

```
SEARCH: dotnet 8 json source generator
FETCH: https://learn.microsoft.com/en-us/dotnet/
```

Stoat runs them, turns pages into plain text (up to 5,000 characters each), and sends the results back. The model can do up to 3 rounds (for example: search, then fetch a result), with up to 3 requests per round. In code mode the results also go into Phase 2.

You control what's allowed with `web <mode>`:

| Mode | Behavior |
|---|---|
| `off` | No web access; the model isn't told about it |
| `ask` (default) | Whitelisted sites allowed, blacklisted blocked, anything else asks you (`a` = always allow, `b` = always block) |
| `whitelist` | Only whitelisted sites, plus the search engine |
| `blacklist` | Any site except blacklisted ones |
| `full` | Any site |

Domains match their subdomains (`github.com` covers `docs.github.com`). Every redirect is checked too. Local and private network addresses (localhost, 192.168.x.x, ...) are always blocked unless you whitelist them.

Searches use DuckDuckGo's HTML page by default, with no key needed. For a self-hosted SearXNG: `web search http://localhost:8888/search?q={query}&format=json` (and `web allow localhost`).

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
Linux: dotnet publish stoat.csproj -c Release -o .\publish --runtime linux-x64   
```
Start:
```
stoat
```
