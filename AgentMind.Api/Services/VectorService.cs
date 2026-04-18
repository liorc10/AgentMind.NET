namespace AgentMind.Api.Services;

using AgentMind.Api.Constants;
using AgentMind.Api.Interfaces;
using Microsoft.Extensions.Configuration;
using Qdrant.Client;
using Qdrant.Client.Grpc;




/* Revised VectorService.cs matching the IVectorService signature */

public class VectorService : IVectorService
{
    private readonly QdrantClient _client;

    public VectorService(IConfiguration configuration)
    {
        var hostname = configuration["VectorDbConfig:Hostname"] ?? "localhost";
        var port = int.Parse(configuration["VectorDbConfig:Port"] ?? "6334");
        _client = new QdrantClient(hostname, port);
    }

    /// <summary>
    /// Creates a collection with a specific vector size.
    /// Returns true if operation succeeded.
    /// </summary>
    public async Task<bool> CreateCollectionAsync(string collectionName, int vectorSize)
    {
        var collections = await _client.ListCollectionsAsync();
        if (collections.Contains(collectionName)) return true;

        await _client.CreateCollectionAsync(collectionName,
            new VectorParams { Size = (ulong)vectorSize, Distance = Distance.Cosine });

        return true;
    }

    /// <summary>
    /// Upserts a vector into a specific collection.
    /// Returns true upon successful insertion.
    /// </summary>
    public async Task<bool> UpsertVectorAsync(string collectionName, Guid id, float[] vector, Dictionary<string, object> payload)
    {
        var point = new PointStruct
        {
            Id = id,
            Vectors = vector,
            //Payload = payload // This won't work because Payload is a Dictionary<string, FieldValue> not Dictionary<string, object>
        };

        foreach (var item in payload)
        {
            point.Payload.Add(item.Key, item.Value.ToString());
        }

        await _client.UpsertAsync(collectionName, new[] { point });
        return true;
    }

    /// <summary>
    /// Searches and maps Qdrant ScoredPoint to our custom VectorMatch DTO.
    /// This prevents leaking Qdrant-specific types to other layers.
    /// </summary>
    public async Task<List<VectorMatch>> SearchSimilarAsync(string collectionName, float[] queryVector, int limit = AppConstants.Defaults.SearchLimit)
    {
        var results = await _client.SearchAsync(
            collectionName: collectionName,
            vector: queryVector,
            limit: (ulong)limit
        );

        // Mapping from ScoredPoint (Qdrant) to VectorMatch (Internal DTO)
        return results.Select(r => new VectorMatch(
            Guid.Parse(r.Id.Uuid),
            r.Score,
            r.Payload.ToDictionary(k => k.Key, v => (object)v.Value.ToString())
        )).ToList();
    }
}
