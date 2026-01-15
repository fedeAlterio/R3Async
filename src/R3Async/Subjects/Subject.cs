using System;
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
            (PublishingOption.Serial, false) => new SerialBehaviorSubject<T>(startValue),
            (PublishingOption.Concurrent, false) => new ConcurrentBehaviorSubject<T>(startValue),
            (PublishingOption.Serial, true) => new SerialStatelessBehaviorSubject<T>(startValue),
            (PublishingOption.Concurrent, true) => new ConcurrentStatelessBehaviorSubject<T>(startValue),
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}