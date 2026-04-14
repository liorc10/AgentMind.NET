using Microsoft.AspNetCore.Mvc;
using AgentMind.Api.Services;

namespace AgentMind.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    // The service that handles logic and memory markers
    private readonly OllamaService _ollamaService;

    // We inject the OllamaService through the constructor
    public ChatController(OllamaService ollamaService)
    {
        _ollamaService = ollamaService;
    }

    [HttpPost("ask")]
    public async Task Ask([FromBody] ChatRequest request)
    {
        // Set the response type to plain text for a cleaner stream display
        Response.ContentType = "text/plain";

        // HARDCODED identification key to ensure memory works every time
        // Even if Swagger sends something else, we use this strictly.
        string sessionId = "default-user";

        try
        {
            // Consuming the asynchronous stream from the service
            // We explicitly pass the "default-user" string here
            await foreach (var word in _ollamaService.StreamChatAsync(request.Prompt, sessionId, HttpContext.RequestAborted))
            {
                // Writing each chunk directly to the HTTP response stream
                await Response.WriteAsync(word);

                // Forcing the buffer to flush so the user sees text in real-time
                await Response.Body.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            Response.StatusCode = 400;
            await Response.WriteAsync($"Error: {ex.Message}");
        }
    }

    public class ChatRequest
    {
        public string Prompt { get; set; }
        public List<long>? History { get; set; }

    }
}