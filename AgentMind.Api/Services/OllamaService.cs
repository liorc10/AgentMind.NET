using AgentMind.Api.Constants;
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

        // Fix 1: Use 'input' instead of 'prompt' for the /api/embed endpoint
        var requestBody = new
        {
            model = _config[AppConstants.ConfigKeys.EmbeddingModel] ?? "all-minilm",
            input = text
        };

        try
        {
            // PostAsJsonAsync handles the serialization of the request bod
            var response = await client.PostAsJsonAsync("api/embed", requestBody, ct);
            response.EnsureSuccessStatusCode();
            // Deserializing into the model we created in OllamaModels.cs
            var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(ct);

            /* * * The /api/embed API returns an array of vectors (embeddings).
            * Since we send a single string input, we take the first vector from the array.
            */
            if (result?.Embeddings != null && result.Embeddings.Length > 0)
            {
                return result.Embeddings[0];
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating embeddings: {ex.Message}");
        }
        return Array.Empty<float>();
    }

    // --- PHASE 2: INTENT ROUTING & PERSONA ---
    /* * Uses the LLM to analyze the user's query and decide the context.
     * It returns a ProjectName for Vector DB filtering and a System Instruction.
     * We force JSON output to ensure the C# code can parse the decision.
     */
    public async Task<RoutingResult> GetRoutingInfoAsync(string userQuery, string categoriesList, CancellationToken ct = default)
    {
        // Validation
        //If the input is empty, we must not proceed. 
        // Returning a default ID would be misleading; an exception ensures the flow is halted.
        if (string.IsNullOrWhiteSpace(userQuery))
        {
            throw new ArgumentException("Cannot route an empty or null context.");
        }

        var client = _httpClientFactory.CreateClient("OllamaClient");
        //Generate the expected JSON structure dynamically from the RoutingResult class.
        string dynamicSchema = GetDynamicJsonSchema<RoutingResult>();



        //  We use an explicit instruction 'STRICTLY use one of the following slugs' 
        // to prevent the model from returning pretty-printed names like 'Science & Space'.
        string routingPrompt = ("SystemInstruction: You are a strict router. Map the user query to the exact string from the list. " +
                               $"Query: '{userQuery.Trim()}' " +
                               $"Available Categories: [{categoriesList.Trim()}]. " +
                               "Rules: 1. ProjectName MUST be the EXACT verbatim string from the list (no changes, no unicode escaping). " +
                               "2. If no match is found, output 'General Category'. " +
                               "3. SystemInstruction: Write a short 'You are an expert in...' style persona. " +
                               $"Constraint: Return ONLY JSON matching: {dynamicSchema.Trim()}").Trim();


        //string routingPrompt = ($"Analyze the query: '{userQuery.Trim()}' " +
        //                       $"Available Project Slugs: {categoriesList.Trim()}. " +
        //                       "Task: 1. Identify the most relevant slug. You MUST return the exact string from the list. " +
        //                       "2. Write a one-sentence 'System' persona instruction. " +
        //                       "Return ONLY a valid JSON object matching this structure: " + dynamicSchema.Trim()).Trim();

        var requestBody = new
        {
            model = _config["OllamaConfig:ModelName"],
            prompt = routingPrompt,
            format = "json",
            // Technical Note: 'raw = true' and 'stream = false' are used here to ensure deterministic JSON output 
            // and to prevent Ollama from injecting automatic BOS (Beginning Of Sentence) tokens. 
            // This is critical for maintaining vector embedding consistency with the database (Qdrant) 
            // and avoiding parsing exceptions caused by conversational preambles or streaming chunks.
            stream = false, // get full response at once
            raw = true // <--- Mandatory to fix the BOS issue
        };

        try
        {
            var response = await client.PostAsJsonAsync("api/generate", requestBody, ct);
            response.EnsureSuccessStatusCode();

            var jsonString = await response.Content.ReadAsStringAsync(ct);

            // Case-insensitive deserialization to handle potential LLM casing issues
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // The /api/generate endpoint wraps the model output inside the "response" field
            // of the OllamaGenerateResponse envelope. We must extract that inner JSON string
            // before deserializing it into our RoutingResult model.
            var envelope = JsonSerializer.Deserialize<OllamaGenerateResponse>(jsonString, options);
            if (envelope == null || string.IsNullOrEmpty(envelope.Response))
            {
                throw new Exception("Empty response from Ollama.");
            }
            // Technical English Comment: Final deserialization of the inner AI response into the RoutingResult object.
            return JsonSerializer.Deserialize<RoutingResult>(envelope!.Response, options)!;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in GetRoutingInfoAsync: {ex.Message}");
            // Consider logging the full JSON string for debugging
            // Console.WriteLine($"Full JSON received: {jsonString}");
            throw;
        }
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
    private string GetDynamicJsonSchema<T>()
    {
        var properties = typeof(T).GetProperties()
            .Select(p => $"\"{p.Name}\": \"{p.PropertyType.Name.ToLower()}\"");
        return "{{ " + string.Join(", ", properties) + " }}";
    }

}
