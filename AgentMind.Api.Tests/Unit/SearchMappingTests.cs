using AgentMind.Api.Interfaces;
using AgentMind.Api.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using Qdrant.Client.Grpc;
using static AgentMind.Api.Constants.AppConstants;

namespace AgentMind.Api.Tests.Unit;

public class SearchMappingTests
{
    [Fact]
    public async Task SearchSimilarAsync_ReturnsVectorMatches_WithConvertedPayload()
    {
        var mockClient = new Mock<IQdrantClientWrapper>();


        /* SETUP: Define the raw configuration data in memory */
        Dictionary<string, string?> inMemorySettings = new Dictionary<string, string?>
        {
            {
                VectorDbConfig.SimilarityThreshold, "0.8"
            }
        };

        /* BUILD: Convert the dictionary into a real IConfiguration object */
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();


        var scoredPoints = new List<ScoredPoint>
        {
            new ScoredPoint
            {
                Id = new PointId { Uuid = Guid.NewGuid().ToString() },
                Score = 0.95f,
                Payload =
                {
                    ["name"] = new Value { StringValue = "test" },
                    ["count"] = new Value { IntegerValue = 10 },
                    ["active"] = new Value { BoolValue = true },
                    ["score"] = new Value { DoubleValue = 8.5 }
                }
            }
        };

        mockClient
            .Setup(c => c.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<float[]>(),
                It.IsAny<Filter>(),
                It.IsAny<ulong>(),
                It.IsAny<float>()))
            .ReturnsAsync(scoredPoints);

        var service = new VectorService(mockClient.Object, configuration);
        var results = await service.SearchSimilarAsync("test_collection", new float[] { 0.1f, 0.2f }, 10);

        Assert.Single(results);
        var match = results[0];
        Assert.Equal(0.95, match.Score, precision: 2);
        Assert.Equal("test", match.Payload["name"]);
        Assert.Equal(10L, match.Payload["count"]);
        Assert.Equal(true, match.Payload["active"]);
        Assert.Equal(8.5, match.Payload["score"]);
    }

    [Fact]
    public async Task SearchSimilarAsync_WithRoleFilter_PassesFilterToClient()
    {
        /* 1. Define the test value once */
        var expectedThreshold = 0.8f;
        var thresholdString = expectedThreshold.ToString();

        var mockClient = new Mock<IQdrantClientWrapper>();

        /* SETUP: Define the raw configuration data in memory */
        Dictionary<string, string?> inMemorySettings = new Dictionary<string, string?>
        {
            {
                VectorDbConfig.SimilarityThreshold, thresholdString
            }
        };

        /* BUILD: Convert the dictionary into a real IConfiguration object */
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        mockClient
            .Setup(c => c.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<float[]>(),
                It.Is<Filter>(f => f != null && f.Should.Count == 2),
                It.IsAny<ulong>(),
                It.IsAny<float>()))
            .ReturnsAsync(new List<ScoredPoint>());

        var service = new VectorService(mockClient.Object, configuration);
        await service.SearchSimilarAsync("test_collection", new float[] { 0.1f }, 10, new List<string> { "user", "system" });

        mockClient.Verify(c => c.SearchAsync(
            "test_collection",
            It.IsAny<float[]>(),
            It.Is<Filter>(f => f != null && f.Should.Count == 2),
            10,
            expectedThreshold), Times.Once);
    }

    [Fact]
    public async Task SearchSimilarAsync_ShouldPassExactParametersToClient()
    {

        // Exact values we expect to see
        const string expectedCollection = "test_collection";
        var expectedVector = new float[] { 0.1f };
        const ulong expectedLimit = 10;
        const float expectedThreshold = 0.7f;
        var thresholdString = expectedThreshold.ToString();
        var roles = new List<string> { "user", "system" };


        // ARRANGE
        var mockClient = new Mock<IQdrantClientWrapper>();


        /* *
     * THE SIMPLE WAY: Creating the configuration with a one-liner.
     * Using var and a direct Dictionary initializer.
     */
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [VectorDbConfig.SimilarityThreshold] = thresholdString
            })
            .Build();

        /* *
         * STRENGTHENED SETUP:
         * Instead of It.IsAny, we use the exact expected values.
         * For the vector, we check sequence equality.
         * For the filter, we verify it contains the exact number of roles.
         */
        mockClient
            .Setup(c => c.SearchAsync(
                expectedCollection,                                 // Must match exactly
                It.Is<float[]>(v => v != null && v.SequenceEqual(expectedVector)), // Must contain 0.1f
                It.Is<Filter>(f => f != null && f.Should.Count == 2), // Must have both roles
                expectedLimit,                                      // Must be exactly 10
                expectedThreshold                                   // Must be exactly 0.7f
            ))
            .ReturnsAsync(new List<ScoredPoint>());

        var service = new VectorService(mockClient.Object, configuration);

        // ACT
        // Calling with the exact same values defined above
        await service.SearchSimilarAsync(expectedCollection, expectedVector, (int)expectedLimit, roles);

        // ASSERT
        // Verification confirms that SearchAsync was hit with these precise requirements
        mockClient.Verify(c => c.SearchAsync(
            expectedCollection,
            It.Is<float[]>(v => v.SequenceEqual(expectedVector)),
            It.Is<Filter>(f => f.Should.Count == 2),
            expectedLimit,
            expectedThreshold), Times.Once);
    }
    [Fact]
    public async Task SearchSimilarAsync_EmptyResults_ReturnsEmptyList()
    {
        var mockClient = new Mock<IQdrantClientWrapper>();
        /* *
     * THE SIMPLE WAY: Creating the configuration with a one-liner.
     * Using var and a direct Dictionary initializer.
     */
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [VectorDbConfig.SimilarityThreshold] = "0.8"
            })
            .Build();

        mockClient
            .Setup(c => c.SearchAsync(
                It.IsAny<string>(),
                It.IsAny<float[]>(),
                It.IsAny<Filter>(),
                It.IsAny<ulong>(),
                It.IsAny<float>()))
            .ReturnsAsync(new List<ScoredPoint>());

        var service = new VectorService(mockClient.Object, configuration);
        var results = await service.SearchSimilarAsync("test_collection", new float[] { 0.1f }, 10);

        Assert.Empty(results);
    }
}
