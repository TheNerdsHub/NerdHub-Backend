using DotNetEnv;
using MongoDB.Driver;
using NerdHub.Services;
using NerdHub.Services.Interfaces;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Load environment variables from .env
DotNetEnv.Env.Load();

// Override configuration with .env values
builder.Configuration["MongoDB:ConnectionString"] = Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING");
builder.Configuration["Steam:ApiKey"] = Environment.GetEnvironmentVariable("STEAM_API_KEY");
builder.Configuration["Version"] = Environment.GetEnvironmentVariable("VERSION");

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo 
    { 
        Title = "NerdHub API", 
        Version = "v1",
        Description = "The NerdHub backend API for games, users, and quotes"
    });
});

// Register MongoDB client
builder.Services.AddSingleton<IMongoClient, MongoClient>(sp =>
{
    var connectionString = sp.GetRequiredService<IConfiguration>()["MongoDB:ConnectionString"];
    return new MongoClient(connectionString);
});

// Register SteamService
builder.Services.AddScoped<SteamService>();
// Register UserMappingService
builder.Services.AddScoped<UserMappingService>();
// Register ProgressTracker
builder.Services.AddSingleton<IProgressTracker, ProgressTracker>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "NerdHub API v1");
        c.RoutePrefix = string.Empty; // This makes Swagger UI available at the root
    });
}

app.UseCors("AllowFrontend");

app.UseHttpsRedirection();
app.UseAuthorization();

// Add a simple health check endpoint at root when not in development
app.MapGet("/", () => new
{
    service = "NerdHub API",
    version = "1.0.0",
    status = "running",
    timestamp = DateTime.UtcNow,
    endpoints = new
    {
        swagger = "/swagger",
        games = "/api/Games",
        quotes = "/api/Quotes",
        health = "/health"
    }
}).WithName("GetApiInfo");

app.MapControllers();
app.Run();