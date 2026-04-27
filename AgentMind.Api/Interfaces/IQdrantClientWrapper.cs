using Qdrant.Client.Grpc;

namespace AgentMind.Api.Interfaces;

public interface IQdrantClientWrapper
{
    Task CreateCollectionAsync(string collectionName, VectorParams vectorsConfig);

    Task UpsertAsync(string collectionName, IReadOnlyList<PointStruct> points);

    Task<IReadOnlyList<ScoredPoint>> SearchAsync(string collectionName, float[] vector, Filter? filter, ulong limit, float scoreThreshold);

    // Optionally: delete collection for integration tests
    Task DeleteCollectionAsync(string collectionName);

    Task<IEnumerable<string>> ListCollectionsAsync();
}
