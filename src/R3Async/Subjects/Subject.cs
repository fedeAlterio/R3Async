using System;
using R3Async.Internals;
using R3Async.Subjects.Internals;

namespace R3Async.Subjects;

public static class Subject
{
    public static ISubject<T> Create<T>() => Create<T>(SubjectCreationOptions.Default);
    public static ISubject<T> Create<T>(SubjectCreationOptions options)
    {
        return (options.PublishingOption, options.IsStateless) switch
        {
            (PublishingOption.Serial, false) => new SerialSubject<T>(),
            (PublishingOption.Concurrent, false) => new ConcurrentSubject<T>(),
            (PublishingOption.Serial, true) => new SerialStatelessSubjet<T>(),
            (PublishingOption.Concurrent, true) => new ConcurrentlStatelessSubjet<T>(),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public static ISubject<T> CreateBehavior<T>(T startValue) => CreateBehavior(startValue, BehaviorSubjectCreationOptions.Default);
    public static ISubject<T> CreateBehavior<T>(T startValue, BehaviorSubjectCreationOptions options)
    {
        return (options.PublishingOption, options.IsStateless) switch
        {
            (PublishingOption.Serial, false) => new SerialReplayLatestSubject<T>(new(startValue)),
            (PublishingOption.Concurrent, false) => new ConcurrentReplayLatestSubject<T>(new(startValue)),
            (PublishingOption.Serial, true) => new SerialStatelessReplayLastSubject<T>(new(startValue)),
            (PublishingOption.Concurrent, true) => new ConcurrentStatelessReplayLatestSubject<T>(new(startValue)),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    public static ISubject<T> CreateReplayLatest<T>() => CreateReplayLatest<T>(ReplayLatestSubjectCreationOptions.Default);
    public static ISubject<T> CreateReplayLatest<T>(ReplayLatestSubjectCreationOptions options)
    {
        return (options.PublishingOption, options.IsStateless) switch
        {
            (PublishingOption.Serial, false) => new SerialReplayLatestSubject<T>(Optional<T>.Empty),
            (PublishingOption.Concurrent, false) => new ConcurrentReplayLatestSubject<T>(Optional<T>.Empty),
            (PublishingOption.Serial, true) => new SerialStatelessReplayLastSubject<T>(Optional<T>.Empty),
            (PublishingOption.Concurrent, true) => new ConcurrentStatelessReplayLatestSubject<T>(Optional<T>.Empty),
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}