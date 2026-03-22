
# Stoat With A Tote

    Stoat is a small agentic cli, designed to reduce token use. Makes local LLM's viable.

## Reqirements
    1. dotnet
    2. ollama

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