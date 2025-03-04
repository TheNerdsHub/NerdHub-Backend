using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using NerdHub.Services;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace NerdHub.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GameController : ControllerBase
    {
        private readonly SteamService _steamService;
        private readonly ILogger<GameController> _logger;

        public GameController(SteamService steamService, ILogger<GameController> logger)
        {
            _steamService = steamService;
            _logger = logger;
        }

        [HttpGet("update-owned-games/{steamId}")]
        public async Task<IActionResult> UpdateOwnedGames(string steamId)
        {
            if (string.IsNullOrEmpty(steamId))
            {
                return BadRequest("Steam ID is required.");
            }

            try
            {
                await _steamService.UpdateOwnedGames(steamId);
                return Ok("Owned games updated successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating owned games for Steam ID {SteamId}", steamId);
                return StatusCode(500, "An error occurred while updating owned games.");
            }
        }
    }
}