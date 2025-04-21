using NerdHub.Models;
using MongoDB.Driver;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace NerdHub.Services
{
    public class SteamService
    {
        private readonly IConfiguration _configuration;
        private readonly IMongoCollection<GameDetails> _games;
        private readonly ILogger<SteamService> _logger;
        private readonly SemaphoreSlim _rateLimitSemaphore = new SemaphoreSlim(3, 3); // Allow up to 3 concurrent requests
        private readonly TimeSpan _rateLimitDelay = TimeSpan.FromSeconds(1); // 1-second delay for rate limiting

        public SteamService(IConfiguration configuration, IMongoClient client, ILogger<SteamService> logger)
        {
            _configuration = configuration;
            _logger = logger;

            var database = client.GetDatabase("NH-Games");
            _games = database.GetCollection<GameDetails>("games");
        }

        private async Task<string> FetchWithRetryAndRateLimitAsync(HttpClient httpClient, string url, int maxRetries = 15)
        {
            int retryCount = 0;

            while (retryCount < maxRetries)
            {
                await _rateLimitSemaphore.WaitAsync(); // Acquire a slot for the request
                try
                {
                    // Wait for the rate limit delay to ensure we don't exceed the limit
                    await Task.Delay(_rateLimitDelay);

                    var response = await httpClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        return await response.Content.ReadAsStringAsync();
                    }
                    else if ((int)response.StatusCode == 429) // Too Many Requests
                    {
                        retryCount++;
                        var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? Math.Pow(2, retryCount); // Exponential backoff
                        _logger.LogWarning("Rate limit hit at {Timestamp}. Retrying request to {Url}. Retry count: {RetryCount}. Waiting for {RetryAfter} seconds.", DateTime.UtcNow, url, retryCount, retryAfter);
                        await Task.Delay(TimeSpan.FromSeconds(retryAfter));
                    }
                    else
                    {
                        response.EnsureSuccessStatusCode(); // Throw exception for other non-success status codes
                    }
                }
                finally
                {
                    _rateLimitSemaphore.Release(); // Release the slot after the request is complete
                }
            }

            throw new HttpRequestException($"Failed to fetch data from {url} after {maxRetries} retries due to rate limiting.");
        }

        public async Task<GameDetails?> FetchGameDetailsAsync(HttpClient httpClient, int? appId)
        {
            try
            {
                var gameDataResponse = await FetchWithRetryAndRateLimitAsync(httpClient, $"http://store.steampowered.com/api/appdetails?appids={appId}&l=english");

                var gameDataDictionary = JsonConvert.DeserializeObject<Dictionary<string, GameDetailsResponse>>(gameDataResponse, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore
                });

                if (gameDataDictionary != null && gameDataDictionary.TryGetValue(appId.ToString(), out var gameDetailsResponse) && gameDetailsResponse.success)
                {
                    return gameDetailsResponse.data;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch details for game {AppId}", appId);
            }

            return null;
        }

        public async Task UpdateOwnedGames(long steamId, bool overrideExisting)
        {
            try
            {
                var httpClient = new HttpClient();
                var steamApiKey = _configuration["Steam:ApiKey"];

                var response = await FetchWithRetryAndRateLimitAsync(httpClient, $"http://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={steamApiKey}&steamid={steamId}&format=json");

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
                        var existingGame = await _games.Find(g => g.steam_appid == game.steam_appid).FirstOrDefaultAsync();

                        if (existingGame != null && !overrideExisting)
                        {
                            _logger.LogInformation("Game with AppID {AppId} already exists in the database. Skipping.", game.steam_appid);
                            continue;
                        }

                        var gameDetails = await FetchGameDetailsAsync(httpClient, game.steam_appid);
                        if (gameDetails != null)
                        {
                            gameDetails.LastModifiedTime = DateTime.UtcNow;
                            gameDetails.ownedBy = new List<OwnedBy>
                            {
                                new OwnedBy { steamId = new List<long> { steamId } }
                            };

                            var filter = Builders<GameDetails>.Filter.Eq(g => g.steam_appid, gameDetails.steam_appid);
                            var update = new ReplaceOneModel<GameDetails>(filter, gameDetails) { IsUpsert = true };
                            gameDetailsList.Add(update);
                        }
                    }

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