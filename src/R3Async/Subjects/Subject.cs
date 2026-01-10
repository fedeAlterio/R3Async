using System;
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
            PublishingOption.Serial => new SerialBehaviorSubject<T>(startValue),
            PublishingOption.Concurrent => new ConcurrentBehaviorSubject<T>(startValue),
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}