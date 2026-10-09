using System.Text;
using System.Text.Json;

namespace StoatTote;

internal static class LlmClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private static string BaseUrl => Config.CurrentServer.Url;
    private static ServerType ServerType => Config.CurrentServer.Type;

    private static string? ApiKey => ServerType == ServerType.OpenRouter
        ? (Config.OpenRouterApiKey ?? Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"))
        : null;

    private static bool IsOpenAiCompatible => ServerType is ServerType.LlamaCpp or ServerType.OpenRouter;

    private static readonly JsonSerializerOptions BodyJsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static object BuildOllamaBody(string prompt, string system, bool stream, IReadOnlyList<ImageAttachment>? images) => new
    {
        model = Config.Model,
        prompt,
        system,
        stream,
        images = images is { Count: > 0 } ? images.Select(i => i.Base64).ToArray() : null,
        options = new { temperature = 0, num_ctx = 32768 }
    };

    private static object BuildOpenAiBody(string prompt, string system, bool stream, IReadOnlyList<ImageAttachment>? images)
    {
        var messages = new List<object>();
        if (!string.IsNullOrEmpty(system))
            messages.Add(new { role = "system", content = system });

        if (images is { Count: > 0 })
        {
            var parts = new List<object> { new { type = "text", text = prompt } };
            parts.AddRange(images.Select(i => new { type = "image_url", image_url = new { url = i.DataUrl } }));
            messages.Add(new { role = "user", content = parts });
        }
        else
        {
            messages.Add(new { role = "user", content = prompt });
        }

        return new
        {
            model = Config.Model,
            messages,
            temperature = 0,
            stream
        };
    }

    private static string SerializeBody(string prompt, string system, bool stream, IReadOnlyList<ImageAttachment>? images)
    {
        return IsOpenAiCompatible
            ? JsonSerializer.Serialize(BuildOpenAiBody(prompt, system, stream, images), BodyJsonOptions)
            : JsonSerializer.Serialize(BuildOllamaBody(prompt, system, stream, images), BodyJsonOptions);
    }

    private static string GenerateEndpoint => ServerType switch
    {
        ServerType.OpenRouter => "/chat/completions",
        ServerType.LlamaCpp => "/v1/chat/completions",
        _ => "/api/generate"
    };

    private static HttpRequestMessage BuildRequest(HttpMethod method, string url, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (content != null)
            request.Content = content;
        if (!string.IsNullOrEmpty(ApiKey))
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
        return request;
    }

    private static void WriteToken(string text, StringBuilder response, ref bool inThinking)
    {
        int i = 0;
        while (i < text.Length)
        {
            if (!inThinking)
            {
                var nextThink = text.IndexOf("\u003cthink\u003e", i, StringComparison.Ordinal);
                var nextThinking = text.IndexOf("\u003cthinking\u003e", i, StringComparison.Ordinal);
                var start = nextThink >= 0 ? nextThink : nextThinking;
                if (nextThink >= 0 && nextThinking >= 0)
                    start = Math.Min(nextThink, nextThinking);
                else if (nextThinking >= 0)
                    start = nextThinking;

                if (start >= 0)
                {
                    if (start > i)
                        Console.Write(text.Substring(i, start - i));
                    inThinking = true;
                    i = start;
                }
                else
                {
                    Console.Write(text.Substring(i));
                    break;
                }
            }
            else
            {
                var endThink = text.IndexOf("\u003c/think\u003e", i, StringComparison.Ordinal);
                var endThinking = text.IndexOf("\u003c/thinking\u003e", i, StringComparison.Ordinal);
                var end = endThink >= 0 ? endThink : endThinking;
                if (endThink >= 0 && endThinking >= 0)
                    end = Math.Min(endThink, endThinking);
                else if (endThinking >= 0)
                    end = endThinking;

                if (end >= 0)
                {
                    var thinkContent = text.Substring(i, end - i);
                    if (!string.IsNullOrEmpty(thinkContent))
                        Console.Write(Ansi.Dim(thinkContent));
                    inThinking = false;
                    var endTag = endThink == end ? "\u003c/think\u003e" : "\u003c/thinking\u003e";
                    Console.Write(Ansi.Dim(text.Substring(end, endTag.Length)));
                    i = end + endTag.Length;
                }
                else
                {
                    Console.Write(Ansi.Dim(text.Substring(i)));
                    break;
                }
            }
        }
        response.Append(text);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Writes separately-streamed reasoning, wrapped in think tags so the parser can skip it.</summary>
    private static void WriteReasoning(string text, StringBuilder response, ref bool inReasoning)
    {
        if (!inReasoning)
        {
            response.Append("<think>");
            inReasoning = true;
        }
        Console.Write(Ansi.Dim(text));
        response.Append(text);
    }

    private static void EndReasoning(StringBuilder response, ref bool inReasoning)
    {
        if (!inReasoning) return;
        response.Append("</think>");
        inReasoning = false;
    }

    /// <summary>Non-streaming generate - returns full response.</summary>
    public static async Task<string> GenerateAsync(string prompt, string system = "", IReadOnlyList<ImageAttachment>? images = null)
    {
        var json = SerializeBody(prompt, system, stream: false, images);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = BuildRequest(HttpMethod.Post, $"{BaseUrl}{GenerateEndpoint}", content);
        using var res = await Http.SendAsync(request);

        if (!res.IsSuccessStatusCode)
            throw new Exception($"{Config.CurrentServer.Name} error {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");

        var responseText = await res.Content.ReadAsStringAsync();

        if (IsOpenAiCompatible)
        {
            using var doc = JsonDocument.Parse(responseText);
            var choice = doc.RootElement.GetProperty("choices")[0];
            var text = choice.GetProperty("message").GetProperty("content").GetString() ?? "";
            return StoatStandards.StandardizeText(text);
        }
        else
        {
            using var doc = JsonDocument.Parse(responseText);
            return StoatStandards.StandardizeText(doc.RootElement.GetProperty("response").GetString() ?? "");
        }
    }

    /// <summary>Streaming generate - streams to console and returns full response.</summary>
    public static async Task<string> GenerateStreamAsync(string prompt, string system = "", IReadOnlyList<ImageAttachment>? images = null)
    {
        var json = SerializeBody(prompt, system, stream: true, images);
        using var request = BuildRequest(HttpMethod.Post, $"{BaseUrl}{GenerateEndpoint}");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        Console.WriteLine(Ansi.Dim($"  [DEBUG] Sending to {Config.CurrentServer.Name} ({json.Length:N0} bytes)..."));

        using var res = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (!res.IsSuccessStatusCode)
            throw new Exception($"{Config.CurrentServer.Name} error {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");

        await using var stream = await res.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var response = new StringBuilder();
        bool inThinking = false;
        bool inReasoning = false;

        if (IsOpenAiCompatible)
        {
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data:")) continue;

                var data = line.Substring(5).Trim();
                if (data == "[DONE]") continue;

                try
                {
                    using var doc = JsonDocument.Parse(data);
                    if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        if (choices[0].TryGetProperty("delta", out var delta))
                        {
                            // llama.cpp sends reasoning_content, OpenRouter sends reasoning
                            var reasoning = GetString(delta, "reasoning_content") ?? GetString(delta, "reasoning");
                            if (!string.IsNullOrEmpty(reasoning))
                                WriteReasoning(reasoning, response, ref inReasoning);

                            var text = GetString(delta, "content");
                            if (!string.IsNullOrEmpty(text))
                            {
                                EndReasoning(response, ref inReasoning);
                                WriteToken(text, response, ref inThinking);
                            }
                        }
                    }
                }
                catch { /* skip malformed lines */ }
            }
        }
        else
        {
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);

                    // Newer Ollama versions send reasoning separately as "thinking"
                    var reasoning = GetString(doc.RootElement, "thinking");
                    if (!string.IsNullOrEmpty(reasoning))
                        WriteReasoning(reasoning, response, ref inReasoning);

                    var text = GetString(doc.RootElement, "response");
                    if (!string.IsNullOrEmpty(text))
                    {
                        EndReasoning(response, ref inReasoning);
                        WriteToken(text, response, ref inThinking);
                    }
                }
                catch { /* skip malformed lines */ }
            }
        }

        EndReasoning(response, ref inReasoning);
        Console.WriteLine();
        return StoatStandards.StandardizeText(response.ToString());
    }

    /// <summary>Ensures the server is running, attempting to start Ollama if needed.</summary>
    public static async Task<bool> EnsureRunningAsync()
    {
        if (await IsReachableAsync()) return true;

        if (ServerType == ServerType.Ollama)
        {
            Console.WriteLine(Ansi.Yellow($"  \u26a0 Ollama not reachable at {BaseUrl} - attempting to start..."));
            if (!await TryStartOllamaAsync())
            {
                Console.WriteLine(Ansi.Red("  \u2717 Could not start Ollama. Please start it manually.\n"));
                return false;
            }
            Console.WriteLine(Ansi.Green("  \u2713 Ollama started.\n"));
            return true;
        }
        else if (ServerType == ServerType.OpenRouter)
        {
            Console.WriteLine(Ansi.Red($"  \u2717 OpenRouter not reachable at {BaseUrl}. Check your internet connection and API key.\n"));
            return false;
        }
        else
        {
            Console.WriteLine(Ansi.Red($"  \u2717 llama.cpp server not reachable at {BaseUrl}. Please start it manually.\n"));
            return false;
        }
    }

    /// <summary>Returns available models from the server.</summary>
    public static async Task<List<string>> GetModelsAsync()
    {
        try
        {
            if (ServerType == ServerType.OpenRouter)
            {
                // OpenRouter uses user-managed preferred models instead of listing all
                return [];
            }

            if (IsOpenAiCompatible)
            {
                using var request = BuildRequest(HttpMethod.Get, $"{BaseUrl}/v1/models");
                using var res = await Http.SendAsync(request);
                if (!res.IsSuccessStatusCode) return [];

                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("data")
                    .EnumerateArray()
                    .Select(m => m.GetProperty("id").GetString() ?? "")
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
            }
            else
            {
                using var res = await Http.GetAsync($"{BaseUrl}/api/tags");
                if (!res.IsSuccessStatusCode) return [];

                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("models")
                    .EnumerateArray()
                    .Select(m => m.GetProperty("name").GetString() ?? "")
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();
            }
        }
        catch { return []; }
    }

    /// <summary>
    /// Asks the server whether the current model accepts images.
    /// Returns null when the server doesn't say (older versions, network errors).
    /// </summary>
    public static async Task<bool?> SupportsVisionAsync()
    {
        try
        {
            switch (ServerType)
            {
                case ServerType.Ollama:
                {
                    var body = JsonSerializer.Serialize(new { model = Config.Model });
                    using var res = await Http.PostAsync($"{BaseUrl}/api/show", new StringContent(body, Encoding.UTF8, "application/json"));
                    if (!res.IsSuccessStatusCode) return null;

                    using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array)
                        return caps.EnumerateArray().Any(c => c.GetString() == "vision");

                    // Older Ollama: vision models ship a projector
                    return doc.RootElement.TryGetProperty("projector_info", out _) ? true : null;
                }

                case ServerType.LlamaCpp:
                {
                    // One model per server; vision needs the server started with --mmproj
                    using var request = BuildRequest(HttpMethod.Get, $"{BaseUrl}/props");
                    using var res = await Http.SendAsync(request);
                    if (!res.IsSuccessStatusCode) return null;

                    using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("modalities", out var modalities)
                        && modalities.TryGetProperty("vision", out var vision)
                        && vision.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        return vision.GetBoolean();
                    return null;
                }

                case ServerType.OpenRouter:
                {
                    using var request = BuildRequest(HttpMethod.Get, $"{BaseUrl}/models");
                    using var res = await Http.SendAsync(request);
                    if (!res.IsSuccessStatusCode) return null;

                    using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                    foreach (var model in doc.RootElement.GetProperty("data").EnumerateArray())
                    {
                        if (GetString(model, "id") != Config.Model) continue;

                        if (model.TryGetProperty("architecture", out var arch)
                            && arch.TryGetProperty("input_modalities", out var inputs)
                            && inputs.ValueKind == JsonValueKind.Array)
                            return inputs.EnumerateArray().Any(m => m.GetString() == "image");
                        return null;
                    }
                    return null;
                }
            }
        }
        catch { /* treat as unknown */ }

        return null;
    }

    private static async Task<bool> IsReachableAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var endpoint = IsOpenAiCompatible ? "/models" : "/api/tags";
            if (ServerType == ServerType.LlamaCpp)
                endpoint = "/v1/models";

            using var request = BuildRequest(HttpMethod.Get, $"{BaseUrl}{endpoint}");
            using var res = await Http.SendAsync(request, cts.Token);
            return res.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static async Task<bool> TryStartOllamaAsync()
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
            Console.WriteLine(Ansi.Red($"  \u2717 Failed to launch: {ex.Message}"));
            return false;
        }

        for (int i = 0; i < 15; i++)
        {
            await Task.Delay(700);
            if (await IsReachableAsync()) return true;
        }
        return false;
    }
}
