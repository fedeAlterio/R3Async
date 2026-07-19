using System;

namespace R3Async;

/// <summary>
/// Global sink for exceptions that R3Async cannot propagate to a caller, such as errors from a resumable
/// <c>OnErrorResumeAsync</c> handler that itself throws, or cleanup failures during disposal. By default, such
/// exceptions are written to the console; register a custom handler via <see cref="Register"/> to change this.
/// </summary>
public static class UnhandledExceptionHandler
{
    static Action<Exception> _unhandledException = DefaultUnhandledExceptionHandler;

    /// <summary>
    /// Replaces the global unhandled exception handler with <paramref name="unhandledExceptionHandler"/>. Not
    /// thread-safe with concurrent unhandled exceptions being reported; typically called once at startup.
    /// </summary>
    public static void Register(Action<Exception> unhandledExceptionHandler) => _unhandledException = unhandledExceptionHandler;
    static void DefaultUnhandledExceptionHandler(Exception exception) => Console.WriteLine("R3 UnhandleException: " + exception);
    internal static void OnUnhandledException(Exception e)
    {
        if (e is OperationCanceledException) return;

        try
        {
            _unhandledException(e);
        }
        catch
        {
            // Ignored
        }
    }
}
