using NerdHub.Models;
using MongoDB.Driver;
using Newtonsoft.Json;
namespace NerdHub.Services
{
    public class SteamService
    {
        private readonly IConfiguration _configuration;
        private readonly IMongoCollection<Game> _games;

        public SteamService(IConfiguration configuration, IMongoClient client)
        {
            _configuration = configuration;

            var database = client.GetDatabase("NerdHub-Games");
            _games = database.GetCollection<Game>("games");
        }

        public async Task UpdateOwnedGames(string steamId)
        {
            var httpClient = new HttpClient();
            var steamApiKey = _configuration["Steam:ApiKey"];

            // Call the Steam API to get the owned games for the user
            var response = await httpClient.GetStringAsync($"http://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={steamApiKey}&steamid={steamId}&format=json");

            // Parse the response and get the list of games
            var games = JsonConvert.DeserializeObject<List<Game>>(response);

            if (games != null)
            {
                foreach (var game in games)
                {
                    // Call the App details request to get the full data of the game
                    var gameDataResponse = await httpClient.GetStringAsync($"http://store.steampowered.com/api/appdetails?appids={game.steam_appid}&l=english");

                    // Parse the response and get the game data
                    var gameData = JsonConvert.DeserializeObject<Game>(gameDataResponse);

                    if (gameData != null)
                    {
                        // Extract only the descriptions of the genres
                        if (gameData.genres != null)
                        {
                            gameData.genres = gameData.genres.Select(genre => new Genre { description = genre.description }).ToList();
                        }

                        // Extract only the descriptions of the categories
                        if (gameData.categories != null)
                        {
                            gameData.categories = gameData.categories.Select(category => new Category { description = category.description }).ToList();
                        }

                        // Check if the game already exists in the database
                        var existingGame = _games.FindSync(g => g.steam_appid == game.steam_appid).FirstOrDefault();

                        if (existingGame == null)
                        {
                            // If the game does not exist in the database, add it
                            _games.InsertOne(gameData);
                        }
                        else
                        {
                            // If the game exists and the new steamId is not in the list, append it
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
    }
}