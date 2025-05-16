using System.Collections.Concurrent;

public interface IProgressTracker
{
    void SetProgress(string operationId, int progress, string phase, string message);
    bool TryGetProgress(string operationId, out ProgressInfo? progressInfo);
}

public class ProgressTracker : IProgressTracker
{
    private readonly ConcurrentDictionary<string, ProgressInfo> _progressStore = new();

    public void SetProgress(string operationId, int progress, string phase, string message)
    {
        _progressStore[operationId] = new ProgressInfo
        {
            Progress = progress,
            Phase = phase,
            Message = message
        };
    }

    public bool TryGetProgress(string operationId, out ProgressInfo? progressInfo)
    {
        return _progressStore.TryGetValue(operationId, out progressInfo);
    }
}

public class ProgressInfo
{
    public int Progress { get; set; } = 0;
    public string Phase { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public double? RetryAfterSeconds { get; set; }
}