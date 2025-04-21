using Newtonsoft.Json;
using System.Collections.Generic;

namespace NerdHub.Models
{
    public class SteamApiResponse
    {
        [JsonProperty("response")]
        public Response? response { get; set; }
    }

    public class Response
    {
        [JsonProperty("game_count")]
        public int gameCount { get; set; }

        [JsonProperty("games")]
        public List<GameDetails> games { get; set; } = new List<GameDetails>();
    }
}