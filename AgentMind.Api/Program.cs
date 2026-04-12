
using AgentMind.Api.Services;

namespace AgentMind.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddSingleton<OllamaService>();

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Register the HttpClient factory to handle connections to your Mac
            // Using a named client "OllamaClient" allows us to reuse this configuration across the app
            builder.Services.AddHttpClient("OllamaClient", client =>
            {
                // Retrieve the Mac's IP address from appsettings.json
                var baseUrl = builder.Configuration["OllamaConfig:BaseUrl"];

                // Set the base destination for all requests made by this client
                // Note: If baseUrl is null or malformed, this will throw an exception on startup
                client.BaseAddress = new Uri(baseUrl);

                // Optional: Add a timeout to prevent the API from waiting forever if the Mac is asleep
                client.Timeout = TimeSpan.FromMinutes(5);
            });

            builder.Services.AddMemoryCache();
            builder.Services.AddScoped<OllamaService>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
