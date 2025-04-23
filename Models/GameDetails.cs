using Newtonsoft.Json;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace NerdHub.Models
{
    public class GameDetails
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        [JsonProperty("_id")]
        public string? Id { get; set; }

        [JsonProperty("type")]
        public string? type { get; set; }

        [JsonProperty("name")]
        public string? name { get; set; }

        private int? _appid;

        [JsonProperty("steam_appid")]
        public int? steam_appid
        {
            get => _appid;
            set => _appid = value;
        }

        [JsonProperty("appid")]
        public int? appid
        {
            get => _appid;
            set => _appid = value;
        }

        [JsonProperty("required_age")]
        public int? requiredAge { get; set; }

        [JsonProperty("is_free")]
        public bool? isFree { get; set; }

        [JsonProperty("controller_support")]
        public string? controllerSupport { get; set; }

        [JsonProperty("dlc")]
        public List<int>? dlc { get; set; }

        [JsonProperty("detailed_description")]
        public string? detailedDescription { get; set; }

        [JsonProperty("about_the_game")]
        public string? aboutTheGame { get; set; }

        [JsonProperty("short_description")]
        public string? shortDescription { get; set; }

        [JsonProperty("fullgame")]
        public FullGame? fullGame { get; set; }

        [JsonProperty("supported_languages")]
        public string? supportedLanguages { get; set; }

        [JsonProperty("header_image")]
        public string? headerImage { get; set; }
        
        [JsonProperty("capsule_image")]
        public string? capsuleImage { get; set; }
        
        [JsonProperty("capsule_imagev5")]
        public string? capsuleImagev5 { get; set; }

        [JsonProperty("website")]
        public string? website { get; set; }

        [JsonProperty("pc_requirements")]
        [JsonConverter(typeof(RequirementsConverter))]
        public Requirements? pcRequirements { get; set; }
        
        [JsonProperty("mac_requirements")]
        [JsonConverter(typeof(RequirementsConverter))]
        public Requirements? macRequirements { get; set; }

        [JsonProperty("linux_requirements")]
        [JsonConverter(typeof(RequirementsConverter))]
        public Requirements? linuxRequirements { get; set; }

        [JsonProperty("legal_notice")]
        public string? legalNotice { get; set; }

        [JsonProperty("ext_user_account_notice")]
        public string? extUserAccountNotice { get; set; }

        [JsonProperty("developers")]
        public List<string>? developers { get; set; }

        [JsonProperty("publishers")]
        public List<string>? publishers { get; set; }

        [JsonProperty("demos")]
        public List<Demo>? demos { get; set; }

        [JsonProperty("price_overview")]
        public PriceOverview? priceOverview { get; set; }

        [JsonProperty("packages")]
        public List<int>? packages { get; set; }

        [JsonProperty("package_groups")]
        public List<PackageGroup>? packageGroups { get; set; }

        [JsonProperty("platforms")]
        public Platforms? platforms { get; set; }

        [JsonProperty("metacritic")]
        public Metacritic? metacritic { get; set; }

        [JsonProperty("categories")]
        public List<Category>? categories { get; set; }

        [JsonProperty("genres")]
        public List<Genre>? genres { get; set; }

        [JsonProperty("screenshots")]
        public List<Screenshot>? screenshots { get; set; }

        [JsonProperty("movies")]
        public List<Movie>? movies { get; set; }

        [JsonProperty("recommendations")]
        public Recommendations? recommendations { get; set; }

        [JsonProperty("achievements")]
        public Achievements? achievements { get; set; }

        [JsonProperty("release_date")]
        public ReleaseDate? releaseDate { get; set; }

        [JsonProperty("support_info")]
        public SupportInfo? supportInfo { get; set; }

        [JsonProperty("background")]
        public string? background { get; set; }

        [JsonProperty("background_raw")]
        public string? backgroundRaw { get; set; }

        [JsonProperty("content_descriptors")]
        public ContentDescriptors? contentDescriptors { get; set; }

        [JsonProperty("ratings")]
        public Ratings? ratings { get; set; }

        [JsonProperty("LastModifiedTime")]
        public string LastModifiedTime { get; set; } = DateTime.UtcNow.ToString("o");

        [JsonProperty("owned_by")]
        public List<OwnedBy>? ownedBy { get; set; } = new List<OwnedBy>();

        [BsonIgnore]
        [JsonProperty("playtime_forever")]
        public int playtime_forever { get; set; }

        [BsonIgnore]
        [JsonProperty("playtime_windows_forever")]
        public int playtime_windows_forever { get; set; }

        [BsonIgnore]
        [JsonProperty("playtime_mac_forever")]
        public int playtime_mac_forever { get; set; }

        [BsonIgnore]
        [JsonProperty("playtime_linux_forever")]
        public int playtime_linux_forever { get; set; }

        [BsonIgnore]
        [JsonProperty("playtime_deck_forever")]
        public int playtime_deck_forever { get; set; }

        [BsonIgnore]
        [JsonProperty("rtime_last_played")]
        public int rtime_last_played { get; set; }

        [BsonIgnore]
        [JsonProperty("playtime_disconnected")]
        public int playtime_disconnected { get; set; }
    }
    public class OwnedBy {
        [JsonProperty("steam")]
        public List<long>? steamId { get; set; }

        [JsonProperty("epic")]
        public List<int>? epicId { get; set; }
    }
    public class FullGame
    {
        [JsonProperty("appid")]
        public int appid { get; set; }

        [JsonProperty("name")]
        public string? name { get; set; }
    }
    public class Demo {
        [JsonProperty("appid")]
        public int appid { get; set; }

        [JsonProperty("description")]
        public string? description { get; set; }
    }
    public class Requirements
    {
        [JsonProperty("minimum")]
        public string? minimum { get; set; }

        [JsonProperty("recommended")]
        public string? recommended { get; set; }
    }
    public class PriceOverview
    {
        [JsonProperty("currency")]
        public string? currency { get; set; }

        [JsonProperty("initial")]
        public int initial { get; set; }

        [JsonProperty("final")]
        public int final { get; set; }

        [JsonProperty("discount_percent")]
        public int discountPercent { get; set; }

        [JsonProperty("initial_formatted")]
        public string? initialFormatted { get; set; }

        [JsonProperty("final_formatted")]
        public string? finalFormatted { get; set; }
    }
    public class Platforms
    {
        [JsonProperty("windows")]
        public bool windows { get; set; }

        [JsonProperty("mac")]
        public bool mac { get; set; }

        [JsonProperty("linux")]
        public bool linux { get; set; }
    }
    public class ReleaseDate
    {
        [JsonProperty("coming_soon")]
        public bool comingSoon { get; set; }

        [JsonProperty("date")]
        public string? date { get; set; }
    }
    public class Recommendations
    {
        [JsonProperty("total")]
        public int total { get; set; }
    }
    public class PackageGroup
    {
        [JsonProperty("name")]
        public string? name { get; set; }

        [JsonProperty("title")]
        public string? title { get; set; }

        [JsonProperty("description")]
        public string? description { get; set; }

        [JsonProperty("selection_text")]
        public string? selectionText { get; set; }

        [JsonProperty("save_text")]
        public string? saveText { get; set; }

        [JsonProperty("display_type")]
        public string? displayType { get; set; }

        [JsonProperty("is_recurring_subscription")]
        public bool isRecurringSubscription { get; set; }

        [JsonProperty("subs")]
        public List<Subscription>? subs { get; set; }
    }
    public class Subscription
    {
        [JsonProperty("packageid")]
        public int packageId { get; set; }

        [JsonProperty("percent_savings_text")]
        public string? percentSavingsText { get; set; }

        [JsonProperty("percent_savings")]
        public int percentSavings { get; set; }

        [JsonProperty("option_text")]
        public string? optionText { get; set; }

        [JsonProperty("option_description")]
        public string? optionDescription { get; set; }

        [JsonProperty("can_get_free_license")]
        public string? canGetFreeLicense { get; set; }

        [JsonProperty("is_free_license")]
        public bool isFreeLicense { get; set; }

        [JsonProperty("price_in_cents_with_discount")]
        public int priceInCentsWithDiscount { get; set; }
    }
    public class Metacritic {
        [JsonProperty("score")]
        public int score { get; set; }

        [JsonProperty("url")]
        public string? url { get; set; }
    }
    public class Category

    {
        public string? id { get; set; }
        public string? description { get; set; }
    }
    public class Genre
    {
        public string? id { get; set; }
        public string? description { get; set; }
    }
    public class Screenshot
    {
        public string? id { get; set; }
        public string? pathThumbnail { get; set; }
        public string? pathFull { get; set; }
    }
    public class Movie
    {
        public string? id { get; set; }
        public string? name { get; set; }
        public string? thumbnail { get; set; }
        public Webm? webm { get; set; }
        public Mp4? mp4 { get; set; }
        public bool? highlight { get; set; }
    }
    public class Webm {
        [JsonProperty("480")]
        public string? res480 { get; set; }
        public string? max { get; set; }
    }
    public class Mp4 {
        [JsonProperty("480")]
        public string? res480 { get; set; }
        public string? max { get; set; }
    }
    public class Achievements {
        [JsonProperty("total")]
        public int total { get; set; }

        [JsonProperty("highlighted")]
        public List<HighlightedAchievements>? highlighted { get; set; }
    }
    public class HighlightedAchievements {
        [JsonProperty("name")]
        public string? name { get; set; }

        [JsonProperty("path")]
        public string? path { get; set; }

        [JsonProperty("description")]
        public string? description { get; set; }

        [JsonProperty("icon")]
        public string? icon { get; set; }

        [JsonProperty("icongray")]
        public string? iconGray { get; set; }
    }
    public class SupportInfo
    {
        [JsonProperty("url")]
        public string? url { get; set; }

        [JsonProperty("email")]
        public string? email { get; set; }
    }
    public class ContentDescriptors
    {
        [JsonProperty("ids")]
        public List<string>? ids { get; set; }

        [JsonProperty("notes")]
        public string? notes { get; set; }
    }
    public class Ratings {
        [JsonProperty("esrb")]
        public RatingDetails? esrb { get; set; }
        
        [JsonProperty("usk")]
        public RatingDetails? usk { get; set; }

        [JsonProperty("oflc")]
        public RatingDetails? oflc { get; set; }

        [JsonProperty("nzoflc")]
        public RatingDetails? nzoflc { get; set; }

        [JsonProperty("cero")]
        public RatingDetails? cero { get; set; }

        [JsonProperty("fpb")]
        public RatingDetails? fpb { get; set; }

        [JsonProperty("csrr")]
        public RatingDetails? csrr { get; set; }

        [JsonProperty("crl")]
        public RatingDetails? crl { get; set; }

        [JsonProperty("mda")]
        public RatingDetails? mda { get; set; }

        [JsonProperty("dejus")]
        public RatingDetails? dejus { get; set; }

        [JsonProperty("pegi")]
        public RatingDetails? pegi { get; set; }

        [JsonProperty("kgrb")]
        public RatingDetails? kgrb { get; set; }

        [JsonProperty("steam_germany")]
        public SteamGermanyRatingDetails? steamGermany { get; set; }
    }
    public class RatingDetails
    {
        [JsonProperty("rating")]
        public string? rating { get; set; }

        [JsonProperty("use_age_gate")]
        public string? useAgeGate { get; set; }

        [JsonProperty("required_age")]
        public string? requiredAge { get; set; }

        [JsonProperty("descriptors")]
        public string? descriptors { get; set; }

        [JsonProperty("interactive_elements")]
        public string? interactiveElements { get; set; }
    }
    public class SteamGermanyRatingDetails
    {
        [JsonProperty("rating_generated")]
        public string? ratingGenerated { get; set; }

        [JsonProperty("rating")]
        public string? rating { get; set; }

        [JsonProperty("required_age")]
        public string? requiredAge { get; set; }

        [JsonProperty("banned")]
        public string? banned { get; set; }

        [JsonProperty("use_age_gate")]
        public string? useAgeGate { get; set; }

        [JsonProperty("descriptors")]
        public string? descriptors { get; set; }
    }
}