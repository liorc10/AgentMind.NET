using Qdrant.Client.Grpc;
using AgentMind.Api.Constants;

namespace AgentMind.Api.Interfaces;

public interface IVectorService
{
    /* * Creates a new collection in Qdrant with specific vector parameters.
     * Uses the REST API: PUT /collections/{name}
     */
    Task<bool> CreateCollectionAsync(string collectionName, int vectorSize);

    /* * Inserts or updates a point (vector + metadata) in the collection.
     * Uses the REST API: PUT /collections/{name}/points
     */
    Task<bool> UpsertVectorAsync(string collectionName, Guid id, float[] vector, Dictionary<string, object> payload);

    /* * Searches for the closest vectors using cosine similarity.
     * Uses the REST API: POST /collections/{name}/points/search
     */
    Task<List<VectorMatch>> SearchSimilarAsync(string collectionName, float[] queryVector, int limit = AppConstants.Defaults.SearchLimit);
}

/* Custom DTO to hold search results without external dependencies */
public record VectorMatch(Guid Id, double Score, Dictionary<string, object> Payload);