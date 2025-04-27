namespace NerdHub.Models
{
    public class UpdateOwnedGamesResult
    {
        public int UpdatedGamesCount { get; set; }
        public int SkippedGamesCount { get; set; }
        public int FailedGamesCount { get; set; }
        public List<int> FailedGameIds { get; set; } = new List<int>();
        public List<int> SkippedBlacklistedGameIds { get; set; } = new List<int>();
    }
}