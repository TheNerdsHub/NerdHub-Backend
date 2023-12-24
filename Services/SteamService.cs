using SteamWebAPI2.Interfaces;
using SteamWebAPI2.Utilities;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace NerdHub.Services
{
    public class SteamService
    {
        private readonly IConfiguration _configuration;

        public SteamService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task<string> GetPlayerNickname()
        {
            var apiKey = _configuration["Steam:ApiKey"];
            var webInterfaceFactory = new SteamWebInterfaceFactory(apiKey);
            var steamUser = webInterfaceFactory.CreateSteamWebInterface<SteamUserWebAPIInterface>(new HttpClient());
            var steamId = new SteamID(76561197960435530);
            var playerSummaryResponse = await steamUser.GetPlayerSummaryAsync(steamId);
            return playerSummaryResponse.Data.Nickname;
        }
    }
}