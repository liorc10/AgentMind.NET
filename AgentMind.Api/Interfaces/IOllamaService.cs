using AgentMind.Api.Models;

namespace AgentMind.Api.Interfaces;

public interface IOllamaService
{
    // Added CancellationToken to match the implementation
    Task<float[]> GetEmbeddingsAsync(string text, CancellationToken ct = default);

    // Matches the routing logic you use in the Controller
    Task<RoutingResult> GetRoutingInfoAsync(string userQuery, string categoriesList, CancellationToken ct = default);

    // Changed to match the streaming implementation in your Service
    IAsyncEnumerable<string> RetrieveAugmentedResponseAsync(
       string prompt,
       string systemInstruction,
       string knowledgeBaseMatchResults,
       List<string>? history,
       CancellationToken ct);
}