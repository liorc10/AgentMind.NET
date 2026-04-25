using AgentMind.Api.Interfaces;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using static AgentMind.Api.Constants.AppConstants;

namespace AgentMind.Api.Services;

public class QdrantClientWrapper : IQdrantClientWrapper
{
    private readonly QdrantClient _client;

    public QdrantClientWrapper(IConfiguration configuration)
    {
        var hostname = configuration[VectorDbConfig.Hostname] ?? "localhost";
        var port = int.Parse(configuration[VectorDbConfig.Port] ?? "6334");
        _client = new QdrantClient(hostname, port);
    }

    public Task CreateCollectionAsync(string collectionName, VectorParams vectorsConfig)
    {
        return _client.CreateCollectionAsync(collectionName, vectorsConfig);
    }

    public Task UpsertAsync(string collectionName, IReadOnlyList<PointStruct> points)
    {
        return _client.UpsertAsync(collectionName, points);
    }

    public async Task<IReadOnlyList<ScoredPoint>> SearchAsync(string collectionName, float[] vector, Filter? filter, ulong limit, float scoreThreshold)
    {
        var results = await _client.SearchAsync(collectionName: collectionName, vector: vector, filter: filter, limit: limit, scoreThreshold: scoreThreshold);
        return results;
    }

    public Task DeleteCollectionAsync(string collectionName)
    {
        return _client.DeleteCollectionAsync(collectionName);
    }
}
