namespace R3Async.Subjects;

public sealed record SubjectCreationOptions
{
    public required PublishingOption PublishingOption { get; init; }
    public required bool IsStateless { get; init; }
    public static SubjectCreationOptions Default { get; } = new()
    {
        PublishingOption = PublishingOption.Serial,
        IsStateless = false
    };
}

public sealed record BehaviorSubjectCreationOptions
{
    public required PublishingOption PublishingOption { get; init; }
    public required bool IsStateless { get; init; }
    public static BehaviorSubjectCreationOptions Default { get; } = new()
    {
        PublishingOption = PublishingOption.Serial,
        IsStateless = false
    };
}

public sealed record ReplayLatestSubjectCreationOptions
{
    public required PublishingOption PublishingOption { get; init; }
    public required bool IsStateless { get; init; }
    public static ReplayLatestSubjectCreationOptions Default { get; } = new()
    {
        PublishingOption = PublishingOption.Serial,
        IsStateless = false
    };
}