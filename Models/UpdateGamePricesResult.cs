namespace NerdHub.Models
{
    public class UpdateGamePricesResult
    {
        public int TotalGamesCount { get; set; }
        public int UpdatedGamesCount { get; set; }
        public int SkippedGamesCount { get; set; }
        public int FailedGamesCount { get; set; }
        public List<int> UpdatedAppIds { get; set; } = new List<int>();
        public List<int> SkippedAppIds { get; set; } = new List<int>();
        public List<int> FailedAppIds { get; set; } = new List<int>();
    }
}
