namespace AgentMind.Api.Models;

/* * DTO representing the incoming user request.
 * This class is decoupled from internal Ollama models for better flexibility.
 */
public class ChatRequest
{
    // The main user query - Mandatory
    public string Prompt { get; set; }

    /* * * OPTIONAL: Conversation history.
     * In our RAG flow, if this is null or empty, the system will simply 
     * generate a response based on the prompt and retrieved knowledge.
     */
    public List<string>? History { get; set; }
}