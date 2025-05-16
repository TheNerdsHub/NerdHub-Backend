using DotNetEnv;
using MongoDB.Driver;
using NerdHub.Services;

var builder = WebApplication.CreateBuilder(args);

// Load environment variables from .env
DotNetEnv.Env.Load();

// Override configuration with .env values
builder.Configuration["MongoDB:ConnectionString"] = Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING");
builder.Configuration["Steam:ApiKey"] = Environment.GetEnvironmentVariable("STEAM_API_KEY");

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

// Configure CORS
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Environment.IsDevelopment()
        ? new[] { "*" }
        : (Environment.GetEnvironmentVariable("FRONTEND_URLS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries);

    options.AddPolicy("CustomCorsPolicy", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Use CORS
app.UseCors("CustomCorsPolicy");

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();