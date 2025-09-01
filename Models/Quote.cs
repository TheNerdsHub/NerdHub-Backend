using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NerdHub_Backend.Models
{
    public class Quote
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }
        
        [BsonElement("quoteText")]
        public string QuoteText { get; set; } = string.Empty;
        
        [BsonElement("quotedPersons")]
        public List<string> QuotedPersons { get; set; } = new List<string>();
        
        [BsonElement("submitter")]
        public string Submitter { get; set; } = string.Empty;
        
        [BsonElement("discordUserId")]
        public string DiscordUserId { get; set; } = string.Empty;
        
        [BsonElement("channelId")]
        public string ChannelId { get; set; } = string.Empty;
        
        [BsonElement("channelName")]
        public string ChannelName { get; set; } = string.Empty;
        
        [BsonElement("messageId")]
        public string MessageId { get; set; } = string.Empty;
        
        [BsonElement("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
