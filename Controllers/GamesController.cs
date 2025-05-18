using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using NerdHub.Models;
using NerdHub.Services;
using NerdHub.Services.Interfaces;

namespace NerdHub.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GamesController : ControllerBase
    {
        private readonly SteamService _steamService;
        private readonly UserMappingService _userMappingService;
        private readonly ILogger<GamesController> _logger;
        private readonly IMongoCollection<GameDetails> _games;
        private readonly IProgressTracker _progressTracker;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _updateResults = new();

        public GamesController(
            SteamService steamService,
            UserMappingService userMappingService,
            IMongoClient client,
            ILogger<GamesController> logger,
            IProgressTracker progressTracker)
        {
            _steamService = steamService;
            _userMappingService = userMappingService;
            _logger = logger;
            _progressTracker = progressTracker;

            var database = client.GetDatabase("NH-Games");
            _games = database.GetCollection<GameDetails>("games");
        }
        
        [HttpPost("update-owned-games")]
        [ProducesResponseType(200)] // OK
        [ProducesResponseType(400)] // Bad Request
        [ProducesResponseType(500)] // Internal Server Error
        public async Task<IActionResult> UpdateOwnedGames([FromBody] string steamIds, bool overrideExisting = false, [FromQuery] List<int>? appIdsToUpdate = null)
        {
            if (string.IsNullOrWhiteSpace(steamIds))
            {
                _logger.LogWarning("Invalid input: steamIds is null or empty.");
                return BadRequest("Steam IDs cannot be null or empty.");
            }

            try
            {
                // Call the service method and capture the result
                var operationId = Guid.NewGuid().ToString();
                var result = await _steamService.UpdateOwnedGamesAsync(steamIds, overrideExisting, appIdsToUpdate, operationId);

                // Return a detailed response
                return Ok(new
                {
                    Message = "Owned games update completed.",
                    result.UpdatedGamesCount,
                    result.SkippedGamesCount,
                    result.FailedGamesCount,
                    result.FailedToFetchGameDetails,
                    result.SkippedNotInUpdateList,
                    result.SkippedDueToBlacklist
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating owned games for Steam IDs: {SteamIds}", steamIds);
                return StatusCode(500, "An error occurred while updating owned games.");
            }
        }

        [HttpPost("start-update")]
        [ProducesResponseType(200)] // OK
        [ProducesResponseType(400)] // Bad Request
        [ProducesResponseType(500)] // Internal Server Error
        public IActionResult StartUpdate([FromBody] string steamIds, [FromQuery] bool overrideExisting = false, [FromQuery] List<int>? appIdsToUpdate = null)
        {
            if (string.IsNullOrWhiteSpace(steamIds))
            {
                _logger.LogWarning("Invalid input: steamIds is null or empty.");
                return BadRequest("Steam IDs cannot be null or empty.");
            }

            try
            {
                // Generate a unique operation ID
                var operationId = Guid.NewGuid().ToString();

                // Initialize progress tracking
                _progressTracker.SetProgress(operationId, 0, "Initializing", "Starting the update process...");

                // Start the update process asynchronously (fire-and-forget)
                Task.Run(async () =>
                {
                    try
                    {
                        var result = await _steamService.UpdateOwnedGamesAsync(steamIds, overrideExisting, appIdsToUpdate, operationId);

                        // Store the result for later retrieval
                        _updateResults[operationId] = new
                        {
                            Message = "Owned games update completed.",
                            result.UpdatedGamesCount,
                            result.SkippedGamesCount,
                            result.FailedGamesCount,
                            result.FailedToFetchGameDetails,
                            result.SkippedNotInUpdateList,
                            result.SkippedDueToBlacklist
                        };

                        _progressTracker.SetProgress(operationId, 100, "Completed", "Update process completed.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An error occurred during the update process for operationId: {OperationId}", operationId);
                        _progressTracker.SetProgress(operationId, 100, "Failed", "An error occurred during the update process.");
                        _updateResults[operationId] = new { Error = "An error occurred during the update process." };
                    }
                });

                // Immediately return the operation ID to the client
                return Ok(new { operationId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while starting the update process for Steam IDs: {SteamIds}", steamIds);
                return StatusCode(500, "An error occurred while starting the update process.");
            }
        }

        [HttpGet]
        [ProducesResponseType(200)] // OK
        [ProducesResponseType(500)] // Internal Server Error
        public async Task<IActionResult> GetAllGames()
        {
            try
            {
                var games = await _steamService.GetAllGamesAsync();
                return Ok(games);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching all games.");
                return StatusCode(500, "An error occurred while fetching all games.");
            }
        }

        [HttpGet("{appid}")]
        [ProducesResponseType(200)] // OK
        [ProducesResponseType(404)] // Not Found
        [ProducesResponseType(500)] // Internal Server Error
        public async Task<IActionResult> GetGameById(int appid)
        {
            try
            {
                var game = await _steamService.GetGameByIdAsync(appid);
                if (game == null)
                {
                    return NotFound($"Game with AppID {appid} not found.");
                }

                return Ok(game);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching the game with AppID {appid}.", appid);
                return StatusCode(500, "An error occurred while fetching the game.");
            }
        }

        [HttpPost("update-game-info/{appid}")]
        [ProducesResponseType(200)] // OK
        [ProducesResponseType(404)] // Not Found
        [ProducesResponseType(500)] // Internal Server Error
        public async Task<IActionResult> UpdateGameInfo(int appid)
        {
            try
            {
                var updatedGame = await _steamService.UpdateGameInfoAsync(appid);
                if (updatedGame == null)
                {
                    return NotFound(new { message = $"Game with AppID {appid} not found or could not be updated." });
                }

                return Ok(new { message = $"Game with AppID {appid} updated successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating the game with AppID {appid}.", appid);
                return StatusCode(500, new { message = "An error occurred while updating the game." });
            }
        }

        [HttpPost("get-usernames")]
        [ProducesResponseType(200)] // OK
        [ProducesResponseType(500)] // Internal Server Error
        public async Task<IActionResult> GetUsernames([FromBody] List<string> steamIds)
        {
            try
            {
                var usernames = new Dictionary<string, object>();
                foreach (var steamId in steamIds)
                {
                    var mapping = await _userMappingService.GetUserMappingAsync(steamId);
                    usernames[steamId] = new
                    {
                        username = mapping?.Username ?? "Unknown User",
                        nickname = mapping?.Nickname
                    };
                }
                return Ok(usernames);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch usernames.");
                return StatusCode(500, "An error occurred while fetching usernames.");
            }
        }

        [HttpPost("add-or-update-user-mapping")]
        [ProducesResponseType(200)] // OK
        [ProducesResponseType(400)] // Bad Request
        [ProducesResponseType(500)] // Internal Server Error
        public async Task<IActionResult> AddOrUpdateUserMapping([FromBody] UserMapping userMapping)
        {
            if (userMapping == null || string.IsNullOrEmpty(userMapping.SteamId) || string.IsNullOrEmpty(userMapping.Username))
            {
                _logger.LogWarning("Invalid user mapping data received. SteamId and Username are required.");
                return BadRequest("Invalid user mapping data. SteamId and Username are required.");
            }

            try
            {
                _logger.LogInformation("Received request to add or update user mapping for SteamId: {SteamId}", userMapping.SteamId);
                await _userMappingService.AddOrUpdateUserMappingAsync(
                    userMapping.SteamId, 
                    userMapping.Username, 
                    userMapping.Nickname, 
                    userMapping.DiscordId
                );
                _logger.LogInformation("From Controller: Successfully added or updated user mapping for SteamId: {SteamId}", userMapping.SteamId);
                return Ok(new { message = $"User mapping for SteamId {userMapping.SteamId} updated successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while adding or updating the user mapping for SteamId: {SteamId}", userMapping.SteamId);
                return StatusCode(500, "An error occurred while adding or updating the user mapping.");
            }
        }

        [HttpGet("get-all-usernames")]
        [ProducesResponseType(200)] // OK
        [ProducesResponseType(500)] // Internal Server Error
        public async Task<IActionResult> GetAllUsernames()
        {
            try
            {
                _logger.LogInformation("Received request to fetch all usernames.");
                var userMappings = await _userMappingService.GetAllUserMappingsAsync();
                var response = userMappings.Select(mapping => new
                {
                    SteamId = mapping.SteamId,
                    Username = mapping.Username,
                    Nickname = mapping.Nickname,
                    DiscordId = mapping.DiscordId
                }).ToList();
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching all usernames.");
                return StatusCode(500, "An error occurred while fetching all usernames.");
            }
        }

        [HttpGet("update-progress/{operationId}")]
        [ProducesResponseType(200)] // OK
        [ProducesResponseType(404)] // Not Found
        public IActionResult GetUpdateProgress(string operationId)
        {
            if (!_progressTracker.TryGetProgress(operationId, out var progressInfo) || progressInfo == null)
            {
                _logger.LogWarning("Progress not found for operationId: {OperationId}", operationId);
                return NotFound(new { message = "Operation not found." });
            }

            _logger.LogInformation("Returning progress for operationId: {OperationId} - {ProgressInfo}", operationId, progressInfo);
            if (progressInfo.Phase == "Rate Limited")
            {
                return Ok(new {
                    progress = progressInfo.Progress,
                    phase = progressInfo.Phase,
                    message = progressInfo.Message,
                    retryAfterSeconds = progressInfo.RetryAfterSeconds
                });
            }

            return Ok(progressInfo);
        }

        [HttpGet("update-result/{operationId}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        public IActionResult GetUpdateResult(string operationId)
        {
            if (_updateResults.TryGetValue(operationId, out var result))
            {
                return Ok(result);
            }
            return NotFound(new { message = "Result not found for this operation." });
        }

        [HttpPost("start-price-update")]
        [ProducesResponseType(200)] // OK
        [ProducesResponseType(500)] // Internal Server Error
        public IActionResult StartPriceUpdate()
        {
            try
            {
                var operationId = Guid.NewGuid().ToString();
                _progressTracker.SetProgress(operationId, 0, "Initializing", "Starting price update process...");

                Task.Run(async () =>
                {
                    try
                    {
                        var result = await _steamService.UpdateAllGamePricesAsync(operationId);
                        _progressTracker.SetProgress(operationId, 100, "Completed", "Price update process completed.");
                        _updateResults[operationId] = result;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An error occurred during the price update process for operationId: {OperationId}", operationId);
                        _progressTracker.SetProgress(operationId, 100, "Failed", "An error occurred during the price update process.");
                        _updateResults[operationId] = new { Error = "An error occurred during the price update process." };
                    }
                });

                return Ok(new { operationId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while starting the price update process.");
                return StatusCode(500, "An error occurred while starting the price update process.");
            }
        }
    }
}