namespace NerdHub.Models
{
    public class ProgressInfo
    {
        public int Progress { get; set; } = 0;
        public string Phase { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public double? RetryAfterSeconds { get; set; }
    }
}