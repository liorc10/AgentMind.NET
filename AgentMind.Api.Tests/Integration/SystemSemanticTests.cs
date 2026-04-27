using AgentMind.Api.Controllers;
using AgentMind.Api.Interfaces;
using AgentMind.Api.Models;
using AgentMind.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Text;

namespace AgentMind.Api.Tests.System;

public class SystemSemanticTests : IDisposable
{
    private readonly IQdrantClientWrapper _qdrantWrapper;
    private readonly IVectorService _vectorService;
    private readonly IOllamaService _ollamaService;
    private readonly IConfiguration _config;
    private readonly string _testCollectionName;
    private readonly bool _isIntegrationTestEnabled;

    public SystemSemanticTests()
    {
        _config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddEnvironmentVariables()
            .Build();

        var runEnv = Environment.GetEnvironmentVariable("RUN_QDRANT_INTEGRATION_TESTS");
        _isIntegrationTestEnabled = runEnv == "true";

        if (_isIntegrationTestEnabled)
        {
            _qdrantWrapper = new QdrantClientWrapper(_config);
            _vectorService = new VectorService(_qdrantWrapper, _config/* 0.2f*/);

            var mockFactory = new Mock<IHttpClientFactory>();
            string ollamaUrl = _config["OllamaConfig:BaseUrl"] ?? "http://localhost:11434";
            var httpClient = new HttpClient { BaseAddress = new Uri(ollamaUrl) };
            // Technical English Comment: Request the server to keep the TCP connection open for subsequent requests.
            httpClient.DefaultRequestHeaders.Connection.Add("keep-alive");

            // Technical English Comment: Increase timeout to handle potential model loading delays on the first call.
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            mockFactory.Setup(f => f.CreateClient("OllamaClient")).Returns(httpClient);

            _ollamaService = new OllamaService(mockFactory.Object, _config, _vectorService);
            _testCollectionName = $"system_semantic_test_{Guid.NewGuid():N}";
        }
    }

    [Fact]
    public async Task SemanticFlow_FullControllerIntegration_ShouldReturnCorrectResult()
    {
        if (!_isIntegrationTestEnabled) return;

        // --- PART 1: DATA PREPARATION ---
        var dataset = new[] {
            new {
                Title = "Space Exploration",
                Content = "The James Webb telescope captures images of distant galaxies using infrared sensors to pierce through cosmic dust clouds.",
                Category = "Science & Space",
                Metadata = new Dictionary<string, string> { ["Author"] = "NASA", ["Year"] = "2024" }
            },
            new {
                Title = "Global Economy",
                Content = "Central banks are adjusting interest rates and quantitative easing measures to combat rising inflation and stabilize global currency markets.",
                Category = "Finance & Economy",
                Metadata = new Dictionary<string, string> { ["Sector"] = "Banking", ["Risk"] = "High" }
            },
            new {
                Title = "Italian Cuisine",
                Content = "Authentic Neapolitan pizza requires high-protein double zero flour, San Marzano tomatoes, and buffalo mozzarella.",
                Category = "Culinary Arts",
                Metadata = new Dictionary<string, string> { ["Region"] = "Naples" }
            },
            new {
                Title = "Fitness Routine",
                Content = "High-intensity interval training (HIIT) combined with progressive overload is proven to maximize metabolic rate efficiency.",
                Category = "Health & Fitness",
                Metadata = new Dictionary<string, string> { ["Focus"] = "Cardio" }
            },
            new {
                Title = "AI Evolution",
                Content = "Large language models are transforming human-machine interaction through zero-shot learning and transformer architectures.",
                Category = "Artificial Intelligence",
                Metadata = new Dictionary<string, string> { ["Model"] = "Transformer" }
            }
        };

        var controller = new RAGController(_ollamaService, _vectorService, _config);
        var httpContext = new DefaultHttpContext();
        var responseStream = new MemoryStream();
        httpContext.Response.Body = responseStream;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        try
        {
           // await _vectorService.CreateCollectionAsync(_testCollectionName, 384);

            // --- PART 2: DIRECT INGEST CALLS (Fixing IngestRequest record constructor) ---
            foreach (var item in dataset)
            {
                await Task.Delay(1000); 
                
                // Note: Based on your image, IngestRequest expects (int? ProjectName, string KnowlegeBaseText, Dictionary<string, string> KnowlegeBaseDictionary)
                var ingestRequest = new IngestRequest(string.Empty, item.Content, item.Metadata);

                var ingestResult = await controller.Ingest(ingestRequest);
                Assert.IsType<OkObjectResult>(ingestResult);
            }



            // --- PART 3: DIRECT ASK CALL (Fixing ChatRequest and History type) ---
            var chatRequest = new ChatRequest
            {
                Prompt = "How do financial institutions manage the value of money and rising prices?",
                Metadata = new Dictionary<string, string> { ["Context"] = "Money" },
                History = new List<string>()
            };

            await controller.Ask(chatRequest);

            responseStream.Position = 0;
            using var reader = new StreamReader(responseStream);
            string finalResponse = await reader.ReadToEndAsync();

            Assert.NotEmpty(finalResponse);
            // Verify that the response is grounded in the provided economy data
            Assert.Contains("interest", finalResponse.ToLower());
        }
        finally
        {
            if (_qdrantWrapper != null) await _qdrantWrapper.DeleteCollectionAsync(_testCollectionName);
        }
    }

    public void Dispose() { }
}