namespace R3Async.Subjects;

public sealed record SubjectCreationOptions
{
    public required PublishingOption PublishingOption { get; init; }
    public static SubjectCreationOptions Default { get; } = new()
    {
        PublishingOption = PublishingOption.Serial
    };
}

public sealed record BehaviorSubjectCreationOptions
{
    public required PublishingOption PublishingOption { get; init; }
    public static BehaviorSubjectCreationOptions Default { get; } = new()
    {
        PublishingOption = PublishingOption.Serial
    };
}