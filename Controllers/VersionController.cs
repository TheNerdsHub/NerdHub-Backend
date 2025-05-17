using Microsoft.AspNetCore.Mvc;

namespace NerdHub.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class VersionController : ControllerBase
    {
        private readonly ILogger<VersionController> _logger;
        private readonly IConfiguration _configuration;

        public VersionController(IConfiguration configuration, ILogger<VersionController> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        [HttpGet]
        [ProducesResponseType(200)] // OK
        [ProducesResponseType(500)] // Internal Server Error
        public IActionResult GetVersion()
        {
            try
            {
                _logger.LogInformation("Fetching version information.");
                var version = _configuration["Version"];
                return new JsonResult(new { version });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while the version.");
                return StatusCode(500, "An error occurred while fetching all games.");
            }
        }
    }
}