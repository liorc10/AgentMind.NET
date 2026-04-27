using AgentMind.Api.Interfaces;
using AgentMind.Api.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text;
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

    // Reserved key for the primary content field in the unified input dictionary.
    // Using a constant prevents magic strings and casing mismatches across all call sites.
    private const string AllContentPoint = "Content";

    // We inject the OllamaService through the constructor
    public RAGController(IOllamaService ollamaService, IVectorService vectorService, IConfiguration config)
    {
        _ollamaService = ollamaService;
        _vectorService = vectorService;
        _config = config;
    }

    /* * ResolveTargetCollectionAsync - Unified Context & Collection Resolver
     *
     * Accepts a single unified dictionary that contains ALL input fields:
     *   - "Content" key  : the main text (prompt or document body)
     *   - All other keys : metadata fields (Title, Category, AuthorId, etc.)
     *
     * This shared helper is called by BOTH Ingest and Ask to guarantee:
     * - Consistent routing via the same 8-category LLM decision.
     * - A deterministic, project-scoped collection name.
     * - An identical flattened embedding string so that vectors written
     *   during ingestion are comparable to vectors generated at query time.
     *
     * Manual Override: if requestedProjectName is provided and > 0, AI routing
     * is bypassed and the supplied value is used directly for collection mapping.
     *
     * Returns a ResolvedContext record containing:
     *   CollectionName : project-scoped Qdrant collection (e.g. "project_4_collection")
     *   FlattenedText  : enriched string used to generate the embedding vector
     *   Routing        : LLM routing decision (ProjectName)
     */
    private async Task<ResolvedContext> ResolveTargetCollectionAsync(
        Dictionary<string, string> enrichedKnowlegebaseDict,
        string? requestedProjectName = null,
        CancellationToken ct = default)
    {
        // Build temporary string only for the routing decision.
        string routingContext = string.Join(" | ", enrichedKnowlegebaseDict.Select(kv => $"{kv.Key}: {kv.Value}"));

        RoutingResult routing;
        if (!string.IsNullOrEmpty(requestedProjectName))
        {
            routing = new RoutingResult
            {
                ProjectName = requestedProjectName,
                SystemInstruction = string.Empty
            };
        }
        else
        {
            routing = await _ollamaService.GetRoutingInfoAsync(routingContext, AvailableDatasetCategories.DynamicCategories, ct);
        }
        string safeProjectName = SanitizeCollectionName(routing.ProjectName);
        string collectionName = $"test_project_{safeProjectName}_collection";

        // Return the full dictionary along with the routing decision.
        return new ResolvedContext(collectionName, enrichedKnowlegebaseDict, routing);
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
        Response.ContentType = "text/plain";

        try
        {
            int limit = _config.GetValue<int>(ConfigKeys.SearchLimit, Defaults.SearchLimit);

            var enrichedInput = EnrichUserPayload(request.Prompt, request.Metadata);
            var context = await ResolveTargetCollectionAsync(enrichedInput, null, HttpContext.RequestAborted);

            //string flattenedForEmbedding = 
            //    string.Join(" | ", context.enrichedKnowlegebaseDict.Select(kv => $"{kv.Key}: {kv.Value}"));
            string flattenedForEmbedding = request.Prompt.Trim();

            var queryVector = await _ollamaService.GetEmbeddingsAsync(flattenedForEmbedding, HttpContext.RequestAborted);

            var matchRetrievedResults = await _vectorService.SearchSimilarAsync(context.CollectionName, queryVector, limit);

            string knowledgeBaseMatchResults = string.Empty;

            if (matchRetrievedResults.Any())
            {
                var resultBuilder = new StringBuilder();

                foreach (var match in matchRetrievedResults)
                {
                    var rowParts = new List<string>();

                    foreach (var entry in match.Payload)
                    {
                        rowParts.Add($"{entry.Key}: {entry.Value}");
                    }

                    if (resultBuilder.Length > 0)
                    {
                        resultBuilder.Append("\n");
                    }

                    resultBuilder.Append(string.Join(" | ", rowParts));
                }

                knowledgeBaseMatchResults = resultBuilder.ToString();
            }
            else
            {
                knowledgeBaseMatchResults = "No specific information found.";
            }

            string systemInstruction = context.Routing.SystemInstruction ?? "You are a helpful AI assistant.";

            await foreach (var chunk in _ollamaService.RetrieveAugmentedResponseAsync(
                request.Prompt,
                systemInstruction,
                knowledgeBaseMatchResults,
                request.History,
                HttpContext.RequestAborted))
            {
                await Response.WriteAsync(chunk);
                await Response.Body.FlushAsync();
            }
        }
        catch (OperationCanceledException)
        {
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
        var enrichedInput = EnrichUserPayload(request.KnowlegeBaseText, request.KnowlegeBaseDictionary);
        var context = await ResolveTargetCollectionAsync(enrichedInput, request.ProjectId, HttpContext.RequestAborted);

        // Prepare the storage payload with system fields.
        var unifiedKnowlegebasePayload = new Dictionary<string, object>();
        foreach (var kv in context.enrichedKnowlegebaseDict)
        {
            unifiedKnowlegebasePayload[kv.Key] = kv.Value;
        }

        unifiedKnowlegebasePayload["_system_project_id"] = context.CollectionName;
        unifiedKnowlegebasePayload["_system_ingested_at"] = DateTime.UtcNow.ToString("o");

        // Generate flattened text for embedding from the enriched data.
        string flattenedForEmbedding = string.Join(" | ", context.enrichedKnowlegebaseDict.Select(kv => $"{kv.Key}: {kv.Value}"));
        var vector = await _ollamaService.GetEmbeddingsAsync(flattenedForEmbedding);


        await _vectorService.UpsertVectorAsync(context.CollectionName, Guid.NewGuid(), vector, unifiedKnowlegebasePayload);

        return Ok("Information ingested successfully.");
    }


    private Dictionary<string, string> EnrichUserPayload(string freeText, Dictionary<string, string> dictionary)
    {
        var enriched = new Dictionary<string, string>();
        string primaryContentKey = $"user_provided_{AllContentPoint}";

        if (!string.IsNullOrWhiteSpace(freeText))
        {
            enriched[primaryContentKey] = freeText;
        }

        if (dictionary != null)
        {
            foreach (var kv in dictionary)
            {
                string SystemKeyContent = $"user_provided_{kv.Key}";
                int collisionCounter = 1;
                string originalKey = SystemKeyContent;

                while (enriched.ContainsKey(SystemKeyContent) && collisionCounter <= 1000)
                {
                    SystemKeyContent = $"{originalKey}_additional_{collisionCounter}";
                    collisionCounter++;
                }
                enriched[SystemKeyContent] = kv.Value;
            }
        }
        return enriched;
    }

    private string SanitizeCollectionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "default";

        // Remove special characters, replace spaces with underscores, 
        // and convert to lowercase for Qdrant compatibility.
        var sanitized = name.Replace(" ", "_")
                            .Replace("&", "and") // Replace '&' with 'and' for readability
                            .ToLower();

        //  Keep only alphanumeric characters and underscores using Regex.
        sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"[^a-z0-9_]", "");

        return sanitized;
    }
}

