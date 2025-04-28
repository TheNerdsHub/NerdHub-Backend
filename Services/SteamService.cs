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

        private async Task RefreshBlacklistAsync()
        {
            _logger.LogTrace("Refreshing blacklisted AppIDs from the database.");
            try
            {
                var blacklistedAppIds = await _blacklistedAppIdsCollection
                    .Find(_ => true)
                    .Project(b => b.AppId)
                    .ToListAsync();

                _blacklistedAppIds = blacklistedAppIds.ToHashSet();
                _logger.LogInformation("Refreshed {Count} blacklisted AppIDs from the database.", _blacklistedAppIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to refresh blacklisted AppIDs from the database.");
                throw;
            }
        }

        public async Task<UpdateOwnedGamesResult> UpdateOwnedGamesAsync(string steamIds, bool overrideExisting, List<int>? appIdsToUpdate = null)
        {
            await RefreshBlacklistAsync(); // Refresh blacklist at the start

            _logger.LogTrace("Received request to update owned games for Steam IDs: {SteamIds}", steamIds);
            if (appIdsToUpdate == null)
            {
                _logger.LogTrace("No AppIDs provided to update. Proceeding with all owned games.");
            }
            else if (appIdsToUpdate != null && appIdsToUpdate.Count == 0)
            {
                _logger.LogWarning("Provided list of AppIDs to update is empty.");
                throw new ArgumentException("App IDs to update cannot be an empty list.");
            }
            else
            {
                _logger.LogTrace("Provided list of AppIDs to update: {AppIdsToUpdate}", string.Join(", ", appIdsToUpdate));
            }

            var result = new UpdateOwnedGamesResult();

            try
            {
                var httpClient = new HttpClient();
                var steamApiKey = _configuration["Steam:ApiKey"];
                var steamIdList = steamIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                var writeModels = new List<WriteModel<GameDetails>>(); // List of write models for bulk operations
                var steamOwnedGames = new Dictionary<string, List<int>>(); // Dictionary to store owned games for each Steam ID
                var apiResponse = new SteamApiResponse(); // Declare apiResponse outside the loop

                foreach (var steamId in steamIdList)
                {
                    var url = $"http://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={steamApiKey}&steamid={steamId}&format=json";

                    var response = await FetchWithRetryAndRateLimitAsync(httpClient, url);
                    apiResponse = JsonConvert.DeserializeObject<SteamApiResponse>(response, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        MissingMemberHandling = MissingMemberHandling.Ignore
                    });
                    _logger.LogInformation("Successfully fetched owned games for Steam ID {SteamId}.", steamId);

                    if (apiResponse?.response?.games != null)
                    {
                        steamOwnedGames[steamId] = apiResponse.response.games.Select(g => (int)g.appid).ToList();
                    }
                    else
                    {
                        _logger.LogWarning("No games found for Steam ID {SteamId}. Response: {ApiResponse}", steamId, response);
                        steamOwnedGames[steamId] = new List<int>(); // Add an empty list for this Steam ID
                    }
                }

                // Iterate through all games in the database
                foreach (var kvp in steamOwnedGames)
                {
                    var steamId = kvp.Key;
                    var ownedGames = kvp.Value;

                    if (ownedGames == null)
                    {
                        _logger.LogWarning("No owned games found for Steam ID {SteamId}. Skipping.", steamId);
                        continue;
                    }

                    foreach (var appId in ownedGames)
                    {
                        if (appIdsToUpdate != null && !appIdsToUpdate.Contains(appId))
                        {
                            _logger.LogInformation("Skipping AppID {AppId} as it is not in the provided list of AppIDs to update.", appId);
                            result.SkippedGamesCount++;
                            continue;
                        }

                        // Check if the app ID is blacklisted
                        if (await IsAppIdBlacklisted(appId))
                        {
                            _logger.LogInformation("Skipping blacklisted AppID {AppId}.", appId);
                            result.SkippedBlacklistedGameIds.Add(appId);
                            result.SkippedGamesCount++;
                            continue;
                        }

                        await _rateLimitSemaphore.WaitAsync(); // Respect rate limits
                        try
                        {
                            var filter = Builders<GameDetails>.Filter.Eq(g => g.appid, appId);
                            var existingGame = await _games.Find(filter).FirstOrDefaultAsync();

                            if (existingGame != null && !overrideExisting)
                            {
                                _logger.LogInformation("Game with AppID {AppId} already exists in the database. Updating ownedBy field.", appId);
                                existingGame.LastModifiedTime = DateTime.UtcNow.ToString("o");

                                if (existingGame.ownedBy == null)
                                {
                                    existingGame.ownedBy = new OwnedBy();
                                }

                                if (existingGame.ownedBy.steamId == null)
                                {
                                    existingGame.ownedBy.steamId = new List<string>();
                                }

                                foreach (var dictionarySteamId in steamOwnedGames.Keys)
                                {
                                    if (steamOwnedGames[dictionarySteamId].Contains(appId))
                                    {
                                        if (!existingGame.ownedBy.steamId.Contains(dictionarySteamId))
                                        {
                                            existingGame.ownedBy.steamId.Add(dictionarySteamId);
                                        }
                                    }
                                }

                                var upsertModel = new ReplaceOneModel<GameDetails>(
                                    Builders<GameDetails>.Filter.Eq(g => g.appid, existingGame.appid),
                                    existingGame
                                )
                                {
                                    IsUpsert = true
                                };
                                writeModels.Add(upsertModel);
                                result.UpdatedGamesCount++;
                            }
                            else
                            {
                                if (overrideExisting)
                                {
                                    _logger.LogInformation("Game with AppID {AppId} already exists in the database but overrideExists is True. Overriding existing game.", appId);
                                }
                                else
                                {
                                    _logger.LogInformation("Game with AppID {AppId} does not exist in the database. Adding new game.", appId);
                                }
                                var gameDetails = await FetchGameDetailsAsync(httpClient, appId);
                                if (gameDetails != null)
                                {
                                    gameDetails.LastModifiedTime = DateTime.UtcNow.ToString("o");

                                    gameDetails.ownedBy = new OwnedBy();
                                    gameDetails.ownedBy.steamId = new List<string>();

                                    foreach (var dictionarySteamId in steamOwnedGames.Keys)
                                    {
                                        if (steamOwnedGames[dictionarySteamId].Contains(appId))
                                        {
                                            if (!gameDetails.ownedBy.steamId.Contains(dictionarySteamId))
                                            {
                                                gameDetails.ownedBy.steamId.Add(dictionarySteamId);
                                                _logger.LogInformation("Added Steam ID {SteamId} to the ownedBy list for AppID {AppId}.", dictionarySteamId, appId);
                                            }
                                        }
                                        else
                                        {
                                            _logger.LogInformation("Steam ID {SteamId} does not own AppID {AppId}. Skipping", dictionarySteamId, appId);
                                        }
                                    }

                                    var upsertModel = new ReplaceOneModel<GameDetails>(
                                        Builders<GameDetails>.Filter.Eq(g => g.appid, gameDetails.appid),
                                        gameDetails
                                    )
                                    {
                                        IsUpsert = true
                                    };
                                    writeModels.Add(upsertModel);
                                    result.UpdatedGamesCount++;
                                }
                                else
                                {
                                    _logger.LogWarning("Failed to fetch game details for AppID {AppId}.", appId);
                                    result.FailedGameIds.Add(appId);
                                    result.FailedGamesCount++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing game with AppID {AppId}.", appId);
                            result.FailedGameIds.Add(appId);
                            result.FailedGamesCount++;
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

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating owned games for Steam IDs {SteamIds}", steamIds);
                throw;
            }
        }

        public async Task<GameDetails?> UpdateGameInfoAsync(int appId)
        {
            await RefreshBlacklistAsync(); // Refresh blacklist at the start

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
                _logger.LogInformation("Successfully fetched {AppId} from the database.", appId);
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