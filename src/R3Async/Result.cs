using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace R3Async;

/// <summary>
/// Represents the terminal outcome of an <see cref="AsyncObservable{T}"/> stream, passed to
/// <see cref="AsyncObserver{T}.OnCompletedAsync"/>: either a success (<see cref="Success"/>) or a failure carrying
/// the terminating <see cref="Exception"/> (<see cref="Failure"/>).
/// </summary>
public readonly struct Result
{
    /// <summary>Gets a <see cref="Result"/> representing successful completion (no exception).</summary>
    public static Result Success => default;

    /// <summary>Creates a <see cref="Result"/> representing failed completion with the given exception.</summary>
    public static Result Failure(Exception exception) => new(exception);

    /// <summary>The exception that caused the failure, or <see langword="null"/> if this result is a success.</summary>
    public Exception? Exception { get; }

    /// <summary>Gets whether this result represents a successful completion.</summary>
    [MemberNotNullWhen(false, nameof(Exception))]
    public bool IsSuccess => Exception == null;

    /// <summary>Gets whether this result represents a failed completion.</summary>
    [MemberNotNullWhen(true, nameof(Exception))]
    public bool IsFailure => Exception != null;

    /// <summary>Creates a failed <see cref="Result"/> wrapping <paramref name="exception"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public Result(Exception exception)
    {
        if (exception == null) throw new ArgumentNullException(nameof(exception));
        Exception = exception;
    }

    /// <summary>
    /// If this result is a failure, rethrows <see cref="Exception"/> preserving its original stack trace via
    /// <see cref="ExceptionDispatchInfo"/>. Does nothing if this result is a success.
    /// </summary>
    public void TryThrow()
    {
        if (IsFailure)
        {
            ExceptionDispatchInfo.Capture(Exception).Throw();
        }
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        if (IsSuccess)
        {
            return $"Success";
        }
        else
        {
            return $"Failure{{{Exception.Message}}}";
        }
    }
}