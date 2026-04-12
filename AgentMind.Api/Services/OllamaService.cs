using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentMind.Api.Services;

public class OllamaService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

    // 1. Updated Message class for the Chat API - Strictly preserving role/content structure
    public class OllamaMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; }    // "user" or "assistant"

        [JsonPropertyName("content")]
        public string Content { get; set; } // The actual text message
    }

    // 2. Persistent chat history (In-memory storage) to maintain identity like "Rony"
    private static readonly Dictionary<string, List<OllamaMessage>> _chatHistory = new();

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

        // Initialize history for new sessions if not already present in RAM
        if (!_chatHistory.ContainsKey(sessionId))
        {
            _chatHistory[sessionId] = new List<OllamaMessage>();
        }

        // Add the current user prompt to the persistent history before sending
        _chatHistory[sessionId].Add(new OllamaMessage { Role = "user", Content = prompt });

        // 3. CORRECT CHAT REQUEST BODY: Using 'messages' list instead of 'prompt'/'context'
        var requestBody = new
        {
            model = _config["OllamaConfig:ModelName"],
            messages = _chatHistory[sessionId], // The complete conversation thread
            stream = true
        };

        // Serialize with ignore null condition to keep the payload clean
        var jsonOptions = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        // Inside StreamChatAsync, right before sending the request
        var jsonPayload = JsonSerializer.Serialize(requestBody, jsonOptions);
        Console.WriteLine($"[DEBUG] Sending to Ollama: {jsonPayload}");

        var content = new StringContent(JsonSerializer.Serialize(requestBody, jsonOptions), Encoding.UTF8, "application/json");

        // Strictly using the "api/chat" endpoint for stateful conversations
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/chat") { Content = content };

        // ResponseHeadersRead allows immediate streaming of the model's output
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        response.EnsureSuccessStatusCode();

        using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(responseStream);

        // StringBuilder accumulates the full response to save it back into history
        StringBuilder fullAssistantResponse = new StringBuilder();

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;

            // This option tells the JSON parser to be flexible with casing
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            // Deserializing using the Chat-specific response model
            var chunk = JsonSerializer.Deserialize<OllamaChatResponse>(line, options);

            if (chunk?.Message != null && !string.IsNullOrEmpty(chunk.Message.Content))
            {
                // Capture each text fragment for the internal memory
                fullAssistantResponse.Append(chunk.Message.Content);
                yield return chunk.Message.Content;
            }

            if (chunk?.Done == true)
            {
                // AT THIS POINT: Add the assistant's complete reply to the session history
                _chatHistory[sessionId].Add(new OllamaMessage { Role = "assistant", Content = fullAssistantResponse.ToString() });

                // PERFORMANCE CALCULATION: Re-calculating tokens per second for benchmarking
                if (chunk.EvalCount.HasValue && chunk.EvalDuration.HasValue && chunk.EvalDuration > 0)
                {
                    double durationInSeconds = chunk.EvalDuration.Value / 1_000_000_000.0;
                    double tokensPerSecond = chunk.EvalCount.Value / durationInSeconds;

                    Console.WriteLine($"\n--- Chat Performance Statistics ---");
                    Console.WriteLine($"Calculation: {chunk.EvalCount} tokens / {durationInSeconds:F2}s");
                    Console.WriteLine($"Result: {tokensPerSecond:F2} tokens per second");
                    Console.WriteLine("-----------------------------------\n");
                }
            }
        }
    }

    // Dedicated response model for the /api/chat endpoint mapping
    // Inside OllamaService.cs - At the end of the file
    // I am only adding [JsonPropertyName] to ensure the response maps correctly from lowercase JSON
    private class OllamaChatResponse
    {
        [JsonPropertyName("model")] // Added to match Ollama's lowercase JSON output
        public string Model { get; set; }

        [JsonPropertyName("message")] // CRITICAL: This was missing and caused Message to be null in your debug session
        public OllamaMessage Message { get; set; }

        [JsonPropertyName("done")] // Added to ensure 'done' status is correctly captured
        public bool Done { get; set; }

        [JsonPropertyName("eval_count")] // Added to match Ollama's performance metrics
        public int? EvalCount { get; set; }

        [JsonPropertyName("eval_duration")] // Added to match Ollama's performance metrics
        public long? EvalDuration { get; set; }
    }
}