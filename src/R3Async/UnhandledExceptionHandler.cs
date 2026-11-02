using System;
using System.Threading;

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
    /// Replaces the global unhandled exception handler with <paramref name="unhandledExceptionHandler"/>. Safe to call
    /// while exceptions are being reported: each report uses either the previous or the new handler.
    /// </summary>
    public static void Register(Action<Exception> unhandledExceptionHandler) =>
        Volatile.Write(ref _unhandledException, unhandledExceptionHandler ?? throw new ArgumentNullException(nameof(unhandledExceptionHandler)));
    static void DefaultUnhandledExceptionHandler(Exception exception) => Console.WriteLine("R3Async unhandled exception: " + exception);
    internal static void OnUnhandledException(Exception e)
    {
        if (e is OperationCanceledException) return;

        try
        {
            Volatile.Read(ref _unhandledException)(e);
        }
        catch
        {
            // Ignored
        }
    }
}
