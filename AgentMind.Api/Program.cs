
using AgentMind.Api.Interfaces;
using AgentMind.Api.Services;
using AgentMind.Api.Constants;

namespace AgentMind.Api
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.

            builder.Services.AddControllers();
            // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Register the HttpClient factory to handle connections to your Mac
            // Using a named client "OllamaClient" allows us to reuse this configuration across the app
            builder.Services.AddHttpClient("OllamaClient", client =>
            {
                var baseUrl = builder.Configuration["OllamaConfig:BaseUrl"];
                client.BaseAddress = new Uri(baseUrl);

                // Technical English Comment: High timeout to account for model loading on the remote Mac.
                client.Timeout = TimeSpan.FromMinutes(5);

                // Technical English Comment: Explicitly request to keep the TCP connection alive.
                client.DefaultRequestHeaders.ConnectionClose = false;
                client.DefaultRequestHeaders.Add("Connection", "keep-alive");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Technical English Comment: Pooling and Keep-Alive settings at the socket level.
                // This ensures that the connection to the Mac (10.100.102.3) isn't dropped by idle timeouts.
                PooledConnectionLifetime = TimeSpan.FromMinutes(15),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                KeepAlivePingDelay = TimeSpan.FromSeconds(30),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
                EnableMultipleHttp2Connections = true
            });

            builder.Services.AddMemoryCache();
            // Register Qdrant client wrapper and the Vector Database service
            // Singletons are preferred for gRPC clients to manage connection pooling
            builder.Services.AddSingleton<IQdrantClientWrapper, QdrantClientWrapper>();
            builder.Services.AddSingleton<IVectorService, VectorService>();

            builder.Services.AddScoped<IOllamaService, OllamaService>();

            var app = builder.Build();


            /* * STARTUP INFRASTRUCTURE SETUP:
             * We create a temporary 'Scope' to access registered services (like VectorService) 
             * before the application officially starts. Since 'Scoped' services normally 
             * live only during an HTTP request, and there is no request at startup, 
             * this manual scope acts as a temporary container. 
             * .NET will automatically dispose of these services once the 'using' block ends.
             */
            /* AUTOMATIC INITIALIZATION 
             * Using a Scope to retrieve services during startup.
             * This ensures the Vector DB is ready before the API accepts requests.
             */
            //using (var scope = app.Services.CreateScope())
            //{
            //    var vectorService = scope.ServiceProvider.GetRequiredService<IVectorService>();

            //    // Fetching from appsettings with safe fallbacks
            //    string collectionName = builder.Configuration[AppConstants.ConfigKeys.CollectionName]
            //                            ?? AppConstants.Defaults.CollectionName;

            //    ///* * CRITICAL CONFIGURATION NOTE:
            //    // * 384 is the vector dimension for the 'all-minilm' embedding model.
            //    // * IMPORTANT: While models like 'mxbai-embed-large' use 1024 dimensions, 
            //    // * using 1024 here will cause a CRASH because the local Ollama model 
            //    // * we are currently using is not configured for that size. 
            //    // * Always ensure this number matches your active embedding model.
            //    // */
            //    int vectorSize = builder.Configuration.GetValue<int>(
            //        AppConstants.ConfigKeys.VectorSize,
            //        AppConstants.Defaults.VectorSize);

            //    await vectorService.CreateCollectionAsync(collectionName, vectorSize);
            //}

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
