using System;

namespace R3Async;

public static class UnhandledExceptionHandler
{
    static Action<Exception> _unhandledException = DefaultUnhandledExceptionHandler;
    public static void Register(Action<Exception> unhandledExceptionHandler) => _unhandledException = unhandledExceptionHandler;
    static void DefaultUnhandledExceptionHandler(Exception exception) => Console.WriteLine("R3 UnhandleException: " + exception);
    internal static void OnUnhandledException(Exception e)
    {
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
