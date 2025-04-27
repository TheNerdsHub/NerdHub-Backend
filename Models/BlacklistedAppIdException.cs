namespace NerdHub.Models
{

    public class BlacklistedAppIdException : Exception
    {
        public BlacklistedAppIdException(int appId)
            : base($"The AppID {appId} is blacklisted and cannot be processed.")
        {
        }
    }
}