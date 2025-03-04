using NerdHub.Models;
using MongoDB.Driver;
using Newtonsoft.Json;
using Microsoft.Extensions.Logging;

namespace NerdHub.Services
{
    public class SteamService
    {
        private readonly IConfiguration _configuration;
        private readonly IMongoCollection<Game> _games;
        private readonly ILogger<SteamService> _logger;

        public SteamService(IConfiguration configuration, IMongoClient client, ILogger<SteamService> logger)
        {
            _configuration = configuration;
            _logger = logger;

            var database = client.GetDatabase("NerdHub-Games");
            _games = database.GetCollection<Game>("games");
        }

        public async Task UpdateOwnedGames(string steamId)
        {
            try
            {
                var httpClient = new HttpClient();
                var steamApiKey = _configuration["Steam:ApiKey"];

                var response = await httpClient.GetStringAsync($"http://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={steamApiKey}&steamid={steamId}&format=json");

                var games = JsonConvert.DeserializeObject<List<Game>>(response);

                if (games != null)
                {
                    foreach (Game game in games)
                    {
                        var gameDataResponse = await httpClient.GetStringAsync($"http://store.steampowered.com/api/appdetails?appids={game.steam_appid}&l=english");
                        var gameData = JsonConvert.DeserializeObject<Game>(gameDataResponse);

                        if (gameData != null)
                        {
                            if (gameData.genres != null)
                            {
                                gameData.genres = gameData.genres.Select(genre => new Genre { description = genre.description }).ToList();
                            }

                            if (gameData.categories != null)
                            {
                                gameData.categories = gameData.categories.Select(category => new Category { description = category.description }).ToList();
                            }

                            var existingGame = _games.FindSync(g => g.steam_appid == game.steam_appid).FirstOrDefault();

                            if (existingGame == null)
                            {
                                _games.InsertOne(gameData);
                            }
                            else
                            {
                                if (!existingGame.SteamID.Contains(steamId))
                                {
                                    existingGame.SteamID.Add(steamId);
                                    var update = Builders<Game>.Update.Set(g => g.SteamID, existingGame.SteamID);
                                    _games.UpdateOne(g => g.steam_appid == existingGame.steam_appid, update);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating owned games for Steam ID {SteamId}", steamId);
            }
        }
    }
}