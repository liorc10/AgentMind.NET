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

        // Map payload values to Qdrant Value types using a single helper call for clarity.
        // This preserves original value types (int/long/double/bool/string) in Qdrant payloads
        // instead of converting everything to strings.
        if (payload != null)
        {
            var mapped = ConvertPayloadToQdrantValues(payload);
            foreach (var kv in mapped)
            {
                point.Payload.Add(kv.Key, kv.Value);
            }
        }

        await _client.UpsertAsync(collectionName, new[] { point });
        return true;
    }

    // Convert an entire payload dictionary to Qdrant Value dictionary in a single helper.
    // This keeps UpsertVectorAsync implementation simple and readable.
    private static Dictionary<string, Value> ConvertPayloadToQdrantValues(Dictionary<string, object> payload)
    {
        var dict = new Dictionary<string, Value>(payload.Count);
        foreach (var kv in payload)
        {
            dict[kv.Key] = ConvertToQdrantValue(kv.Value);
        }
        return dict;
    }

    // Convert a single CLR value to the generated Qdrant Value proto.
    // This simplified implementation performs direct type checks and assigns the corresponding
    // generated property. It is explicit, fast, and easier to read compared to the reflection-based approach.
    private static Value ConvertToQdrantValue(object? value)
    {
        var qv = new Value();

        if (value is null)
            return qv;

        // Direct type checks and assignments preserve native types in Qdrant payloads.
        if (value is string s)
        {
            qv.StringValue = s;
            return qv;
        }

        if (value is int i)
        {
            qv.IntegerValue = i;
            return qv;
        }

        if (value is long l)
        {
            qv.IntegerValue = l;
            return qv;
        }

        if (value is bool b)
        {
            qv.BoolValue = b;
            return qv;
        }

        if (value is double d)
        {
            qv.DoubleValue = d;
            return qv;
        }

        if (value is float f)
        {
            qv.DoubleValue = f;
            return qv;
        }

        // Fallback: store the string representation if type is not explicitly handled.
        qv.StringValue = value.ToString() ?? string.Empty;
        return qv;
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
