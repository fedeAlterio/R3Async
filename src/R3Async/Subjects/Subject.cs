using System;
using System.Threading;
using System.Threading.Tasks;
using R3Async.Internals;
using R3Async.Subjects.Internals;

namespace R3Async.Subjects;

public static class Subject
{
    public static ISubject<T> Create<T>() => Create<T>(SubjectCreationOptions.Default);
    public static ISubject<T> Create<T>(SubjectCreationOptions options)
    {
        return options.PublishingOption switch
        {
            PublishingOption.Serial => new SerialSubject<T>(),
            PublishingOption.Concurrent => new ConcurrentSubject<T>(),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public static ISubject<T> CreateBehavior<T>(T startValue) => CreateBehavior(startValue, BehaviorSubjectCreationOptions.Default);
    public static ISubject<T> CreateBehavior<T>(T startValue, BehaviorSubjectCreationOptions options)
    {
        return options.PublishingOption switch
        {
            PublishingOption.Serial => new SerialReplayLatestSubject<T>(new(startValue)),
            PublishingOption.Concurrent => new ConcurrentReplayLatestSubject<T>(new(startValue)),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public static ISubject<T> CreateReplayLatest<T>() => CreateReplayLatest<T>(ReplayLatestSubjectCreationOptions.Default);
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

public static class SubjectEx
{
    public static ISubject<T> MapValues<T>(this ISubject<T> @this, Func<AsyncObservable<T>, AsyncObservable<T>> mapper)
    {
        return new MappedSubejct<T>(@this, mapper);
    }

    sealed class MappedSubejct<T>(ISubject<T> original, Func<AsyncObservable<T>, AsyncObservable<T>> mapper) : ISubject<T>
    {
        public AsyncObservable<T> Values { get; } = mapper(original.Values);
        public ValueTask OnNextAsync(T value, CancellationToken cancellationToken) => original.OnNextAsync(value, cancellationToken);
        public ValueTask OnErrorResumeAsync(Exception error, CancellationToken cancellationToken) => original.OnErrorResumeAsync(error, cancellationToken);
        public ValueTask OnCompletedAsync(Result result) => original.OnCompletedAsync(result);
    }
}