namespace AgentMind.Api.Models;

/* * DTO representing the incoming user request.
 * This class is decoupled from internal Ollama models for better flexibility.
 */
public class ChatRequest
{
    // The main user query - Mandatory
    public string Prompt { get; set; }

    /* * Flexible collection for any additional context (Title, Category, Author, etc.) */
    public Dictionary<string, string>? Metadata { get; set; }

    /* * * OPTIONAL: Conversation history.
     * In our RAG flow, if this is null or empty, the system will simply 
     * generate a response based on the prompt and retrieved knowledge.
     */
    public List<string>? History { get; set; }
}

/* * IngestRequest - Unified ingestion contract.
 * Data     : single dictionary carrying ALL fields, including the primary content
 *            under the AllContentPoint ("Content") and any metadata (Title, Category, etc.).
 * ProjectName: optional manual override; when provided and > 0, AI routing is bypassed.
 */
public record IngestRequest(string? ProjectId, string KnowlegeBaseText, Dictionary<string, string> KnowlegeBaseDictionary);

/* * ResolvedContext — immutable result returned by ResolveTargetCollectionAsync.
 * CollectionName : project-scoped Qdrant collection (e.g. "project_4_collection")
 * FlattenedText  : enriched string used to generate the embedding vector
 * Routing        : LLM routing decision (ProjectName + SystemInstruction)
 */
public record ResolvedContext(string CollectionName, Dictionary<string, string> enrichedKnowlegebaseDict, RoutingResult Routing);