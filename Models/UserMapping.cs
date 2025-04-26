using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NerdHub.Models
{
    public class UserMapping
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)] // Use string to store Steam IDs as strings
        public string SteamId { get; set; }

        [BsonElement("username")]
        public string Username { get; set; }

        [BsonElement("nickname")]
        public string? Nickname { get; set; } // Optional nickname
    }
}