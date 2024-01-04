using MongoDB.Bson.Serialization.Attributes;

namespace NerdHub.Models
{
    [BsonIgnoreExtraElements]
    public class Game
    {
        [BsonId]
        public int steam_appid { get; set; }
        public string? name { get; set; }
        public string? header_image { get; set; }
        public string? detailed_description { get; set; }
        public List<Category> categories { get; set; } = new List<Category>();
        public List<Genre> genres { get; set; } = new List<Genre>();
        public List<string> SteamID { get; set; } = new List<string>();
    }
}