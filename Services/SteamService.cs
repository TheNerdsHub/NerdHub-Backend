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
        private readonly IMongoCollection<BlacklistedAppId> _blacklistedAppIdsCollection;
        private readonly ILogger<SteamService> _logger;
        private readonly SemaphoreSlim _rateLimitSemaphore = new SemaphoreSlim(3, 3); // Allow up to 3 concurrent requests
        private readonly TimeSpan _rateLimitDelay = TimeSpan.FromSeconds(1); // 1-second delay for rate limiting
        private HashSet<int> _blacklistedAppIds = new HashSet<int>(); // Cached blacklist

        public SteamService(IConfiguration configuration, IMongoClient client, ILogger<SteamService> logger)
        {
            _configuration = configuration;
            _logger = logger;

            var database = client.GetDatabase("NH-Games");
            _games = database.GetCollection<GameDetails>("games");
            _blacklistedAppIdsCollection = database.GetCollection<BlacklistedAppId>("appBlacklist");

            // Load the blacklist from the database
            LoadBlacklistFromDatabase().Wait();
        }

        private async Task LoadBlacklistFromDatabase()
        {
            _logger.LogTrace("Loading blacklisted AppIDs from the database.");
            try
            {
                // Project only the AppId field from the database
                var blacklistedAppIds = await _blacklistedAppIdsCollection
                    .Find(_ => true)
                    .Project(b => b.AppId)
                    .ToListAsync();

                _blacklistedAppIds = blacklistedAppIds.ToHashSet();
                _logger.LogInformation("Loaded {Count} blacklisted AppIDs from the database.", _blacklistedAppIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load blacklisted AppIDs from the database.");
                throw;
            }
        }

        private async Task<bool> IsAppIdBlacklisted(int appId)
        {
            _logger.LogDebug("Checking if AppID {AppId} is blacklisted.", appId);
            // Check if the app ID is in the cached blacklist
            if (_blacklistedAppIds.Contains(appId))
            {
                return true;
            }

            // If not in cache, check the database
            var isBlacklisted = await _blacklistedAppIdsCollection.Find(b => b.AppId == appId).AnyAsync();
            if (isBlacklisted)
            {
                _blacklistedAppIds.Add(appId); // Add to cache
            }

            return isBlacklisted;
        }

        private async Task<string> FetchWithRetryAndRateLimitAsync(HttpClient httpClient, string url, int maxRetries = 15)
        {
            _logger.LogTrace("Fetching URL: {Url} with retry and rate limit enabled.", url);
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
                        var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? Math.Pow(2, retryCount) * 30;
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
            _logger.LogTrace("Fetching game details for AppID {AppId}.", appId);
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
                    _logger.LogInformation("Successfully fetched game details for AppID {AppId}.", appId);
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
            _logger.LogTrace("Fetching exchange rate from {FromCurrency} to {ToCurrency}.", fromCurrency, toCurrency);
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
        public async Task UpdateOwnedGamesAsync(string steamIds, bool overrideExisting, List<int>? appIdsToUpdate = null)
        {
            try
            {
                var httpClient = new HttpClient();
                var steamApiKey = _configuration["Steam:ApiKey"];
                var steamIdList = steamIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var masterFailedGameIds = new HashSet<int>(); // Deduplicated master list of failed game IDs
                var skippedBlacklistedGameIds = new HashSet<int>(); // Deduplicated list of skipped blacklisted AppIDs
                var writeModels = new List<WriteModel<GameDetails>>(); // List of write models for bulk operations

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

                   foreach (var game in apiResponse.response.games)
                   {
                        // If appIdsToUpdate is provided, skip games not in the list
                        if (appIdsToUpdate != null && !appIdsToUpdate.Contains((int)game.appid))
                        {
                            _logger.LogInformation("Skipping AppID {AppId} as it is not in the provided list of AppIDs to update.", game.appid);
                            continue;
                        }

                        // Check if the app ID is blacklisted
                        if (await IsAppIdBlacklisted((int)game.appid))
                        {
                            _logger.LogInformation("Skipping blacklisted AppID {AppId}.", game.appid);
                            skippedBlacklistedGameIds.Add((int)game.appid); // Add to skipped list
                            continue;
                        }

                        await _rateLimitSemaphore.WaitAsync(); // Respect rate limits
                        try
                        {
                            var filter = Builders<GameDetails>.Filter.Eq(g => g.appid, game.steam_appid);
                            var existingGame = await _games.Find(filter).FirstOrDefaultAsync();

                            var gameDetails = await FetchGameDetailsAsync(httpClient, (int)game.steam_appid);
                            if (gameDetails != null)
                            {
                                // Check if the appid in the response matches the current appid
                                if (gameDetails.appid != game.steam_appid)
                                {
                                    _logger.LogWarning("Mismatch in AppID. Expected: {ExpectedAppId}, Received: {ReceivedAppId}. Likely needs to be added to the blacklist. Skipping.", game.steam_appid, gameDetails.appid);
                                    masterFailedGameIds.Add((int)game.steam_appid); // Add to failed list
                                    continue;
                                }

                                gameDetails.LastModifiedTime = DateTime.UtcNow.ToString("o");

                                if (existingGame != null && !overrideExisting)
                                {
                                    // Update the ownedBy list for the existing game
                                    if (existingGame.ownedBy == null)
                                    {
                                        existingGame.ownedBy = new OwnedBy();
                                    }

                                    if (existingGame.ownedBy.steamId == null)
                                    {
                                        existingGame.ownedBy.steamId = new List<string>();
                                    }

                                    if (!existingGame.ownedBy.steamId.Contains(steamId))
                                    {
                                        existingGame.ownedBy.steamId.Add(steamId);
                                    }

                                    existingGame.LastModifiedTime = DateTime.UtcNow.ToString("o");

                                    // Add an update model for the existing game
                                    var updateModel = new ReplaceOneModel<GameDetails>(
                                        Builders<GameDetails>.Filter.Eq(g => g.appid, existingGame.appid),
                                        existingGame
                                    );
                                    writeModels.Add(updateModel);

                                    // Log the WriteModel
                                    _logger.LogInformation("Queued Replace for existing game with AppID {AppId}.", existingGame.appid);
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

                                    if (!gameDetails.ownedBy.steamId.Contains(steamId))
                                    {
                                        gameDetails.ownedBy.steamId.Add(steamId);
                                    }

                                    // Add an upsert model for the new or updated game
                                    var upsertModel = new ReplaceOneModel<GameDetails>(
                                        Builders<GameDetails>.Filter.Eq(g => g.appid, gameDetails.appid),
                                        gameDetails
                                    )
                                    {
                                        IsUpsert = true
                                    };
                                    writeModels.Add(upsertModel);

                                    // Log the WriteModel
                                    if (existingGame == null)
                                    {
                                        _logger.LogInformation("Queued Upsert for new AppID {AppId}.", gameDetails.appid);
                                    }
                                    else
                                    {
                                        _logger.LogInformation("Queued ReplaceOneModel for updated game with AppID {AppId}.", gameDetails.appid);
                                    }
                                }
                            }
                            else
                            {
                                masterFailedGameIds.Add((int)game.steam_appid); // Add to failed list if details could not be fetched
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing game with AppID {AppId}.", game.steam_appid);
                            masterFailedGameIds.Add((int)game.steam_appid); // Add to failed list on exception
                        }
                        finally
                        {
                            _rateLimitSemaphore.Release();
                        }
                    }
                }

                if (writeModels.Count > 0)
                {
                    await _games.BulkWriteAsync(writeModels);
                    _logger.LogInformation("Successfully performed bulk write for {Count} operations.", writeModels.Count);
                }

                // Log the deduplicated master list of failed game IDs
                if (masterFailedGameIds.Count > 0)
                {
                    _logger.LogWarning("Failed to fetch details for {Count} unique games. AppIDs: {FailedGameIds}", masterFailedGameIds.Count, string.Join(", ", masterFailedGameIds));
                }

                // Log the deduplicated list of skipped blacklisted AppIDs
                if (skippedBlacklistedGameIds.Count > 0)
                {
                    _logger.LogInformation("Skipped {Count} blacklisted games. AppIDs: {SkippedGameIds}", skippedBlacklistedGameIds.Count, string.Join(", ", skippedBlacklistedGameIds));
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
            if (await IsAppIdBlacklisted(appId))
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

        public async Task<List<GameDetails>> GetAllGamesAsync()
        {
            _logger.LogTrace("Received request to GetAllGames.");
            try
            {
                return await _games.Find(_ => true).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving all games from the database.");
                throw;
            }
        }
        public async Task<GameDetails> GetGameByIdAsync(int appId)
        {
            _logger.LogTrace("Received request to GetGameById with AppID {AppId}.", appId);
            try
            {
                var game = await _games.Find(g => g.appid == appId).FirstOrDefaultAsync();
                if (game == null)
                {
                    _logger.LogWarning("Game with AppID {AppId} was not found in the database.", appId);
                }
                _logger.LogInformation("Successfully got {AppId}.", appId);
                return game;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the AppID {AppId} from the database.", appId);
                throw;
            }
        }
    }
}