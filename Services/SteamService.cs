using NerdHub.Models;
using MongoDB.Driver;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization;

namespace NerdHub.Services
{
    public class SteamService
    {
        private readonly IConfiguration _configuration;
        private readonly IMongoCollection<GameDetails> _games;
        private readonly ILogger<SteamService> _logger;

        public SteamService(IConfiguration configuration, IMongoClient client, ILogger<SteamService> logger)
        {
            _configuration = configuration;
            _logger = logger;

            var database = client.GetDatabase("NH-Games");
            _games = database.GetCollection<GameDetails>("games");
        }

        private async Task<string> FetchWithRetryAsync(HttpClient httpClient, string url, int maxRetries = 15)
        {
            int retryCount = 0;
            while (retryCount < maxRetries)
            {
                var response = await httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                else if ((int)response.StatusCode == 429) // Too Many Requests
                {
                    retryCount++;
                    var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? Math.Pow(2, retryCount) * 60; // Exponential backoff in minutes

                    // Log a warning about the retry
                    // ADD THE TIMESTAMP TO THE LOG MESSAGE
                    _logger.LogWarning("Rate limit hit. Retrying request to {Url}. Retry count: {RetryCount}. Waiting for {RetryAfter} seconds.", url, retryCount, retryAfter);

                    await Task.Delay(TimeSpan.FromSeconds(retryAfter));
                }
                else
                {
                    response.EnsureSuccessStatusCode(); // Throw exception for other non-success status codes
                }
            }

            throw new HttpRequestException($"Failed to fetch data from {url} after {maxRetries} retries due to rate limiting.");
        }

        public async Task UpdateOwnedGames(string steamId)
        {
            try
            {
                var httpClient = new HttpClient();
                var steamApiKey = _configuration["Steam:ApiKey"];

                // Get the list of owned games
                var response = await FetchWithRetryAsync(httpClient, $"http://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={steamApiKey}&steamid={steamId}&format=json");

                // Deserialize the JSON response into the wrapper class
                var apiResponse = JsonConvert.DeserializeObject<SteamApiResponse>(response, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore
                });

                if (apiResponse?.response?.games != null)
                {
                    var gameDetailsList = new List<WriteModel<GameDetails>>();

                    foreach (var game in apiResponse.response.games)
                    {
                        try
                        {
                            // Fetch game details for each game with retry logic
                            var gameDataResponse = await FetchWithRetryAsync(httpClient, $"http://store.steampowered.com/api/appdetails?appids={game.steam_appid}&l=english");

                            // Deserialize the JSON response into a dictionary
                            var gameDataDictionary = JsonConvert.DeserializeObject<Dictionary<string, GameDetailsResponse>>(gameDataResponse, new JsonSerializerSettings
                            {
                                NullValueHandling = NullValueHandling.Ignore,
                                MissingMemberHandling = MissingMemberHandling.Ignore
                            });

                            if (gameDataDictionary != null && gameDataDictionary.TryGetValue(game.steam_appid.ToString(), out var gameDetailsResponse) && gameDetailsResponse.success)
                            {
                                // Map the game details to the GameDetails model
                                var gameDetails = new GameDetails
                                {
                                    appid = game.steam_appid, // Use steam_appid as the MongoDB document ID
                                    name = gameDetailsResponse.data?.name,
                                    shortDescription = gameDetailsResponse.data?.shortDescription,
                                    developers = gameDetailsResponse.data?.developers,
                                    publishers = gameDetailsResponse.data?.publishers,
                                    priceOverview = gameDetailsResponse.data?.priceOverview,
                                    releaseDate = gameDetailsResponse.data?.releaseDate,
                                    genres = gameDetailsResponse.data?.genres,
                                    platforms = gameDetailsResponse.data?.platforms,
                                    headerImage = gameDetailsResponse.data?.headerImage,
                                    capsuleImage = gameDetailsResponse.data?.capsuleImage,
                                    capsuleImagev5 = gameDetailsResponse.data?.capsuleImagev5,
                                    website = gameDetailsResponse.data?.website,
                                    pcRequirements = gameDetailsResponse.data?.pcRequirements,
                                    macRequirements = gameDetailsResponse.data?.macRequirements,
                                    linuxRequirements = gameDetailsResponse.data?.linuxRequirements,
                                    dlc = gameDetailsResponse.data?.dlc,
                                    isFree = gameDetailsResponse.data?.isFree,
                                    controllerSupport = gameDetailsResponse.data?.controllerSupport,
                                    requiredAge = gameDetailsResponse.data?.requiredAge,
                                    packages = gameDetailsResponse.data?.packages,
                                    screenshots = gameDetailsResponse.data?.screenshots,
                                    movies = gameDetailsResponse.data?.movies,
                                    achievements = gameDetailsResponse.data?.achievements,
                                    recommendations = gameDetailsResponse.data?.recommendations,
                                    categories = gameDetailsResponse.data?.categories,
                                    supportedLanguages = gameDetailsResponse.data?.supportedLanguages,
                                    metacritic = gameDetailsResponse.data?.metacritic
                                };

                                // Create an upsert operation
                                var filter = Builders<GameDetails>.Filter.Eq(g => g.appid, gameDetails.appid);
                                var update = new ReplaceOneModel<GameDetails>(filter, gameDetails) { IsUpsert = true };
                                gameDetailsList.Add(update);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to fetch details for game {AppId}", game.steam_appid);
                        }
                    }

                    // Perform a bulk write operation to MongoDB
                    if (gameDetailsList.Count > 0)
                    {
                        await _games.BulkWriteAsync(gameDetailsList);
                        _logger.LogInformation("Upserted {Count} game details into the database.", gameDetailsList.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating owned games for Steam ID {SteamId}", steamId);
                throw;
            }
        }
    }
}