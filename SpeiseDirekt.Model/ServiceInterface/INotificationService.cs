namespace SpeiseDirekt.ServiceInterface
{
    public interface INotificationService
    {
        event Func<(string title, string message), Task<bool>> OnConfirmationRequested;

        Task<bool> ShowConfirmation((string title, string message) tuple);
    }
}
