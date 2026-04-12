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
    public async Task<IActionResult> Ask([FromBody] string userPrompt)
    {
        // 1. Get the client from the factory
        var client = _httpClientFactory.CreateClient("OllamaClient");

        // 2. Prepare the request body for Ollama
        // We specify the model and the prompt, and set 'stream' to false for simplicity
        var requestBody = new
        {
            model = _config["OllamaConfig:ModelName"],
            prompt = userPrompt,
            stream = false
        };

        var jsonPayload = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        try
        {
            // 3. Send the POST request to the Mac (Ollama endpoint is usually /api/generate)
            // Use the relative path WITHOUT a leading slash to avoid issues with the BaseAddress
            var response = await client.PostAsync("api/generate", content);
            if (response.IsSuccessStatusCode)
            {
                var responseString = string.Empty;
                //var responseString = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    responseString = await response.Content.ReadAsStringAsync();

                    // Deserialize the JSON to our object
                    var ollamaData = JsonSerializer.Deserialize<OllamaResponse>(responseString);

                    // Return ONLY the text answer
                    //return Ok(ollamaData.response);
                    if (ollamaData != null)
                    {
                        // חישוב זמן בשניות - detailed comments in English
                        double durationInSeconds = Math.Round(ollamaData.total_duration / 1_000_000_000.0, 2);
                        // We return a NEW object containing all the fields we want to see in Swagger
                        return Ok(new
                        {
                            // Rounding to 2 decimal places for a cleaner UI
                            Answer = ollamaData.response,
                            TimeTakenSeconds = durationInSeconds,
                            Tokens = ollamaData.eval_count,
                            IsDone = ollamaData.done
                        });
                    }
                }

                // 4. Return the raw response from the LLM back to the user
                return Ok(responseString);
            }

            return StatusCode((int)response.StatusCode, "Error talking to Ollama on Mac");
        }
        catch (Exception ex)
        {
            return BadRequest($"Connection failed: {ex.Message}");
        }
    }
    public class OllamaResponse
    {
        // The main text response from the AI
        public string response { get; set; }

        // The memory of the conversation
        public List<int> context { get; set; }

        // Total time taken in nanoseconds
        public long total_duration { get; set; }

        // Number of tokens in the response
        public int eval_count { get; set; }

        // Tells us if the generation is finished
        public bool done { get; set; }
    }
}