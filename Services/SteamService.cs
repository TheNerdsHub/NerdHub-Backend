using NerdHub.Models;
using MongoDB.Driver;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Linq;

namespace NerdHub.Services
{
    public class SteamService
    {
        private readonly IConfiguration _configuration;
        private readonly IMongoCollection<GameDetails> _games;
        private readonly ILogger<SteamService> _logger;
        private readonly SemaphoreSlim _rateLimitSemaphore = new SemaphoreSlim(3, 3); // Allow up to 3 concurrent requests
        private readonly TimeSpan _rateLimitDelay = TimeSpan.FromSeconds(1); // 1-second delay for rate limiting
        private readonly HashSet<int> _blacklistedAppIds = new HashSet<int> { 100, 2430, 12750, 10190, 36630, 43160, 90530, 91310, 105430, 107400, 109400, 109410, 1368430, 1449560, 1890860, 200110, 201700, 202090, 202990, 204080, 205930, 21110, 21120, 212220, 212370, 216250, 218210, 218450, 221080, 221790, 226320, 227700, 234530, 238110, 239220, 241640, 263440, 2651360, 310380, 316390, 321040, 323370, 367540, 382850, 410700, 427920, 436150, 447500, 476620, 524440, 596350, 623990, 654310, 858460, 878760, 912290, 931180 };

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

        public async Task UpdateOwnedGamesAsync(string steamIds, bool overrideExisting)
        {
            try
            {
                var httpClient = new HttpClient();
                var steamApiKey = _configuration["Steam:ApiKey"];
                var steamIdList = steamIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var masterFailedGameIds = new HashSet<int>(); // Deduplicated master list of failed game IDs

                foreach (var steamId in steamIdList)
                {
                    var url = $"http://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={steamApiKey}&steamid={steamId}&format=json";

                    var response = await FetchWithRetryAndRateLimitAsync(httpClient, url);
                    var apiResponse = JsonConvert.DeserializeObject<SteamApiResponse>(response, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        MissingMemberHandling = MissingMemberHandling.Ignore
                    });

                    if (apiResponse?.response?.games == null)
                    {
                        _logger.LogWarning("No games found for Steam ID {SteamId}. Response: {ApiResponse}", steamId, response);
                        continue;
                    }

                    var failedGameIds = new List<int>(); // List to store failed game IDs for this Steam ID

                    foreach (var game in apiResponse.response.games)
{
                    if (_blacklistedAppIds.Contains((int)game.appid))
                    {
                        _logger.LogInformation("Skipping blacklisted AppID {AppId}.", game.appid);
                        continue;
                    }

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
                                        steamId = new List<string>(),
                                        epicId = null
                                    };
                                }

                                // Ensure the steamId list is initialized
                                if (existingGame.ownedBy.steamId == null)
                                {
                                    existingGame.ownedBy.steamId = new List<string>();
                                }

                                // Check if the current Steam ID is already in the list
                                if (existingGame.ownedBy.steamId.Contains(steamId))
                                {
                                    _logger.LogInformation("Game with AppID {AppId} already exists and already includes SteamID {SteamId}. No update necessary.", game.appid, steamId);
                                    continue; // Skip further processing for this game
                                }

                                // Add the current Steam ID to the list
                                existingGame.ownedBy.steamId.Add(steamId);

                                existingGame.LastModifiedTime = DateTime.UtcNow.ToString("o");

                                // Update the existing game in the database
                                await _games.ReplaceOneAsync(filter, existingGame);
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

                                    // Add the current Steam ID to the ownedBy list
                                    if (gameDetails.ownedBy == null)
                                    {
                                        gameDetails.ownedBy = new OwnedBy();
                                    }

                                    if (gameDetails.ownedBy.steamId == null || !gameDetails.ownedBy.steamId.Contains(steamId))
                                    {
                                        if (gameDetails.ownedBy.steamId == null)
                                        {
                                            gameDetails.ownedBy.steamId = new List<string>();
                                        }
                                        gameDetails.ownedBy.steamId.Add(steamId);
                                    }

                                    // Update the existing game
                                    await _games.ReplaceOneAsync(filter, gameDetails);
                                    _logger.LogInformation("Successfully updated existing game with AppID {AppId}.", game.steam_appid);
                                }
                                else
                                {
                                    // Add the current Steam ID to the ownedBy list
                                    if (gameDetails.ownedBy == null)
                                    {
                                        gameDetails.ownedBy = new OwnedBy();
                                    }

                                    if (gameDetails.ownedBy.steamId == null)
                                    {
                                        gameDetails.ownedBy.steamId = new List<string>();
                                    }
                                    gameDetails.ownedBy.steamId.Add(steamId);

                                    // Insert the new game
                                    await _games.InsertOneAsync(gameDetails);
                                    _logger.LogInformation("Successfully added new game with AppID {AppId}.", game.steam_appid);
                                }
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

                    // Add the failed game IDs for this Steam ID to the master list
                    foreach (var failedGameId in failedGameIds)
                    {
                        masterFailedGameIds.Add(failedGameId);
                    }
                }

                // Log the deduplicated master list of failed game IDs
                if (masterFailedGameIds.Count > 0)
                {
                    _logger.LogWarning("Failed to fetch details for {Count} unique games. AppIDs: {FailedGameIds}", masterFailedGameIds.Count, string.Join(", ", masterFailedGameIds));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating owned games for Steam IDs {SteamIds}", steamIds);
                throw;
            }
        }
        public async Task<GameDetails?> UpdateGameInfoAsync(int appId)
        {
            if (_blacklistedAppIds.Contains(appId))
            {
                _logger.LogWarning("Attempted to process blacklisted AppID {AppId}.", appId);
                throw new BlacklistedAppIdException(appId);
            }

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