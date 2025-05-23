using DotNetEnv;
using MongoDB.Driver;
using NerdHub.Services;
using NerdHub.Services.Interfaces;

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
builder.Services.AddSwaggerGen();

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
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();