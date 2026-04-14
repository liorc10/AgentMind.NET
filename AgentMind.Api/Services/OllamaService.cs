using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentMind.Api.Services;

public class OllamaService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

    // Shared JsonSerializerOptions instance - case-insensitive to handle Ollama's lowercase JSON fields
    private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    // Per-session numeric context returned by the Generate API.
    // Ollama compresses the conversation history into this integer array,
    // which replaces the role/content message list used by the Chat API.
    private static readonly Dictionary<string, List<long>> _contextStore = new();

    public OllamaService(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    // [EnumeratorCancellation] ensures that the cancellation token is correctly bound
    // to the asynchronous iteration from the Controller.
    public async IAsyncEnumerable<string> StreamChatAsync(string prompt, string sessionId, [EnumeratorCancellation] CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OllamaClient");

        // Retrieve the existing numeric context for this session, if any.
        // On the first call the context will be null, which tells Ollama to start a fresh conversation.
        _contextStore.TryGetValue(sessionId, out var currentContext);

        Console.WriteLine($"[DEBUG] Session '{sessionId}' — context tokens carried forward: {currentContext?.Count ?? 0}");

        // The system prompt anchors the model's behaviour for the entire conversation.
        // Without it, DeepSeek (a coding-focused model) does not retain conversational facts
        // such as the user's name across turns, even when the context array is present.
        var systemPrompt = _config["OllamaConfig:SystemPrompt"];

        // Build the Generate API request body.
        // 'context' carries the compressed conversation history as a raw integer array.
        // 'system'  anchors conversational memory — required for DeepSeek to recall user facts.
        // 'num_ctx' sets the model's context window. DeepSeek's Ollama default is only 2048 tokens,
        // which fills up after ~2 messages and causes silent memory loss. 8192 is a safe minimum.
        var requestBody = new
        {
            model = _config["OllamaConfig:ModelName"],
            //system = systemPrompt,
            prompt,
            context = currentContext, // null on first turn; populated on subsequent turns
            stream = true,
            options = new
            {
                num_ctx = 8192,    // Sets the token window size
                temperature = 0.7  // Optional: Adjusts randomness of the model
            }
        };

        // Serialize with ignore-null so the 'context' field is omitted on the first request
        var serializeOptions = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var jsonPayload = JsonSerializer.Serialize(requestBody, serializeOptions);
        Console.WriteLine($"[DEBUG] Sending to Ollama Generate API: {jsonPayload}");

        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        // Targeting the Generate API endpoint instead of the Chat API
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/generate") { Content = content };

        // ResponseHeadersRead allows immediate streaming of the model's output
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        response.EnsureSuccessStatusCode();

        using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(responseStream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Deserializing using the Generate-specific response model with case-insensitive mapping
            var chunk = JsonSerializer.Deserialize<OllamaGenerateResponse>(line, _jsonOptions);

            // Stream the 'response' field (Generate API) instead of 'message.content' (Chat API)
            if (!string.IsNullOrEmpty(chunk?.Response))
            {
                yield return chunk.Response;
            }

            if (chunk?.Done == true)
            {
                // MEMORY UPDATE: Persist the returned context integer array so the next request
                // carries the full conversation history in compressed numeric form.
                if (chunk.Context is { Count: > 0 })
                {
                    _contextStore[sessionId] = chunk.Context;
                    Console.WriteLine($"[DEBUG] Session '{sessionId}' — context updated, total tokens stored: {chunk.Context.Count}");
                }

                // PERFORMANCE CALCULATION: Re-calculating tokens per second for benchmarking
                if (chunk.EvalCount.HasValue && chunk.EvalDuration.HasValue && chunk.EvalDuration > 0)
                {
                    double durationInSeconds = chunk.EvalDuration.Value / 1_000_000_000.0;
                    double tokensPerSecond = chunk.EvalCount.Value / durationInSeconds;

                    Console.WriteLine($"\n--- Generate Performance Statistics ---");
                    Console.WriteLine($"Calculation: {chunk.EvalCount} tokens / {durationInSeconds:F2}s");
                    Console.WriteLine($"Result: {tokensPerSecond:F2} tokens per second");
                    Console.WriteLine("---------------------------------------\n");
                }
            }
        }
    }

    // Dedicated response model for the /api/generate endpoint.
    // 'response' carries each streamed text fragment; 'context' is the compressed memory array.
    private class OllamaGenerateResponse
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("response")] // Text fragment streamed token by token
        public string Response { get; set; }

        [JsonPropertyName("done")] // True on the final chunk of each response
        public bool Done { get; set; }

        [JsonPropertyName("context")] // Numeric token array representing conversation history
        public List<long> Context { get; set; }

        [JsonPropertyName("eval_count")] // Total tokens generated - used for performance benchmarking
        public int? EvalCount { get; set; }

        [JsonPropertyName("eval_duration")] // Generation time in nanoseconds - used for benchmarking
        public long? EvalDuration { get; set; }
    }
}