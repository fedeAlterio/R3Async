using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;
using R3Async.Subjects.Internals;

namespace R3Async.Subjects;

/// <summary>Factory methods for creating <see cref="ISubject{T}"/> instances.</summary>
public static class Subject
{
    /// <summary>Creates a plain subject with default (<see cref="PublishingOption.Serial"/>) publishing options. New subscribers do not receive past values.</summary>
    public static ISubject<T> Create<T>() => Create<T>(SubjectCreationOptions.Default);

    /// <summary>Creates a plain subject with the given publishing options. New subscribers do not receive past values.</summary>
    public static ISubject<T> Create<T>(SubjectCreationOptions options)
    {
        return options.PublishingOption switch
        {
            PublishingOption.Serial => new SerialSubject<T>(),
            PublishingOption.Concurrent => new ConcurrentSubject<T>(),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    /// <summary>
    /// Creates a BehaviorSubject with default (<see cref="PublishingOption.Serial"/>) publishing options: it stores the
    /// latest value (starting at <paramref name="startValue"/>) and immediately replays it to each new subscriber upon subscription.
    /// </summary>
    public static ISubject<T> CreateBehavior<T>(T startValue) => CreateBehavior(startValue, BehaviorSubjectCreationOptions.Default);

    /// <summary>
    /// Creates a BehaviorSubject with the given publishing options: it stores the latest value (starting at
    /// <paramref name="startValue"/>) and immediately replays it to each new subscriber upon subscription.
    /// </summary>
    public static ISubject<T> CreateBehavior<T>(T startValue, BehaviorSubjectCreationOptions options)
    {
        return options.PublishingOption switch
        {
            PublishingOption.Serial => new SerialReplayLatestSubject<T>(new(startValue)),
            PublishingOption.Concurrent => new ConcurrentReplayLatestSubject<T>(new(startValue)),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    /// <summary>
    /// Creates a subject with default (<see cref="PublishingOption.Serial"/>) publishing options that replays the latest
    /// value to each new subscriber, if any value has been emitted yet (unlike a BehaviorSubject, there is no initial value).
    /// </summary>
    public static ISubject<T> CreateReplayLatest<T>() => CreateReplayLatest<T>(ReplayLatestSubjectCreationOptions.Default);

    /// <summary>
    /// Creates a subject with the given publishing options that replays the latest value to each new subscriber, if any
    /// value has been emitted yet (unlike a BehaviorSubject, there is no initial value).
    /// </summary>
    public static ISubject<T> CreateReplayLatest<T>(ReplayLatestSubjectCreationOptions options)
    {
        return options.PublishingOption switch
        {
            PublishingOption.Serial => new SerialReplayLatestSubject<T>(Optional<T>.Empty),
            PublishingOption.Concurrent => new ConcurrentReplayLatestSubject<T>(Optional<T>.Empty),
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}

/// <summary>Extension methods for composing <see cref="ISubject{T}"/> instances.</summary>
public static class SubjectEx
{
    /// <summary>
    /// Wraps the subject so that its <see cref="ISubject{T}.Values"/> observable is transformed by <paramref name="mapper"/>,
    /// while <c>OnNextAsync</c>/<c>OnErrorResumeAsync</c>/<c>OnCompletedAsync</c> still push directly into the original subject.
    /// </summary>
    public static ISubject<T> MapValues<T>(this ISubject<T> @this, Func<AsyncObservable<T>, AsyncObservable<T>> mapper)
    {
        return new MappedSubject<T>(@this, mapper);
    }

    sealed class MappedSubject<T>(ISubject<T> original, Func<AsyncObservable<T>, AsyncObservable<T>> mapper) : ISubject<T>
    {
        public AsyncObservable<T> Values { get; } = mapper(original.Values);
        public ValueTask OnNextAsync(T value, CancellationToken cancellationToken) => original.OnNextAsync(value, cancellationToken);
        public ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken) => original.OnErrorResumeAsync(error, cancellationToken);
        public ValueTask OnCompletedAsync(Result result) => original.OnCompletedAsync(result);
    }
}