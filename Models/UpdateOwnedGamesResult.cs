namespace NerdHub.Models
{
    public class UpdateOwnedGamesResult
    {
        public int UpdatedGamesCount { get; set; }
        public int SkippedGamesCount { get; set; }
        public int FailedGamesCount { get; set; }

        public List<int> FailedToFetchGameDetails { get; set; } = new List<int>();
        public List<int> SkippedNotInUpdateList { get; set; } = new List<int>();
        public List<int> SkippedDueToBlacklist { get; set; } = new List<int>();
    }
}