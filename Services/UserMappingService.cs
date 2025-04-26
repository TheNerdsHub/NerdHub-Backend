using MongoDB.Driver;
using NerdHub.Models;
using Microsoft.Extensions.Logging;

namespace NerdHub.Services
{
    public class UserMappingService
    {
        private readonly IMongoCollection<UserMapping> _userMappings;
        private readonly IConfiguration _configuration;
        private readonly ILogger<UserMappingService> _logger;

        public UserMappingService(IMongoClient client, IConfiguration configuration, ILogger<UserMappingService> logger)
        {
            _configuration = configuration;
            _logger = logger;

            var database = client.GetDatabase("NH-Games");
            _userMappings = database.GetCollection<UserMapping>("UserMappings");
        }

        public async Task<UserMapping?> GetUserMappingAsync(string steamId)
        {
            try
            {
                _logger.LogInformation("Fetching user mapping for SteamId: {SteamId}", steamId);
                return await _userMappings.Find(mapping => mapping.SteamId == steamId).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while fetching user mapping for SteamId: {SteamId}", steamId);
                throw; // Re-throw the exception to be handled by the caller
            }
        }

        public async Task AddOrUpdateUserMappingAsync(string steamId, string username, string? nickname = null)
        {
            try
            {
                _logger.LogInformation("Adding or updating user mapping for SteamId: {SteamId}, Username: {Username}, Nickname: {Nickname}", steamId, username, nickname);
                var filter = Builders<UserMapping>.Filter.Eq(mapping => mapping.SteamId, steamId);
                var update = Builders<UserMapping>.Update
                    .Set(mapping => mapping.Username, username)
                    .Set(mapping => mapping.Nickname, nickname); // Update nickname if provided
                await _userMappings.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true });
                _logger.LogInformation("Successfully added or updated user mapping for SteamId: {SteamId}", steamId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while adding or updating user mapping for SteamId: {SteamId}", steamId);
                throw; // Re-throw the exception to be handled by the caller
            }
        }
    }
}