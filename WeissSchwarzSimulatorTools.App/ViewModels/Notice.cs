using WeissSchwarzSimulatorTools.EncoreDecks;

namespace WeissSchwarzSimulatorTools.App.ViewModels;

/// <summary>Something a screen wants to tell the user: a snackbar for information, a dialog for errors.</summary>
public abstract record Notice
{
    public sealed record Info(string Text) : Notice;

    /// <param name="SessionExpired">The Encore Decks session expired: the app forgets the linked account.</param>
    public sealed record Error(string Title, string Message, bool SessionExpired = false) : Notice;

    /// <summary>An error dialog for a failed operation; its message says what went wrong.</summary>
    public static Error FromException(string title, Exception e) =>
        new(title, e is UserException ? e.Message : $"{e.Message} ({e.GetType().Name})", e is SessionExpiredException);
}
