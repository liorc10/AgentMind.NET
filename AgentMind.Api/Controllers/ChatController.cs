using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace AgentMind.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
// Injecting the HttpClientFactory to use our pre-configured "OllamaClient"

    public ChatController(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    [HttpPost("ask")]
    public async Task Ask([FromBody] ChatRequest request)
    {
        // 1. Get the pre-configured client
        var client = _httpClientFactory.CreateClient("OllamaClient");

        // 2. Prepare the payload with History (Context) and Streaming enabled
        var requestBody = new
        {
            model = _config["OllamaConfig:ModelName"],
            prompt = request.Prompt,
            context = request.History, // Passing the memory markers back to the model
            stream = true             // Enabling real-time streaming
        };

        var jsonPayload = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            // 3. Use SendAsync with ResponseHeadersRead for streaming support
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "api/generate") { Content = content };
            using var response = await client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);

            if (response.IsSuccessStatusCode)
            {
                // Set the correct content type for the stream
                Response.ContentType = "application/json";

                using var responseStream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(responseStream);

                // 4. Read from Ollama and write directly to the client's response body
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Optional: Log the line to see it in your Output window while debugging
                    // System.Diagnostics.Debug.WriteLine(line);


                    // Write the JSON chunk to the output stream immediately
                    await Response.WriteAsync(line + "\n");
                    await Response.Body.FlushAsync(); // Forces the word to appear in the UI/Swagger
                }
            }
            else
            {
                Response.StatusCode = (int)response.StatusCode;
            }
        }
        catch (Exception ex)
        {
            Response.StatusCode = 400;
            await Response.WriteAsync($"Connection failed: {ex.Message}");
        }
    }

    // Models used for Request/Response
    public class ChatRequest
    {
        public string Prompt { get; set; }
        public List<int>? History { get; set; } // The memory markers from Ollama
    }

    public class OllamaResponse
    {
        public string response { get; set; }
        public List<int> context { get; set; }
        public long total_duration { get; set; }
        public int eval_count { get; set; }
        public bool done { get; set; }
    }
}