using Newtonsoft.Json;

namespace NerdHub.Models
{
    public class GameDetailsResponse
    {
        [JsonProperty("success")]
        public bool success { get; set; }

        [JsonProperty("data")]
        public GameDetails? data { get; set; }
    }

    public class GameDetailsWrapper
    {
        // Use a dictionary to handle the dynamic key
        [JsonExtensionData]
        public Dictionary<string, GameDetailsResponse>? GameDetails { get; set; }
    }
}