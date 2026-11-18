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

        /// <summary>
        /// Red confirm button. Null (only the old tuple overload): guessed from an English title starting with
        /// Delete/Deactivate/Discard/Remove/Unlink — which fails for German titles, so new callers always set it.
        /// </summary>
        public bool? Destructive { get; init; }
    }

    public interface INotificationService
    {
        event Func<ConfirmationRequest, Task<bool>> OnConfirmationRequested;

        /// <summary>Asks the user; true = confirmed, false = cancelled (Escape, backdrop, "Cancel").</summary>
        Task<bool> ShowConfirmation(ConfirmationRequest request);

        /// <summary>
        /// The pre-AP12 form: title and message in English, the dialog guesses the button from the title's verb.
        /// Kept until every caller has moved to <see cref="ShowConfirmation(ConfirmationRequest)"/>.
        /// </summary>
        Task<bool> ShowConfirmation((string title, string message) tuple);
    }
}
