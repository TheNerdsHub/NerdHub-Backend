using NerdHub.Models;
using MongoDB.Driver;


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

    }
}