namespace R3Async.Subjects;

public sealed record SubjectCreationOptions
{
    public required PublishingOption PublishingOption { get; init; }
    public static SubjectCreationOptions Default { get; } = new()
    {
        PublishingOption = PublishingOption.Serial
    };
}