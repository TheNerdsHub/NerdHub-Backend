using Newtonsoft.Json;

namespace NerdHub.Models
{
    public class Game
    {
        [JsonProperty("appid")]
        public int steam_appid { get; set; }

        [JsonProperty("playtime_forever")]
        public int playtime_forever { get; set; }

        [JsonProperty("playtime_windows_forever")]
        public int playtime_windows_forever { get; set; }

        [JsonProperty("playtime_mac_forever")]
        public int playtime_mac_forever { get; set; }

        [JsonProperty("playtime_linux_forever")]
        public int playtime_linux_forever { get; set; }

        [JsonProperty("playtime_deck_forever")]
        public int playtime_deck_forever { get; set; }

        [JsonProperty("rtime_last_played")]
        public int rtime_last_played { get; set; }

        [JsonProperty("playtime_disconnected")]
        public int playtime_disconnected { get; set; }
    }
}