using AgentMind.Api.Interfaces;
using AgentMind.Api.Models;
using AgentMind.Api.Services;
using Microsoft.AspNetCore.Mvc;
using static AgentMind.Api.Constants.AppConstants;


namespace AgentMind.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RAGController : ControllerBase
{
    // The service that handles logic and memory markers
    private readonly IOllamaService _ollamaService;
    private readonly IVectorService _vectorService;
    private readonly IConfiguration _config;

    // We inject the OllamaService through the constructor
    public RAGController(IOllamaService ollamaService, IVectorService vectorService, IConfiguration config)
    {
        _ollamaService = ollamaService;
        _vectorService = vectorService;
        _config = config;
    }
    /* * This endpoint orchestrates the entire RAG pipeline:
     * 1. Intent Routing & Persona selection.
     * 2. Semantic Embedding generation.
     * 3. Knowledge Base retrieval (placeholder).
     * 4. Final augmented streaming response.
     */
    [HttpPost("ask")]
    public async Task Ask([FromBody] ChatRequest request)
    {
        // Set the response type to plain text for a cleaner stream display
        Response.ContentType = "text/plain";

        try
        {

            string collection = _config[ConfigKeys.CollectionName] ?? Defaults.CollectionName;
            int limit = _config.GetValue<int>(ConfigKeys.SearchLimit, Defaults.SearchLimit);


            // TODO: Fetch available categories from a database or configuration service.
            // This ensures the routing logic remains dynamic and scalable.
            string dynamicCategories = "1: API Development, 2: Infrastructure, 3: Security Operations";

            // PHASE 1: ROUTING - Identify project and persona
            // We use the LLM to decide which project context is needed.
            var routing = await _ollamaService.GetRoutingInfoAsync(request.Prompt, dynamicCategories);

            // PHASE 2: EMBEDDING - Vectorize the user prompt with Project Context
            // The query vector must be associated with the routing.ProjectId to filter the Vector DB effectively.
            var queryVector = await _ollamaService.GetEmbeddingsAsync(request.Prompt);

            // PHASE 3: KNOWLEDGE RETRIEVAL 
            // Based on the 'routing.ProjectId' and the 'queryVector', we perform a semantic search.
            // The result is injected as context for the LLM.
            /* PHASE 3: KNOWLEDGE RETRIEVAL - Using actual Vector DB search */
            // var queryVector = await _ollamaService.GetEmbeddingsAsync(request.Prompt);
            var matches = await _vectorService.SearchSimilarAsync(collection, queryVector, limit);
            // Join the matches into a context string for the LLM

            // Fallback if no matches are found
            // Convert match results to a formatted string for the LLM context
            string knowledgeBaseMatchResults = matches.Any()
                ? string.Join("\n", matches.Select(m => m.Payload["content"].ToString()))
                : "No specific information found.";

            // PHASE 4: AUGMENTED GENERATION - The Final Stream - Consuming the asynchronous stream from the service
            // We call the retrieval augmented method that handles context injection.
            await foreach (var chunk in _ollamaService.RetrieveAugmentedResponseAsync(
                request.Prompt,
                routing.SystemInstruction,
                knowledgeBaseMatchResults,
                request.History, // Passing the List<string> history from the DTO
                HttpContext.RequestAborted))
            {
                await Response.WriteAsync(chunk);// Writing each chunk directly to the HTTP response stream
                await Response.Body.FlushAsync();// Forcing the buffer to flush so the user sees text in real-time
            }
        }
        catch (OperationCanceledException)
        {
            // Gracefully handle client disconnection during streaming
            Console.WriteLine("[INFO] Streaming was cancelled by the client.");
        }
        catch (Exception ex)
        {
            Response.StatusCode = 400;
            await Response.WriteAsync($"Error processing RAG request: {ex.Message}");
        }
    }

    [HttpPost("ingest")]
    public async Task<IActionResult> Ingest([FromBody] IngestRequest request)
    {
        // Now calling the service via interface
        var vector = await _ollamaService.GetEmbeddingsAsync(request.Text);

        // 2. Expand the payload with more context
        var payload = new Dictionary<string, object>
        {
            { "content", request.Text },
            { "source", request.Source },
            { "ingested_at", DateTime.UtcNow.ToString("o") }, // ISO 8601 format
            { "file_type", System.IO.Path.GetExtension(request.Source) } // Optional: track file types
        };

        // Using the confirmed VectorService
        string collection = _config[ConfigKeys.CollectionName] ?? Defaults.CollectionName;

        await _vectorService.UpsertVectorAsync(collection, Guid.NewGuid(), vector, payload);
        return Ok("Information ingested successfully.");
    }
}

/* This defines a simple data structure for ingestion */
public record IngestRequest(string Text, string Source);