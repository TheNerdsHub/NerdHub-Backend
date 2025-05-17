using System.Collections.Concurrent;
using NerdHub.Services.Interfaces;
using NerdHub.Models;

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