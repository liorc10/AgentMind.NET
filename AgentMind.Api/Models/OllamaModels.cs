using System.Text.Json.Serialization;

namespace AgentMind.Api.Models;

// --- DTOs for Ollama API Mapping ---

/* * Represents a single message in the conversation.
 * Role can be "system", "user", or "assistant".
 */
public class OllamaMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; }
}

// Response model specifically for the /api/generate endpoint
public class OllamaGenerateResponse
{
    [JsonPropertyName("model")]
    public string Model { get; set; }

    [JsonPropertyName("response")] // The text chunk is here in Generate API
    public string Response { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }

    [JsonPropertyName("eval_count")]
    public int? EvalCount { get; set; }

    [JsonPropertyName("eval_duration")]
    public long? EvalDuration { get; set; }
}

/* * The response structure for the /api/chat endpoint.
 */
//public class OllamaChatResponse
//{
//    [JsonPropertyName("model")]
//    public string Model { get; set; }

//    [JsonPropertyName("message")]
//    public OllamaMessage Message { get; set; }

//    [JsonPropertyName("done")]
//    public bool Done { get; set; }

//    [JsonPropertyName("eval_count")]
//    public int? EvalCount { get; set; }

//    [JsonPropertyName("eval_duration")]
//    public long? EvalDuration { get; set; }
//}

/* * The response structure for the /api/embeddings endpoint.
 * This captures the numerical vector representation of the text.
 */
public class OllamaEmbeddingResponse
{
    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; }
}

/* * Logic object used for internal routing decisions.
 * ProjectId helps filter the SQL database.
 * SystemInstruction sets the AI persona.
 */
public class RoutingResult
{
    public int ProjectId { get; set; }
    public string SystemInstruction { get; set; }
}
