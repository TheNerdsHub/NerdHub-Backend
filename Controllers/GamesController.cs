using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using NerdHub.Models;
using NerdHub.Services;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

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

        public GamesController(SteamService steamService, UserMappingService userMappingService, IMongoClient client, ILogger<GamesController> logger)
        {
            _steamService = steamService;
            _userMappingService = userMappingService;
            _logger = logger;

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
                var result = await _steamService.UpdateOwnedGamesAsync(steamIds, overrideExisting, appIdsToUpdate);

                // Return a detailed response
                return Ok(new
                {
                    Message = "Owned games update completed.",
                    UpdatedGamesCount = result.UpdatedGamesCount,
                    SkippedGamesCount = result.SkippedGamesCount,
                    FailedGamesCount = result.FailedGamesCount,
                    FailedGameIds = result.FailedGameIds,
                    SkippedBlacklistedGameIds = result.SkippedBlacklistedGameIds
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating owned games for Steam IDs: {SteamIds}", steamIds);
                return StatusCode(500, "An error occurred while updating owned games.");
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
                    return NotFound($"Game with AppID {appid} not found or could not be updated.");
                }

                return Ok($"Game with AppID {appid} updated successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating the game with AppID {appid}.", appid);
                return StatusCode(500, "An error occurred while updating the game.");
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
                await _userMappingService.AddOrUpdateUserMappingAsync(userMapping.SteamId, userMapping.Username, userMapping.Nickname);
                _logger.LogInformation("Successfully added or updated user mapping for SteamId: {SteamId}", userMapping.SteamId);
                return Ok($"User mapping for SteamId {userMapping.SteamId} updated successfully.");
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
                    Nickname = mapping.Nickname
                }).ToList();
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching all usernames.");
                return StatusCode(500, "An error occurred while fetching all usernames.");
            }
        }
    }
}