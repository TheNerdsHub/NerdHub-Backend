using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;

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
        public async Task<IActionResult> GetVersion()
        {
            try
            {
                _logger.LogInformation("Fetching version information.");
                var version = _configuration["Version"];
                string? latestBackendTag = null;
                string? latestFrontendTag = null;
                string? latestDiscordTag = null;

                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NerdHub-Backend", version ?? "unknown"));

                    // Backend tags
                    var backendApiUrl = "https://api.github.com/repos/TheNerdsHub/NerdHub-Backend/tags";
                    var backendResponse = await httpClient.GetAsync(backendApiUrl);

                    if (backendResponse.IsSuccessStatusCode)
                    {
                        var json = await backendResponse.Content.ReadAsStringAsync();
                        var tags = JArray.Parse(json);
                        latestBackendTag = tags[0]["name"]?.ToString();
                    }
                    else
                    {
                        _logger.LogWarning("Failed to fetch backend tags from GitHub: {StatusCode}", backendResponse.StatusCode);
                    }

                    // Frontend tags
                    var frontendApiUrl = "https://api.github.com/repos/TheNerdsHub/NerdHub-Frontend/tags";
                    var frontendResponse = await httpClient.GetAsync(frontendApiUrl);

                    if (frontendResponse.IsSuccessStatusCode)
                    {
                        var json = await frontendResponse.Content.ReadAsStringAsync();
                        var tags = JArray.Parse(json);
                        latestFrontendTag = tags[0]["name"]?.ToString();
                    }
                    else
                    {
                        _logger.LogWarning("Failed to fetch frontend tags from GitHub: {StatusCode}", frontendResponse.StatusCode);
                    }

                    // Discordbot tags
                    var discordApiUrl = "https://api.github.com/repos/TheNerdsHub/NerdHub-Discord/tags";
                    var discordResponse = await httpClient.GetAsync(discordApiUrl);

                    if (discordResponse.IsSuccessStatusCode)
                    {
                        var json = await discordResponse.Content.ReadAsStringAsync();
                        var tags = JArray.Parse(json);
                        latestDiscordTag = tags[0]["name"]?.ToString();
                    }
                    else
                    {
                        _logger.LogWarning("Failed to fetch discordbot tags from GitHub: {StatusCode}", discordResponse.StatusCode);
                    }
                }

                return new JsonResult(new
                {
                    backendVersion = version,
                    latestBackendGitTag = latestBackendTag,
                    latestFrontendGitTag = latestFrontendTag,
                    latestDiscordGitTag = latestDiscordTag
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching version info.");
                return StatusCode(500, "An error occurred while fetching version info.");
            }
        }
    }
}