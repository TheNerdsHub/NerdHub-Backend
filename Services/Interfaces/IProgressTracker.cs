using NerdHub.Models;

namespace NerdHub.Services.Interfaces
{
    public interface IProgressTracker
    {
        void SetProgress(string operationId, int progress, string phase, string message);
        bool TryGetProgress(string operationId, out ProgressInfo? progressInfo);
    }
}