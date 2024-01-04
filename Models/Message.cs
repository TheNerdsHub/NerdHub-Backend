using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NerdHub.Models
{
    public class Message
    {
        [BsonId]
        public ObjectId Id { get; set; }
        public string? Content { get; set; }
        public string? Author { get; set; }
        public DateTime Timestamp { get; set; }
    }
}