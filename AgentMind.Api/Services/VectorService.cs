namespace AgentMind.Api.Services;

using AgentMind.Api.Constants;
using AgentMind.Api.Interfaces;
using Qdrant.Client.Grpc;
using static AgentMind.Api.Constants.AppConstants;

public class VectorService : IVectorService
{
    private readonly IQdrantClientWrapper _client;
    private readonly IConfiguration _config;
    private readonly float _similarityThreshold;

    public VectorService(IQdrantClientWrapper client, IConfiguration config)
    {
        _client = client;
        _config = config;

        var SimilarityThresholdSection = _config[VectorDbConfig.SimilarityThreshold];
        _similarityThreshold = (float.TryParse(SimilarityThresholdSection, out var SimilarityThresholdResult) ? SimilarityThresholdResult : (float?)null)
                               ?? VectorDbConfig.similarityThresholdValue;
    }

    public async Task<bool> CreateCollectionAsync(string collectionName, int vectorSize)
    {
        await _client.CreateCollectionAsync(
            collectionName,
            new VectorParams
            {
                Size = (ulong)vectorSize,
                Distance = Distance.Cosine
            }
        );
        return true;
    }

    public async Task<bool> UpsertVectorAsync(string collectionName, Guid id, float[] vector, Dictionary<string, object> payload)
    {
        var point = new PointStruct
        {
            Id = id,
            Vectors = vector
        };

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

    internal static Dictionary<string, Value> ConvertPayloadToQdrantValues(Dictionary<string, object> payload)
    {
        var dict = new Dictionary<string, Value>(payload.Count);
        foreach (var kv in payload)
        {
            dict[kv.Key] = ConvertToQdrantValue(kv.Value);
        }
        return dict;
    }

    internal static Value ConvertToQdrantValue(object? value)
    {
        var qv = new Value();

        if (value is null)
            return qv;

        if (value is DateTime dt)
        {
            qv.IntegerValue = new DateTimeOffset(dt).ToUnixTimeSeconds();
            return qv;
        }

        if (value is DateTimeOffset dto)
        {
            qv.IntegerValue = dto.ToUnixTimeSeconds();
            return qv;
        }

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

        qv.StringValue = value.ToString() ?? string.Empty;
        return qv;
    }



    public async Task<List<VectorMatch>> SearchSimilarAsync(string collectionName,
     float[] queryVector,
     int limit = AppConstants.Defaults.SearchLimit,
     List<string>? requestedRoles = null)
    {
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

        var results = await _client.SearchAsync(
            collectionName,
            queryVector,
            filter,
            (ulong)limit,
            _similarityThreshold
        );

        var list = new List<VectorMatch>();
        foreach (var r in results)
        {
            Guid gid = Guid.Empty;
            if (!string.IsNullOrEmpty(r.Id?.Uuid))
                Guid.TryParse(r.Id.Uuid, out gid);

            var payload = new Dictionary<string, object>();
            if (r.Payload != null)
            {
                foreach (var kv in r.Payload)
                {
                    payload[kv.Key] = ConvertFromQdrantValue(kv.Value);
                }
            }

            list.Add(new VectorMatch(gid, r.Score, payload));
        }

        return list;
    }

    internal static object ConvertFromQdrantValue(Value value)
    {
        if (value == null)
            return string.Empty;

        return value.KindCase switch
        {
            Value.KindOneofCase.IntegerValue => value.IntegerValue,
            Value.KindOneofCase.DoubleValue => value.DoubleValue,
            Value.KindOneofCase.StringValue => value.StringValue,
            Value.KindOneofCase.BoolValue => value.BoolValue,
            _ => string.Empty
        };
    }
}
