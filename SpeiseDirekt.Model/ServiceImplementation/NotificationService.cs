using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation
{
    public class NotificationService : INotificationService
    {
        public event Func<(string title, string message), Task<bool>>? OnConfirmationRequested;

        public async Task<bool> ShowConfirmation((string title, string message) tuple)
        {
            if (OnConfirmationRequested != null)
            {
                return await OnConfirmationRequested.Invoke(tuple);
            }
            return false;
        }
    }
}
