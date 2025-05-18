using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NerdHub.Models
{
    public class UserMapping
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)] // Use string to store Steam IDs as strings
        public required string SteamId { get; set; }

        [BsonElement("username")]
        public required string Username { get; set; }

        [BsonElement("nickname")]
        public string? Nickname { get; set; } // Optional nickname

        [BsonElement("discordId")]
        public string? DiscordId { get; set; } // Optional Discord ID
    }
}