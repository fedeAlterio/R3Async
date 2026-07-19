namespace R3Async.Subjects;

/// <summary>Options controlling how a plain subject created via <see cref="Subject.Create{T}(SubjectCreationOptions)"/> notifies its observers.</summary>
public sealed record SubjectCreationOptions
{
    /// <summary>Whether observers are notified one after another (<see cref="Subjects.PublishingOption.Serial"/>) or concurrently (<see cref="Subjects.PublishingOption.Concurrent"/>).</summary>
    public required PublishingOption PublishingOption { get; init; }

    /// <summary>The default options: <see cref="Subjects.PublishingOption.Serial"/> publishing.</summary>
    public static SubjectCreationOptions Default { get; } = new()
    {
        PublishingOption = PublishingOption.Serial
    };
}

/// <summary>Options controlling how a BehaviorSubject created via <see cref="Subject.CreateBehavior{T}(T, BehaviorSubjectCreationOptions)"/> notifies its observers.</summary>
public sealed record BehaviorSubjectCreationOptions
{
    /// <summary>Whether observers are notified one after another (<see cref="Subjects.PublishingOption.Serial"/>) or concurrently (<see cref="Subjects.PublishingOption.Concurrent"/>).</summary>
    public required PublishingOption PublishingOption { get; init; }

    /// <summary>The default options: <see cref="Subjects.PublishingOption.Serial"/> publishing.</summary>
    public static BehaviorSubjectCreationOptions Default { get; } = new()
    {
        PublishingOption = PublishingOption.Serial,
    };
}

/// <summary>Options controlling how a replay-latest subject created via <see cref="Subject.CreateReplayLatest{T}(ReplayLatestSubjectCreationOptions)"/> notifies its observers.</summary>
public sealed record ReplayLatestSubjectCreationOptions
{
    /// <summary>Whether observers are notified one after another (<see cref="Subjects.PublishingOption.Serial"/>) or concurrently (<see cref="Subjects.PublishingOption.Concurrent"/>).</summary>
    public required PublishingOption PublishingOption { get; init; }

    /// <summary>The default options: <see cref="Subjects.PublishingOption.Serial"/> publishing.</summary>
    public static ReplayLatestSubjectCreationOptions Default { get; } = new()
    {
        PublishingOption = PublishingOption.Serial
    };
}