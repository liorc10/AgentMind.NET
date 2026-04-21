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
    /// 
    public async Task<bool> CreateCollectionAsync(string collectionName, int vectorSize)
    {
        /* In gRPC, we use CreateCollectionsRequest */
        /* Using the high-level method as you requested */
        await _client.CreateCollectionAsync(
            collectionName: collectionName,
            /* VectorParams and Distance are part of Qdrant.Client.Grpc */
            vectorsConfig: new VectorParams
            {
                Size = (ulong)vectorSize,
                Distance = Distance.Cosine
            }
        );
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
    public async Task<List<VectorMatch>> SearchSimilarAsync(string collectionName,
     float[] queryVector,
     int limit = AppConstants.Defaults.SearchLimit,
     List<string>? requestedRoles = null)
    {
        // Build filter only when requestedRoles provided
        Filter? filter = null;
        if (requestedRoles != null && requestedRoles.Count > 0)
        {
            filter = new Filter();
            filter.Should.AddRange(requestedRoles.Select(role =>
                new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "role",
                        Match = new Match { Keyword = role }
                    }
                }));
        }

        // Execute a single search call using the high-level client
        var results = await _client.SearchAsync(
            collectionName: collectionName,
            vector: queryVector,
            filter: filter,
            limit: (ulong)limit,
            scoreThreshold: 0.7f
        );

        /* * 3. Map the results to our DTO: */
        var list = new List<VectorMatch>();
        foreach (var r in results)
        {
            // Safe id parsing: prefer Uuid, fall back to empty GUID
            Guid gid = Guid.Empty;
            try
            {
                if (!string.IsNullOrEmpty(r.Id?.Uuid))
                    Guid.TryParse(r.Id.Uuid, out gid);
            }
            catch { /* ignore and leave gid empty */ }

            // Map payload defensively
            var payload = new Dictionary<string, object>();
            if (r.Payload != null)
            {
                foreach (var kv in r.Payload)
                {
                    try
                    {
                        payload[kv.Key] = kv.Value?.ToString() ?? string.Empty;
                    }
                    catch
                    {
                        payload[kv.Key] = kv.Value?.ToString() ?? string.Empty;
                    }
                }
            }

            list.Add(new VectorMatch(gid, r.Score, payload));
        }

        return list;
    }
}
