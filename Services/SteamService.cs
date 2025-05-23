using NerdHub.Models;
using MongoDB.Driver;
using Newtonsoft.Json;
using NerdHub.Services.Interfaces;

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
        private readonly IProgressTracker _progressTracker;

        public SteamService(IConfiguration configuration, IMongoClient client, ILogger<SteamService> logger, IProgressTracker progressTracker)
        {
            _configuration = configuration;
            _logger = logger;
            _progressTracker = progressTracker;

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
        private async Task<string> FetchWithRetryAndRateLimitAsync(HttpClient httpClient, string url, string operationId, int maxRetries = 15)
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

                        _progressTracker.SetProgress(
                            operationId,
                            0,
                            "Rate Limited",
                            $"Rate limit hit. Retrying in {retryAfter} seconds. Retry attempt {retryCount}."
                        );
                        // After setting progress, also set the RetryAfterSeconds property
                        if (_progressTracker.TryGetProgress(operationId, out var progressInfo) && progressInfo != null)
                        {
                            progressInfo.RetryAfterSeconds = retryAfter;
                        }

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
        private async Task<GameDetails?> FetchGameDetailsAsync(HttpClient httpClient, int appId, string operationId)
        {
            _logger.LogTrace("Fetching game details for AppID {AppId}.", appId);
            try
            {
                var url = $"http://store.steampowered.com/api/appdetails?appids={appId}&l=english";
                var response = await FetchWithRetryAndRateLimitAsync(httpClient, url, operationId);

                var gameData = JsonConvert.DeserializeObject<Dictionary<string, GameDetailsResponse>>(response, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore
                });

                if (gameData != null && gameData.TryGetValue(appId.ToString(), out var gameDetailsResponse) && gameDetailsResponse.success)
                {
                    var gameDetails = gameDetailsResponse.data;

                    // Perform currency conversion if necessary
                    if (gameDetails?.priceOverview != null
                        && !string.IsNullOrEmpty(gameDetails.priceOverview.currency)
                        && gameDetails.priceOverview.currency != "USD")
                    {
                        var exchangeRate = await GetExchangeRateAsync(httpClient, gameDetails.priceOverview.currency, "USD");
                        if (exchangeRate > 0)
                        {
                            gameDetails.priceOverview.initial = gameDetails.priceOverview.initial / exchangeRate;
                            gameDetails.priceOverview.final = gameDetails.priceOverview.final / exchangeRate;
                            gameDetails.priceOverview.currency = "USD";
                            gameDetails.priceOverview.initialFormatted = $"${gameDetails.priceOverview.initial:F2}";
                            gameDetails.priceOverview.finalFormatted = $"${gameDetails.priceOverview.final:F2}";
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
        private async Task<decimal> GetExchangeRateAsync(HttpClient httpClient, string fromCurrency, string toCurrency)
        {
            _logger.LogTrace("Fetching exchange rate from {FromCurrency} to {ToCurrency}.", fromCurrency, toCurrency);
            decimal rate = 0;
            int attempts = 0;
            do
            {
                try
                {
                    var apiKey = _configuration["ExchangeRateApiKey"];
                    var url = $"https://api.exchangerate-api.com/v4/latest/{fromCurrency}";
                    var response = await httpClient.GetStringAsync(url);

                    var exchangeRateData = JsonConvert.DeserializeObject<ExchangeRateResponse>(response);
                    if (exchangeRateData?.Rates != null && exchangeRateData.Rates.TryGetValue(toCurrency, out var fetchedRate))
                    {
                        rate = Convert.ToDecimal(fetchedRate);
                        if (rate < 119.99m)
                            return rate;
                        _logger.LogWarning("Fetched exchange rate {Rate} is too high, retrying... (Attempt {Attempt})", rate, attempts + 1);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch exchange rate from {FromCurrency} to {ToCurrency}", fromCurrency, toCurrency);
                }
                attempts++;
                await Task.Delay(500); // Small delay before retrying
            } while (rate >= 119.99m && attempts < 5);

            return 0; // Return 0 if the exchange rate could not be fetched or is invalid
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
        public async Task<UpdateOwnedGamesResult> UpdateOwnedGamesAsync(string steamIds, bool overrideExisting, List<int>? appIdsToUpdate, string operationId)
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
                _logger.LogTrace("Provided list of AppIDs to update: {AppIdsToUpdate}", string.Join(", ", appIdsToUpdate!));
            }

            var result = new UpdateOwnedGamesResult();
            var httpClient = new HttpClient();
            var steamApiKey = _configuration["Steam:ApiKey"];
            var steamIdList = steamIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var writeModels = new List<WriteModel<GameDetails>>();
            var steamOwnedGames = new Dictionary<string, List<int>>();
            var fetchedGameDetailsCache = new Dictionary<int, GameDetails>();

            try
            {
                // Step 1: Fetch owned games for each Steam ID
                int totalSteamIds = steamIdList.Length;
                int processedSteamIds = 0;
                foreach (var steamId in steamIdList)
                {
                    var url = $"http://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={steamApiKey}&steamid={steamId}&format=json";
                    var response = await FetchWithRetryAndRateLimitAsync(httpClient, url, operationId);

                    var apiResponse = JsonConvert.DeserializeObject<SteamApiResponse>(response, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        MissingMemberHandling = MissingMemberHandling.Ignore
                    });

                    if (apiResponse?.response?.games != null)
                    {
                        steamOwnedGames[steamId] = apiResponse.response.games
                            .Where(g => g.appid != null)
                            .Select(g => g.appid!.Value)
                            .ToList();
                        _logger.LogInformation("Successfully fetched owned games for Steam ID {SteamId}.", steamId);
                    }
                    else
                    {
                        _logger.LogWarning("No games found for Steam ID {SteamId}.", steamId);
                        steamOwnedGames[steamId] = new List<int>();
                    }

                    processedSteamIds++;
                    int progress = (int)(10 * (processedSteamIds / (double)totalSteamIds)); // Scale to 10% max
                    _progressTracker.SetProgress(
                        operationId,
                        progress,
                        "Fetching Owned Games",
                        $"Fetched {processedSteamIds} of {totalSteamIds} Steam IDs... (Current SteamID: {steamId})"
                    );
                    _logger.LogInformation("Progress updated: {Progress}% - {Phase} - {Message}", progress, "Fetching Owned Games", $"Fetched {processedSteamIds} of {totalSteamIds} Steam IDs... (Current SteamID: {steamId})");
                }

                // Step 2: Process each game
                int totalGames = steamOwnedGames.Values.Sum(games => games.Count);
                result.TotalGamesCount = totalGames;
                int processedGames = 0;

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

                        if (await IsAppIdBlacklisted(appId))
                        {
                            _logger.LogInformation("Skipping blacklisted AppID {AppId}.", appId);
                            result.SkippedDueToBlacklist.Add(appId);
                            result.SkippedGamesCount++;
                            continue;
                        }

                        await _rateLimitSemaphore.WaitAsync();
                        try
                        {
                            var filter = Builders<GameDetails>.Filter.Eq(g => g.appid, appId);
                            var existingGame = await _games.Find(filter).FirstOrDefaultAsync();

                            if (existingGame != null && !overrideExisting)
                            {
                                _logger.LogInformation("Game with AppID {AppId} already exists. Updating ownedBy field.", appId);
                                existingGame.LastModifiedTime = DateTime.UtcNow.ToString("o");

                                if (existingGame.ownedBy == null)
                                {
                                    existingGame.ownedBy = new OwnedBy();
                                }

                                if (existingGame.ownedBy.steamId == null)
                                {
                                    existingGame.ownedBy.steamId = new List<string>();
                                }

                                bool changesMade = false;
                                foreach (var dictionarySteamId in steamOwnedGames.Keys)
                                {
                                    if (steamOwnedGames[dictionarySteamId].Contains(appId))
                                    {
                                        if (!existingGame.ownedBy.steamId.Contains(dictionarySteamId))
                                        {
                                            existingGame.ownedBy.steamId.Add(dictionarySteamId);
                                            changesMade = true;
                                        }
                                    }
                                }
                                if (changesMade)
                                {
                                    var upsertModel = new ReplaceOneModel<GameDetails>(
                                        Builders<GameDetails>.Filter.Eq(g => g.appid, existingGame.appid),
                                        existingGame
                                    )
                                    {
                                        IsUpsert = true
                                    };
                                    writeModels.Add(upsertModel);
                                    result.UpdatedGamesCount++;
                                    _logger.LogInformation("Queued OwnedBy Update for AppID {AppId}.", appId);
                                }
                                else
                                {
                                    _logger.LogInformation("No changes made for AppID {AppId}. Skipping.", appId);
                                }
                            }
                            else
                            {
                                if (overrideExisting)
                                {
                                    _logger.LogInformation("Game with AppId {AppId} does exist in the database, but override existing is set to True.", appId);
                                }
                                else
                                {
                                    _logger.LogInformation("Game with AppId {AppId} does not exist in the database. Adding.", appId);
                                }
                                if (!fetchedGameDetailsCache.TryGetValue(appId, out var fetchedGameDetails))
                                {
                                    _logger.LogInformation("Fetching game details for AppID {AppId}.", appId);
                                    fetchedGameDetails = await FetchGameDetailsAsync(httpClient, appId, operationId);

                                    if (fetchedGameDetails != null)
                                    {
                                        fetchedGameDetails.LastModifiedTime = DateTime.UtcNow.ToString("o");
                                        fetchedGameDetailsCache[appId] = fetchedGameDetails;

                                        var upsertModel = new ReplaceOneModel<GameDetails>(
                                            Builders<GameDetails>.Filter.Eq(g => g.appid, fetchedGameDetails.appid),
                                            fetchedGameDetails
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
                                        result.FailedToFetchGameDetails.Add(appId);
                                        result.FailedGamesCount++;
                                        continue;
                                    }
                                }

                                // Treat the fetched game details as an existing game
                                if (fetchedGameDetails.ownedBy == null)
                                {
                                    fetchedGameDetails.ownedBy = new OwnedBy();
                                }

                                if (fetchedGameDetails.ownedBy.steamId == null)
                                {
                                    fetchedGameDetails.ownedBy.steamId = new List<string>();
                                }

                                foreach (var dictionarySteamId in steamOwnedGames.Keys)
                                {
                                    if (steamOwnedGames[dictionarySteamId].Contains(appId))
                                    {
                                        if (!fetchedGameDetails.ownedBy.steamId.Contains(dictionarySteamId))
                                        {
                                            fetchedGameDetails.ownedBy.steamId.Add(dictionarySteamId);
                                            _logger.LogInformation("Added Steam ID {SteamId} to the ownedBy list for AppID {AppId}.", dictionarySteamId, appId);
                                        }
                                    }
                                    else
                                    {
                                        _logger.LogInformation("Steam ID {SteamId} does not own AppID {AppId}. Skipping", dictionarySteamId, appId);
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing game with AppID {AppId}.", appId);
                            result.FailedGamesCount++;
                        }
                        finally
                        {
                            _rateLimitSemaphore.Release();
                        }

                        // Update progress
                        processedGames++;
                        int progress = 10 + (int)((processedGames / (double)totalGames) * 80); // Scale to 80% max
                        _progressTracker.SetProgress(
                            operationId,
                            progress,
                            "Processing Games",
                            $"Processed {processedGames} of {totalGames} games... (Current AppID: {appId})"
                        );
                        _logger.LogInformation("Progress updated: {Progress}% - {Phase} - {Message}", progress, "Processing Games", $"Processed {processedGames} of {totalGames} games... (Current AppID: {appId})");
                    }
                }

                // Step 3: Perform bulk write
                if (writeModels.Count > 0)
                {
                    await _games.BulkWriteAsync(writeModels);
                    _logger.LogInformation("Successfully performed bulk write for {Count} operations.", writeModels.Count);
                }

                // Finalize progress
                _progressTracker.SetProgress(operationId, 100, "Completed", "Update process completed.");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating owned games for Steam IDs {SteamIds}", steamIds);
                _progressTracker.SetProgress(operationId, 100, "Failed", "An error occurred during the update process.");
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
                var operationId = Guid.NewGuid().ToString();
                var gameDetails = await FetchGameDetailsAsync(httpClient, appId, operationId);

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
        public async Task<GameDetails?> GetGameByIdAsync(int appId)
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
        public async Task<UpdateGamePricesResult> UpdateAllGamePricesAsync(string operationId)
        {
            _logger.LogInformation("Starting bulk price overview update for all games.");

            var allGames = await _games.Find(_ => true).Project(g => new { g.appid }).ToListAsync();
            var allAppIds = allGames.Select(g => g.appid).ToList();

            int batchSize = 500;
            int totalBatches = (int)Math.Ceiling(allAppIds.Count / (double)batchSize);
            int processed = 0;

            var httpClient = new HttpClient();
            var writeModels = new List<WriteModel<GameDetails>>();

            var result = new UpdateGamePricesResult
            {
                TotalGamesCount = allAppIds.Count
            };

            _progressTracker.SetProgress(operationId, 0, "Initializing", "Starting price update process...");

            for (int batch = 0; batch < totalBatches; batch++)
            {
                var batchAppIds = allAppIds.Skip(batch * batchSize).Take(batchSize).Where(id => id != null).Select(id => id!.Value).ToList();
                var appIdsString = string.Join(",", batchAppIds);

                var url = $"https://store.steampowered.com/api/appdetails?appids={appIdsString}&filters=price_overview";
                string response;
                try
                {
                    response = await FetchWithRetryAndRateLimitAsync(httpClient, url, operationId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch price overviews for batch {Batch}.", batch + 1);
                    result.FailedGamesCount += batchAppIds.Count;
                    result.FailedAppIds.AddRange(batchAppIds);
                    continue;
                }

                var priceData = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(response);

                foreach (var appIdStr in batchAppIds.Select(id => id.ToString()))
                {
                    dynamic? appData = null;
                    if (priceData != null && priceData.TryGetValue(appIdStr, out appData) && appData != null && appData!.success == true && appData!.data != null)
                    {
                        if (appData?.data is Newtonsoft.Json.Linq.JObject dataObj && dataObj["price_overview"] != null)
                        {
                            var priceOverviewJson = dataObj["price_overview"]?.ToString();
                            var priceOverview = !string.IsNullOrEmpty(priceOverviewJson)
                                ? JsonConvert.DeserializeObject<PriceOverview>(priceOverviewJson)
                                : null;

                            if (priceOverview != null)
                            {
                                // Fetch the current priceOverview from the database
                                var filter = Builders<GameDetails>.Filter.Eq(g => g.appid, int.Parse(appIdStr));
                                var existingGame = await _games.Find(filter).FirstOrDefaultAsync();

                                bool shouldUpdate = false;
                                if (existingGame == null || existingGame.priceOverview == null)
                                {
                                    shouldUpdate = true;
                                }
                                else
                                {
                                    // Compare the important fields of priceOverview
                                    var db = existingGame.priceOverview;
                                    var incoming = priceOverview;
                                    if (
                                        db.discountPercent != incoming.discountPercent ||
                                        db.initialFormatted != incoming.initialFormatted ||
                                        db.finalFormatted != incoming.finalFormatted
                                    )
                                    {
                                        shouldUpdate = true;
                                    }
                                }

                                if (shouldUpdate)
                                {
                                    var update = Builders<GameDetails>.Update
                                        .Set("priceOverview", priceOverview)
                                        .Set("LastModifiedTime", DateTime.UtcNow.ToString("o"));

                                    writeModels.Add(new UpdateOneModel<GameDetails>(filter, update));
                                    result.UpdatedGamesCount++;
                                    result.UpdatedAppIds.Add(int.Parse(appIdStr));
                                }
                                else
                                {
                                    result.SkippedGamesCount++;
                                    result.SkippedAppIds.Add(int.Parse(appIdStr));
                                }
                            }
                            else
                            {
                                result.SkippedGamesCount++;
                                result.SkippedAppIds.Add(int.Parse(appIdStr));
                            }
                        }
                        else
                        {
                            result.SkippedGamesCount++;
                            result.SkippedAppIds.Add(int.Parse(appIdStr));
                        }
                    }
                    else
                    {
                        result.FailedGamesCount++;
                        result.FailedAppIds.Add(int.Parse(appIdStr));
                    }
                }

                processed += batchAppIds.Count;
                int progress = (int)((double)processed / allAppIds.Count * 100);
                _progressTracker.SetProgress(
                    operationId,
                    progress,
                    "Updating Prices",
                    $"Processed {processed} of {allAppIds.Count} games for price update. (Batch {batch + 1} of {totalBatches})"
                );
                _logger.LogInformation("Processed {Processed} of {Total} games for price update.", processed, allAppIds.Count);
            }

            if (writeModels.Count > 0)
            {
                await _games.BulkWriteAsync(writeModels);
                _logger.LogInformation("Bulk price update completed for {Count} games.", writeModels.Count);
            }
            else
            {
                _logger.LogInformation("No price updates were necessary.");
            }

            _progressTracker.SetProgress(operationId, 100, "Completed", "Price update process completed.");
            return result;
        }
    }
}