using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using NerdHub.Models;
using NerdHub.Services;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace NerdHub.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GamesController : ControllerBase
    {
        private readonly SteamService _steamService;
        private readonly ILogger<GamesController> _logger;
        private readonly IMongoCollection<GameDetails> _games;

        public GamesController(SteamService steamService, IMongoClient client, ILogger<GamesController> logger)
        {
            _steamService = steamService;
            _logger = logger;

            var database = client.GetDatabase("NH-Games");
            _games = database.GetCollection<GameDetails>("games");
        }

        [HttpPost("update-owned-games/{steamId}")]
        [ProducesResponseType(200)] // OK
        [ProducesResponseType(500)] // Internal Server Error
        public async Task<IActionResult> UpdateOwnedGames(long steamId, bool overrideExisting = false)
        {
            try
            {
                await _steamService.UpdateOwnedGamesAsync(steamId, overrideExisting);
                return Ok("Owned games updated successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating owned games for Steam ID {SteamId}", steamId);
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
    }
}