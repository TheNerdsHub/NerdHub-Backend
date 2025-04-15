using MongoDB.Driver;
using NerdHub.Services; // Add this to include the SteamService namespace

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();