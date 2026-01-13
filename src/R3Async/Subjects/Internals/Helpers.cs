using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace R3Async.Subjects.Internals;

internal static class Helpers
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask ForwardOnNextConcurrently<T>(IReadOnlyList<AsyncObserver<T>> observers, T value, CancellationToken cancellationToken)
    {
        if (observers.Count == 0) return default;

        return new ValueTask(Task.WhenAll(observers.Select(x => x.OnNextAsync(value, cancellationToken).AsTask())));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask ForwardOnErrorResumeConcurrently<T>(IReadOnlyList<AsyncObserver<T>> observers, Exception error, CancellationToken cancellationToken)
    {
        if (observers.Count == 0) return default;
        return new ValueTask(Task.WhenAll(observers.Select(x => x.OnErrorResumeAsync(error, cancellationToken).AsTask())));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask ForwardOnCompletedConcurrently<T>(IReadOnlyList<AsyncObserver<T>> observers, Result result)
    {
        if (observers.Count == 0) return default;
        return new ValueTask(Task.WhenAll(observers.Select(x => x.OnCompletedAsync(result).AsTask())));
    }
}
