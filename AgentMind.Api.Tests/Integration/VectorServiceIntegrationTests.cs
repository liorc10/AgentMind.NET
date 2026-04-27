using AgentMind.Api.Constants;
using AgentMind.Api.Interfaces;
using AgentMind.Api.Services;
using Microsoft.Extensions.Configuration;
using Qdrant.Client.Grpc;
using static AgentMind.Api.Constants.AppConstants;

namespace AgentMind.Api.Tests.Integration;

public class VectorServiceIntegrationTests : IDisposable
{
    private readonly IQdrantClientWrapper _client;
    private readonly IVectorService _service;
    private readonly IConfiguration _config;
    private readonly string _testCollectionName;
    private readonly bool _isIntegrationTestEnabled;

    public VectorServiceIntegrationTests()
    {
        /* *
         * BUILD: Loading the configuration from multiple sources.
         * 1. SetBasePath: Sets the physical directory where the builder looks for settings.
         * 2. AddJsonFile: Reads our appsettings.json (must be copied to bin via 'Copy if newer').
         * 3. AddEnvironmentVariables: Allows overriding JSON values with OS environment variables.
         */

        _config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();


        /* *
         * CHECK: Reading the manual flag from the environment to decide 
         * if the logic inside the test should actually execute.
         */
        var runEnv = Environment.GetEnvironmentVariable("RUN_QDRANT_INTEGRATION_TESTS");
        _isIntegrationTestEnabled = runEnv == "true";

        // Only initialize heavy resources if the integration flag is active
        if (_isIntegrationTestEnabled)
        {
            _client = new QdrantClientWrapper(_config);
            _service = new VectorService(_client, _config, VectorDbConfig.similarityThresholdValue);
            string baseName = _config.GetValue<string>("VectorDbConfig:CollectionName") ?? "test_collection";
            _testCollectionName = $"{baseName}{Guid.NewGuid():N}";
        }
    }

    /* *
     * [Fact]: The 'Skip' property is used to dynamically disable the test in the Test Explorer
     * if the required environment variable is not set to 'true'.
     */
    [Fact]
    public async Task EndToEnd_CreateUpsertSearch_WithDateTime_ReturnsCorrectPayload()
    {
        // Guard clause: Prevent execution if the environment is not configured for integration
        if (!_isIntegrationTestEnabled)
        {
            return;
        }

        try
        {
            /* ARRANGE: Setup temporary infrastructure */
            // Using 384 dimensions as required by the local 'all-minilm' model
            int vectorSize = _config.GetValue<int>("VectorDbConfig:VectorSize", AppConstants.Defaults.VectorSize);

            await _service.CreateCollectionAsync(_testCollectionName, vectorSize);

            var testId = Guid.NewGuid();
            var testVector = new float[384];
            // The rest of the array (indices 1 to 383) remains 0.0f by default in C#
            testVector[0] = 0.5f; // Set a value to ensure similarity during search

            var testDateTime = new DateTime(2024, 6, 15, 14, 30, 0, DateTimeKind.Utc);

            var payload = new Dictionary<string, object>
            {
                ["name"] = "integration_test",
                ["count"] = 42,
                ["active"] = true,
                ["score"] = 9.8,
                ["created"] = testDateTime
            };

            /* ACT: Execute service methods */
            await _service.UpsertVectorAsync(_testCollectionName, testId, testVector, payload);

            // Eventually Consistent delay: Wait for Qdrant to index the new point
            await Task.Delay(1000);

            // Search using the threshold fetched from configuration
            var results = await _service.SearchSimilarAsync(_testCollectionName, testVector, 5);

            /* ASSERT: Validate the data cycle */
            Assert.NotEmpty(results);
            var match = results.First();

            Assert.Equal(testId, match.Id);
            Assert.Equal("integration_test", match.Payload["name"]);

            // Qdrant's gRPC layer maps integers to 'long' in C#
            Assert.Equal(42L, match.Payload["count"]);
            Assert.Equal(true, match.Payload["active"]);

            /* *
             * VALIDATE: Check if our DateTime conversion to Unix Epoch (seconds) 
             * is working correctly as implemented in VectorService.ConvertToQdrantValue
             */
            var epochValue = Assert.IsType<long>(match.Payload["created"]);
            var expectedEpoch = new DateTimeOffset(testDateTime).ToUnixTimeSeconds();
            Assert.Equal(expectedEpoch, epochValue);
        }
        finally
        {
            /* CLEANUP: Remove the temporary collection from the live database */
            if (_client != null)
            {
                await _client.DeleteCollectionAsync(_testCollectionName);
            }
        }
    }

    [Fact]
    public async Task UpsertAsync_MultiplePointsWithDiversePayload_ShouldSucceed()
    {
        // 1. Arrange: Define vector size and prepare 5 unique points
        int vectorSize = _config.GetValue<int>("VectorDbConfig:VectorSize", AppConstants.Defaults.VectorSize);
        var points = new List<PointStruct>();

        // Defining descriptive names for the 'name' field
        string[] names = { "Integration_Alpha", "Integration_Beta", "Integration_Gamma", "Integration_Delta", "Integration_Epsilon" };

        for (int i = 0; i < 5; i++)
        {
            // Creating vectors with varied values to ensure they are distinct
            float[] vectorData = new float[vectorSize];
            vectorData[0] = 0.1f * (i + 1);

            var point = new PointStruct
            {
                Id = Guid.NewGuid(),
                Vectors = vectorData,
                Payload =
            {
                // Existing fields from previous tests
                ["name"] = new Value { StringValue = names[i] },
                ["count"] = new Value { IntegerValue = i + 100 },
                ["created"] = new Value { StringValue = DateTime.UtcNow.ToString("o") },
                
                // New requested fields
                ["point_index"] = new Value { IntegerValue = i },
                ["test_name"] = new Value { StringValue = $"Batch_Test_Run_{i}" }
            }
            };
            points.Add(point);
        }

        try
        {
            // 2. Act: Prepare the collection and perform the batch upsert
            await _service.CreateCollectionAsync(_testCollectionName, vectorSize);

            // Sending the entire list as a batch to the database
            await _client.UpsertAsync(_testCollectionName, points.ToArray());

            // Increased delay to ensure consistency, especially during Debug sessions
            // This allows the database to finish indexing the new points
            await Task.Delay(2500);

            // 3. Assert: Verify the data was stored and is searchable
            // We use a search vector that matches the characteristics of our data
            float[] queryVector = new float[vectorSize];
            queryVector[0] = 0.1f;

            var searchResult = await _client.SearchAsync(
                _testCollectionName,      // collectionName
                queryVector,              // vector
                null,                     // filter
                10,                       // limit (requesting more than we sent to be safe)
                0.0f                      // similarityThreshold (0.0 ensures we see all results)
            );

            // Asserting that we received exactly the 5 points we inserted
            Assert.NotNull(searchResult);
            Assert.Equal(5, searchResult.Count);

            // Deep verification: checking the payload of the first point
            var alphaPoint = searchResult.FirstOrDefault(p =>
                p.Payload.ContainsKey("name") &&
                p.Payload["name"].StringValue == "Integration_Alpha");

            Assert.NotNull(alphaPoint);
            Assert.Equal(0, alphaPoint.Payload["point_index"].IntegerValue);
            Assert.Equal("Batch_Test_Run_0", alphaPoint.Payload["test_name"].StringValue);
        }
        finally
        {
            // 4. Cleanup: Remove the temporary collection from the database
            if (_client != null)
            {
                await _client.DeleteCollectionAsync(_testCollectionName);
            }
        }
    }

    [Fact]
    public async Task SearchAsync_WithFilter_ShouldReturnOnlyMatchingPoints()
    {
        // 1. Arrange: Prepare 5 points with incremental indexes
        int vectorSize = _config.GetValue<int>("VectorDbConfig:VectorSize", AppConstants.Defaults.VectorSize);
        var points = new List<PointStruct>();

        for (int i = 0; i < 5; i++)
        {
            float[] vectorData = new float[vectorSize];
            vectorData[0] = 0.1f * (i + 1) + 0.1f;

            points.Add(new PointStruct
            {
                Id = Guid.NewGuid(),
                Vectors = vectorData,
                Payload =
            {
                ["point_index"] = new Value { IntegerValue = i },
                ["test_run"] = new Value { StringValue = "filter_test" }
            }
            });
        }

        try
        {
            await _service.CreateCollectionAsync(_testCollectionName, vectorSize);
            await _client.UpsertAsync(_testCollectionName, points.ToArray());
            await Task.Delay(2000);

            // 2. Act: Create a filter for "point_index > 2"
            // This will narrow down our 5 points to only 2 points (index 3 and 4)
            var filter = new Filter();
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "point_index",
                    Range = new Qdrant.Client.Grpc.Range { Gt = 2 }
                }
            });
            /* *
         * CRITICAL FIX: 
         * 1. Use a non-zero search vector.
         * 2. Use a negative threshold (-1.0f) to bypass score filtering.
         * This forces Qdrant to return everything that matches the filter.
         */
            float[] queryVector = new float[vectorSize];
            queryVector[0] = 0.5f;
            // Use the filter in the SearchAsync call
            var searchResult = await _client.SearchAsync(
                _testCollectionName,
                queryVector,
                filter,                 // Passing the filter object here
                10,                     // Limit
                0.0f                    // Threshold
            );

            // 3. Assert: We expect exactly 2 points (those with index 3 and 4)
            Assert.NotNull(searchResult);
            Assert.Equal(2, searchResult.Count);

            // Verify that all returned points actually satisfy the filter
            foreach (var p in searchResult)
            {
                Assert.True(p.Payload["point_index"].IntegerValue > 2);
            }
        }
        finally
        {
            if (_client != null)
            {
                await _client.DeleteCollectionAsync(_testCollectionName);
            }
        }
    }
    public void Dispose()
    {
        // Cleanup logic for the test class instance
    }
}