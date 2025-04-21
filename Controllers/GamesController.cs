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

        /// <summary>
        /// Updates the owned games for a given Steam ID.
        /// </summary>
        /// <param name="steamId">The Steam ID of the user.</param>
        /// <param name="overrideExisting">Whether to override existing data.</param>
        /// <returns>A success message or an error message.</returns>
        [HttpGet("update-owned-games/{steamId}")]
        [ProducesResponseType(200)] // OK
        [ProducesResponseType(500)] // Internal Server Error
        public async Task<IActionResult> UpdateOwnedGames(long steamId, bool overrideExisting = false)
        {
            try
            {
                await _steamService.UpdateOwnedGames(steamId, overrideExisting);
                return Ok("Owned games updated successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating owned games for Steam ID {SteamId}", steamId);
                return StatusCode(500, "An error occurred while updating owned games.");
            }
        }

        /// <summary>
        /// Fetches all games from the database.
        /// </summary>
        /// <returns>A list of all games or an error message.</returns>
        [HttpGet]
        [ProducesResponseType(200)] // OK
        [ProducesResponseType(500)] // Internal Server Error
        public async Task<IActionResult> GetAllGames()
        {
            try
            {
                var games = await _games.Find(_ => true).ToListAsync();
                return Ok(games);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching all games.");
                return StatusCode(500, "An error occurred while fetching all games.");
            }
        }

        /// <summary>
        /// Fetches a specific game by its Steam App ID.
        /// </summary>
        /// <param name="appid">The Steam App ID of the game.</param>
        /// <returns>The game details or an error message.</returns>
        [HttpGet("{appid}")]
        [ProducesResponseType(200)] // OK
        [ProducesResponseType(404)] // Not Found
        [ProducesResponseType(500)] // Internal Server Error
        public async Task<IActionResult> GetGameById(int appid)
        {
            try
            {
                var game = await _games.Find(g => g.appid == appid).FirstOrDefaultAsync();
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
    }
}