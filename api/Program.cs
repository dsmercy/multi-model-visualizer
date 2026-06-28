using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using MultiModelVisualizer.Api.Data;
using MultiModelVisualizer.Api.Services;
using Qdrant.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var connectionString = builder.Configuration.GetConnectionString("PostgreSQL")
    ?? throw new InvalidOperationException("PostgreSQL connection string is required.");

// DbContext — scoped via AddDbContext for controllers/services; worker creates its own scopes via IServiceScopeFactory
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

// Ollama
builder.Services.AddHttpClient<IOllamaService, OllamaService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AI:OllamaBaseUrl"] ?? "http://localhost:11434");
    client.Timeout = TimeSpan.FromSeconds(120);
});

// Python AI service
builder.Services.AddHttpClient<IPythonAiService, PythonAiService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["PythonService:BaseUrl"] ?? "http://localhost:8000");
    client.Timeout = TimeSpan.FromMinutes(builder.Configuration.GetValue<int>("PythonService:JobTimeoutMinutes", 10));
});

// Generation job infrastructure
// In-process channel: used by RabbitMqConsumerWorker → GenerationWorker, and for retry re-queuing
var jobQueue = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });
builder.Services.AddSingleton(jobQueue);
builder.Services.AddSingleton<JobProgressHub>();

// RabbitMQ: publish new jobs; consumer bridges RabbitMQ → in-process channel
builder.Services.AddSingleton<IRabbitMqService, RabbitMqService>();
builder.Services.AddSingleton<CancelledJobRegistry>();
builder.Services.AddHostedService<RabbitMqConsumerWorker>();

builder.Services.AddScoped<IGenerationJobService, GenerationJobService>();
builder.Services.AddHostedService<GenerationWorker>();

// Qdrant client (singleton — gRPC connection)
var qdrantHost = builder.Configuration["Qdrant:Host"] ?? "localhost";
var qdrantPort = builder.Configuration.GetValue<int>("Qdrant:Port", 6334);
builder.Services.AddSingleton(new QdrantClient(qdrantHost, qdrantPort));

// Knowledge service — uses its own HttpClient for Ollama embeddings
builder.Services.AddHttpClient<IKnowledgeService, KnowledgeService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
});

// Phase 4 services
builder.Services.AddSingleton<IReviewService, ReviewService>();
builder.Services.AddHostedService<ApprovalTimeoutService>();

// Workflow engine
builder.Services.AddScoped<IWorkflowEngine, WorkflowEngine>();

var app = builder.Build();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var initializer = new DatabaseInitializer(connectionString, scope.ServiceProvider.GetRequiredService<ILogger<DatabaseInitializer>>());
        await initializer.InitializeAsync();
        logger.LogInformation("Database initialization complete.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database initialization failed. Ensure PostgreSQL is running and accessible.");
        throw;
    }
}

app.UseCors();
app.MapControllers();
app.Run();
