using System.Text;
using System.Text.Json;

namespace StoatTote;

internal static class OllamaClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private static object BuildBody(string prompt, string system, bool stream) => new
    {
        model = Config.Model,
        prompt,
        system,
        stream,
        options = new { temperature = 0, num_ctx = 32768 }
    };

    /// <summary>Non-streaming generate - returns full response.</summary>
    public static async Task<string> GenerateAsync(string prompt, string system = "")
    {
        var json = JsonSerializer.Serialize(BuildBody(prompt, system, stream: false));
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var res = await Http.PostAsync($"{Config.OllamaUrl}/api/generate", content);

        if (!res.IsSuccessStatusCode)
            throw new Exception($"Ollama error {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return StoatStandards.StandardizeText(doc.RootElement.GetProperty("response").GetString() ?? "");
    }

    /// <summary>Streaming generate - streams to console and returns full response.</summary>
    public static async Task<string> GenerateStreamAsync(string prompt, string system = "")
    {
        var json = JsonSerializer.Serialize(BuildBody(prompt, system, stream: true));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Config.OllamaUrl}/api/generate")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var res = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (!res.IsSuccessStatusCode)
            throw new Exception($"Ollama error {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");

        await using var stream = await res.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var response = new StringBuilder();
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("response", out var token))
                {
                    var text = token.GetString() ?? "";
                    Console.Write(text);
                    response.Append(text);
                }
            }
            catch { /* skip malformed lines */ }
        }

        Console.WriteLine();
        return StoatStandards.StandardizeText(response.ToString());
    }

    /// <summary>Ensures Ollama is running, starting it if needed.</summary>
    public static async Task<bool> EnsureRunningAsync()
    {
        if (await IsReachableAsync()) return true;

        Console.WriteLine(Ansi.Yellow($"  ⚠ Ollama not reachable at {Config.OllamaUrl} - attempting to start..."));
        if (!await TryStartAsync())
        {
            Console.WriteLine(Ansi.Red("  ✗ Could not start Ollama. Please start it manually.\n"));
            return false;
        }
        Console.WriteLine(Ansi.Green("  ✓ Ollama started.\n"));
        return true;
    }

    /// <summary>Returns available local models.</summary>
    public static async Task<List<string>> GetModelsAsync()
    {
        try
        {
            using var res = await Http.GetAsync($"{Config.OllamaUrl}/api/tags");
            if (!res.IsSuccessStatusCode) return [];

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("models")
                .EnumerateArray()
                .Select(m => m.GetProperty("name").GetString() ?? "")
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();
        }
        catch { return []; }
    }

    private static async Task<bool> IsReachableAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var res = await Http.GetAsync($"{Config.OllamaUrl}/api/tags", cts.Token);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static async Task<bool> TryStartAsync()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ollama", "serve")
            {
                UseShellExecute = true,
                CreateNoWindow = false,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Minimized
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine(Ansi.Red($"  ✗ Failed to launch: {ex.Message}"));
            return false;
        }

        // Poll for up to 10 seconds
        for (int i = 0; i < 15; i++)
        {
            await Task.Delay(700);
            if (await IsReachableAsync()) return true;
        }
        return false;
    }
}
