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
                await _rateLimitSemaphore.WaitAsync();
                try
                {
                    await Task.Delay(_rateLimitDelay);
                    var response = await httpClient.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                        return await response.Content.ReadAsStringAsync();

                    if ((int)response.StatusCode == 429) // Too Many Requests
                    {
                        retryCount++;
                        var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? Math.Pow(2, retryCount)*30;
                        _logger.LogWarning("Rate limit hit. Retrying in {RetryAfter} seconds. Retry count: {RetryCount}.", retryAfter, retryCount);
                        await Task.Delay(TimeSpan.FromSeconds(retryAfter));
                    }
                    else
                    {
                        response.EnsureSuccessStatusCode();
                    }
                }
                finally
                {
                    _rateLimitSemaphore.Release();
                }
            }

            throw new HttpRequestException($"Failed to fetch data from {url} after {maxRetries} retries.");
        }

        private async Task<GameDetails?> FetchGameDetailsAsync(HttpClient httpClient, int appId)
        {
            try
            {
                var url = $"http://store.steampowered.com/api/appdetails?appids={appId}&l=english";
                var response = await FetchWithRetryAndRateLimitAsync(httpClient, url);

                var gameData = JsonConvert.DeserializeObject<Dictionary<string, GameDetailsResponse>>(response, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore
                });

                if (gameData != null && gameData.TryGetValue(appId.ToString(), out var gameDetailsResponse) && gameDetailsResponse.success)
                {
                    var gameDetails = gameDetailsResponse.data;

                    // Perform currency conversion if necessary
                    if (gameDetails?.priceOverview != null && gameDetails.priceOverview.currency != "USD")
                    {
                        var exchangeRate = await GetExchangeRateAsync(httpClient, gameDetails.priceOverview.currency, "USD");
                        if (exchangeRate > 0)
                        {
                            gameDetails.priceOverview.initial = Convert.ToInt32(gameDetails.priceOverview.initial / exchangeRate);
                            gameDetails.priceOverview.final = Convert.ToInt32(gameDetails.priceOverview.final / exchangeRate);
                            gameDetails.priceOverview.currency = "USD";
                            gameDetails.priceOverview.initialFormatted = $"${gameDetails.priceOverview.initial / 100.0:F2}";
                            gameDetails.priceOverview.finalFormatted = $"${gameDetails.priceOverview.final / 100.0:F2}";
                        }
                    }

                    return gameDetails;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch details for game {AppId}", appId);
            }

            return null;
        }

        private async Task<double> GetExchangeRateAsync(HttpClient httpClient, string fromCurrency, string toCurrency)
        {
            try
            {
                var apiKey = _configuration["ExchangeRateApiKey"];
                var url = $"https://api.exchangerate-api.com/v4/latest/{fromCurrency}";
                var response = await httpClient.GetStringAsync(url);

                var exchangeRateData = JsonConvert.DeserializeObject<ExchangeRateResponse>(response);
                if (exchangeRateData?.Rates != null && exchangeRateData.Rates.TryGetValue(toCurrency, out var rate))
                    return rate;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch exchange rate from {FromCurrency} to {ToCurrency}", fromCurrency, toCurrency);
            }

            return 0; // Return 0 if the exchange rate could not be fetched
        }

        public async Task UpdateOwnedGamesAsync(long steamId, bool overrideExisting)
        {
            try
            {
                var httpClient = new HttpClient();
                var steamApiKey = _configuration["Steam:ApiKey"];
                var url = $"http://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={steamApiKey}&steamid={steamId}&format=json";

                var response = await FetchWithRetryAndRateLimitAsync(httpClient, url);
                var apiResponse = JsonConvert.DeserializeObject<SteamApiResponse>(response, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore
                });

                if (apiResponse?.response?.games == null) return;

                var gameDetailsList = new List<WriteModel<GameDetails>>();
                var failedGameIds = new List<int>(); // List to store failed game IDs

                foreach (var game in apiResponse.response.games)
                {
                    await _rateLimitSemaphore.WaitAsync(); // Respect rate limits
                    try
                    {
                        var filter = Builders<GameDetails>.Filter.Eq(g => g.appid, game.steam_appid);
                        var existingGame = await _games.Find(filter).FirstOrDefaultAsync();

                        if (!overrideExisting && existingGame != null)
                        {
                            // Ensure the ownedBy object is initialized
                            if (existingGame.ownedBy == null)
                            {
                                existingGame.ownedBy = new OwnedBy
                                {
                                    steamId = new List<long>(),
                                    epicId = null
                                };
                            }

                            // Ensure the steamId list is initialized
                            if (existingGame.ownedBy.steamId == null)
                            {
                                existingGame.ownedBy.steamId = new List<long>();
                            }

                            // Add the current Steam ID if it doesn't already exist
                            if (!existingGame.ownedBy.steamId.Contains(steamId))
                            {
                                existingGame.ownedBy.steamId.Add(steamId);
                                existingGame.LastModifiedTime = DateTime.UtcNow.ToString("o");
                            }

                            // Update the existing game in the database
                            var update = new ReplaceOneModel<GameDetails>(
                                Builders<GameDetails>.Filter.Eq(g => g.appid, existingGame.appid),
                                existingGame
                            ) { IsUpsert = true };

                            gameDetailsList.Add(update);

                            _logger.LogInformation("Game with AppID {AppId} already exists. Updated ownedBy to include SteamID {SteamId}.", game.appid, steamId);
                            continue;
                        }

                        var gameDetails = await FetchGameDetailsAsync(httpClient, (int)game.steam_appid);
                        if (gameDetails != null)
                        {
                            gameDetails.LastModifiedTime = DateTime.UtcNow.ToString("o");

                            // Preserve existing fields if the game already exists
                            if (existingGame != null)
                            {
                                gameDetails.appid = existingGame.appid;
                                gameDetails.ownedBy = existingGame.ownedBy;
                            }

                            // Add the current Steam ID to the ownedBy list
                            if (gameDetails.ownedBy == null)
                            {
                                gameDetails.ownedBy = new OwnedBy();
                            }

                            if (gameDetails.ownedBy.steamId == null || !gameDetails.ownedBy.steamId.Contains(steamId))
                            {
                                if (gameDetails.ownedBy.steamId == null)
                                {
                                    gameDetails.ownedBy.steamId = new List<long>();
                                }
                                gameDetails.ownedBy.steamId.Add(steamId);
                            }

                            var update = new ReplaceOneModel<GameDetails>(filter, gameDetails) { IsUpsert = true };
                            gameDetailsList.Add(update);

                            _logger.LogInformation("Successfully added or updated game with AppID {AppId}.", game.steam_appid);
                        }
                        else
                        {
                            failedGameIds.Add((int)game.steam_appid); // Add to failed list if details could not be fetched
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing game with AppID {AppId}.", game.steam_appid);
                        failedGameIds.Add((int)game.steam_appid); // Add to failed list on exception
                    }
                    finally
                    {
                        _rateLimitSemaphore.Release();
                    }
                }

                if (gameDetailsList.Count > 0)
                {
                    await _games.BulkWriteAsync(gameDetailsList);
                    _logger.LogInformation("Upserted {Count} game details into the database.", gameDetailsList.Count);
                }

                // Log the failed game IDs and their count
                if (failedGameIds.Count > 0)
                {
                    _logger.LogWarning("Failed to fetch details for {Count} games. AppIDs: {FailedGameIds}", failedGameIds.Count, string.Join(", ", failedGameIds));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating owned games for Steam ID {SteamId}", steamId);
                throw;
            }
        }
        public async Task<GameDetails?> UpdateGameInfoAsync(int appId)
        {
            try
            {
                var httpClient = new HttpClient();
                var gameDetails = await FetchGameDetailsAsync(httpClient, appId);

                if (gameDetails != null)
                {
                    gameDetails.LastModifiedTime = DateTime.UtcNow.ToString("o");

                    var filter = Builders<GameDetails>.Filter.Eq(g => g.appid, appId);
                    var existingGame = await _games.Find(filter).FirstOrDefaultAsync();

                    if (existingGame != null)
                    {
                        gameDetails.appid = existingGame.appid;
                        gameDetails.ownedBy = existingGame.ownedBy;
                    }

                    await _games.ReplaceOneAsync(filter, gameDetails, new ReplaceOptions { IsUpsert = true });
                    _logger.LogInformation("Game with AppID {AppId} updated successfully.", appId);
                    return gameDetails;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating game with AppID {AppId}.", appId);
            }

            return null;
        }

        public async Task<List<GameDetails>> GetAllGamesAsync() => await _games.Find(_ => true).ToListAsync();

        public async Task<GameDetails> GetGameByIdAsync(int appId) => await _games.Find(g => g.appid == appId).FirstOrDefaultAsync();
    }
}