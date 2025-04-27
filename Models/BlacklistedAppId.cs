using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NerdHub.Models
{
    public class BlacklistedAppId
    {
        [BsonId]
        public ObjectId Id { get; set; }

        [BsonElement("AppId")]
        [BsonRepresentation(BsonType.Int32)]
        public int AppId { get; set; }

        [BsonElement("LastModifiedTime")]
        public string LastModifiedTime { get; set; } = DateTime.UtcNow.ToString("o");
    }
}