using AgentMind.Api.Interfaces;
using AgentMind.Api.Models;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentMind.Api.Services;

/* * OllamaService.cs
 * 
 * This service orchestrates all interactions with the Ollama API.
 * It handles three core phases:
 * 1. Embeddings Generation
 * 2. Intent Routing & Persona Assignment
 * 3. Augmented Streaming Response
 * 
 * The service uses strongly-typed models from OllamaModels.cs to ensure
 * correct serialization and deserialization of API requests/responses.
 * 
 * It also includes helper methods like LogPerformance for monitoring.
 */


//the following url is Qdrant Vector DB endpoint
//http://localhost:6333/dashboard#/collections
//https://github.com/ollama/ollama/blob/main/docs/api.md

public class OllamaService : IOllamaService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly IVectorService _vectorService;

    public OllamaService(IHttpClientFactory httpClientFactory, IConfiguration config, IVectorService vectorService)
    {
        _httpClientFactory = httpClientFactory;
        _vectorService = vectorService; // Injecting our new Vector Database service
        _config = config;
    }

    // --- PHASE 1: EMBEDDINGS GENERATION ---
    /* * Converts raw text into a numerical vector (float array).
     * This vector is used to perform semantic searches in the Vector DB.
     * We call the /api/embeddings endpoint specifically for this task.
     */
    public async Task<float[]> GetEmbeddingsAsync(string text, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("OllamaClient");

        var requestBody = new
        {
            model = _config["OllamaConfig:EmbeddingModel"] ?? "all-minilm",
            prompt = text
        };

        // PostAsJsonAsync handles the serialization of the request body
        var response = await client.PostAsJsonAsync("api/embeddings", requestBody, ct);
        response.EnsureSuccessStatusCode();

        // Deserializing into the model we created in OllamaModels.cs
        var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(ct);
        return result?.Embedding ?? Array.Empty<float>();
    }

    // --- PHASE 2: INTENT ROUTING & PERSONA ---
    /* * Uses the LLM to analyze the user's query and decide the context.
     * It returns a ProjectId for Vector DB filtering and a System Instruction.
     * We force JSON output to ensure the C# code can parse the decision.
     */
    public async Task<RoutingResult> GetRoutingInfoAsync(string userQuery, string categoriesList, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("OllamaClient");

        string routingPrompt = $@"
            Analyze the following user query: '{userQuery}'
            Available Categories/Projects: {categoriesList}
            Your task:
            1. Identify the most relevant ProjectId.
            2. Write a one-sentence 'System' persona instruction for this context.
            Return ONLY a valid JSON object: {{ ""ProjectId"": int, ""SystemInstruction"": ""string"" }}";

        var requestBody = new
        {
            model = _config["OllamaConfig:ModelName"],
            prompt = routingPrompt,
            format = "json",
            stream = false
        };

        var response = await client.PostAsJsonAsync("api/generate", requestBody, ct);
        response.EnsureSuccessStatusCode();

        var jsonString = await response.Content.ReadAsStringAsync(ct);

        // Case-insensitive deserialization to handle potential LLM casing issues
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<RoutingResult>(jsonString, options);
    }

    // --- PHASE 3: AUGMENTED STREAMING ---
    /* * This method performs the final generation (RAG).
     * It combines the System Instruction, the facts from the Vector DB, and the User query.
     * By wrapping everything in one prompt, we avoid the 'Double BOS' token issue.
     */
    // [EnumeratorCancellation] ensures that the cancellation token is correctly bound 
    // to the asynchronous iteration from the Controller.
    public async IAsyncEnumerable<string> RetrieveAugmentedResponseAsync(
       string prompt,
       string systemInstruction,
       string knowledgeBaseMatchResults,
       List<string>? history,
       [EnumeratorCancellation] CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("OllamaClient");

        // Building the final augmented prompt
        // We strictly use a single string to avoid 'Double BOS' issues in Ollama
        StringBuilder finalPrompt = new StringBuilder();

        finalPrompt.AppendLine($"### SYSTEM: {systemInstruction}");
        // Injecting chat history if available to maintain session context
        if (history != null && history.Any())
        {
            finalPrompt.AppendLine("### PREVIOUS CONVERSATION HISTORY:");
            foreach (var message in history)
            {
                finalPrompt.AppendLine(message);
            }
        }
        finalPrompt.AppendLine("### REFERENCE MATERIAL FROM KNOWLEDGE BASE:");
        finalPrompt.AppendLine(knowledgeBaseMatchResults); // This is the 'context' found in the DB
        finalPrompt.AppendLine($"### USER QUESTION: {prompt}");
        finalPrompt.AppendLine("### ASSISTANT RESPONSE:");

        var requestBody = new
        {
            model = _config["OllamaConfig:ModelName"],
            prompt = finalPrompt.ToString(),
            stream = true
        };

        // Serialize with ignore null condition to keep the payload clean
        var jsonOptions = new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        // Prepare the final JSON payload for the /api/generate endpoint.
        // This contains the combined 'system + context + user' prompt to avoid token collisions.
        var jsonPayload = JsonSerializer.Serialize(requestBody, jsonOptions);

        Console.WriteLine($"[DEBUG] Sending to Ollama: {jsonPayload}");

        // Using HttpRequestMessage for granular control over the request
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/generate")
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        // ResponseHeadersRead allows immediate streaming of the model's output
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        response.EnsureSuccessStatusCode();

        using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(responseStream);

        // StringBuilder accumulates the full response to save it back into history
        StringBuilder fullAssistantResponse = new StringBuilder();

        // This option tells the JSON parser to be flexible with casing
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };


        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Mapping the generic /api/generate response to a dynamic or specific object
            var chunk = JsonSerializer.Deserialize<OllamaGenerateResponse>(line, options);
            if (chunk?.Response != null)
            {
                // Capture each text fragment for the internal memory
                fullAssistantResponse.Append(chunk.Response);
                yield return chunk.Response;
            }

            // Now we can use the 'Done' property properly as you requested
            // Check if generation is finished to report performance
            if (chunk?.Done == true)
            {
                LogPerformance(chunk);
            }
        }
    }
    /* * HELPER: LOG PERFORMANCE
    * Rounds long decimal numbers for cleaner console output.
    */
    private void LogPerformance(OllamaGenerateResponse chunk)
    {
        if (chunk.EvalCount.HasValue && chunk.EvalDuration.HasValue && chunk.EvalDuration > 0)
        {
            // Converting nanoseconds to seconds and rounding to 2 decimal places
            double rawDuration = chunk.EvalDuration.Value / 1_000_000_000.0;
            double durationInSeconds = Math.Round(rawDuration, 2);

            // Calculating tokens per second and rounding
            double rawTps = chunk.EvalCount.Value / rawDuration;
            double tokensPerSecond = Math.Round(rawTps, 2);

            Console.WriteLine($"\n--- Generation Performance Statistics ---");
            Console.WriteLine($"Total Tokens: {chunk.EvalCount}");
            Console.WriteLine($"Time Taken: {durationInSeconds}s");
            Console.WriteLine($"Throughput: {tokensPerSecond} tokens/sec");
            Console.WriteLine("-----------------------------------------\n");
        }
    }

}