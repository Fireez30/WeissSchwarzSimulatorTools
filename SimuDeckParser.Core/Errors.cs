namespace SimuDeckParser;

/// <summary>
/// An error meant for the user: its message says what went wrong, prefixed by what was being
/// done ("reading the deck: file not found"). Created with <see cref="Errors.Context"/>.
/// </summary>
public class UserException : Exception
{
    public UserException(string message) : base(message) { }

    public UserException(string message, Exception inner) : base(message, inner) { }
}

public static class Errors
{
    /// <summary>Wraps <paramref name="inner"/> with what was being done: "context: inner message".</summary>
    public static UserException Context(string context, Exception inner) =>
        new($"{context}: {inner.Message}", inner);

    /// <summary>Runs <paramref name="action"/>, wrapping its I/O errors with <paramref name="context"/>.</summary>
    public static T WithContext<T>(string context, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw Context(context, e);
        }
    }

    public static void WithContext(string context, Action action) =>
        WithContext(context, () =>
        {
            action();
            return 0;
        });
}
