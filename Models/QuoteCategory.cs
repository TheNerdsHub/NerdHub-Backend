using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NerdHub_Backend.Models
{
    public class QuoteCategory
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        
        [BsonElement("guildId")]
        public string GuildId { get; set; } = string.Empty;
        
        [BsonElement("categoryId")]
        public string CategoryId { get; set; } = string.Empty;
        
        [BsonElement("categoryName")]
        public string CategoryName { get; set; } = string.Empty;
        
        [BsonElement("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        [BsonElement("updatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
