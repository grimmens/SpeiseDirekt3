namespace SpeiseDirekt.ServiceInterface
{
    /// <summary>
    /// A confirmation the back office asks for (<see cref="INotificationService.ShowConfirmation(ConfirmationRequest)"/>):
    /// the dialog's title and message, the confirm button's text and whether the action is destructive (red button).
    /// All texts come localized from the caller (AP12); the dialog adds only "Cancel".
    /// </summary>
    /// <param name="Title">The dialog's heading, e.g. "Delete menu" / "Speisekarte löschen".</param>
    /// <param name="Message">The question, e.g. "Delete the menu "Lunch"?".</param>
    public sealed record ConfirmationRequest(string Title, string Message)
    {
        /// <summary>The confirm button's text (e.g. "Delete" / "Löschen"). Null: the title for a destructive action, else "OK".</summary>
        public string? ConfirmText { get; init; }

        /// <summary>Red confirm button: the action deletes, removes or discards something.</summary>
        public bool Destructive { get; init; }
    }

    public interface INotificationService
    {
        event Func<ConfirmationRequest, Task<bool>> OnConfirmationRequested;

        /// <summary>Asks the user; true = confirmed, false = cancelled (Escape, backdrop, "Cancel").</summary>
        Task<bool> ShowConfirmation(ConfirmationRequest request);
    }
}
