using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation
{
    public class NotificationService : INotificationService
    {
        public event Func<ConfirmationRequest, Task<bool>>? OnConfirmationRequested;

        public async Task<bool> ShowConfirmation(ConfirmationRequest request)
        {
            if (OnConfirmationRequested != null)
            {
                return await OnConfirmationRequested.Invoke(request);
            }
            return false;
        }
    }
}
