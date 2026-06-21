using Microsoft.EntityFrameworkCore;
using MultiModelVisualizer.Api.Data;
using MultiModelVisualizer.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
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

// Database
var connectionString = builder.Configuration.GetConnectionString("PostgreSQL")
    ?? throw new InvalidOperationException("PostgreSQL connection string is required.");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

// Ollama HTTP client
builder.Services.AddHttpClient<IOllamaService, OllamaService>(client =>
{
    var ollamaBaseUrl = builder.Configuration["AI:OllamaBaseUrl"] ?? "http://localhost:11434";
    client.BaseAddress = new Uri(ollamaBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(120);
});

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
