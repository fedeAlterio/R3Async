namespace R3Async.Subjects;

/// <summary>Controls how a subject notifies its subscribed observers when a value, error, or completion is pushed into it.</summary>
public enum PublishingOption
{
    /// <summary>Observers are notified one after another; a slow observer delays notification of the rest.</summary>
    Serial,

    /// <summary>Observers are notified concurrently, allowing them to process the notification in parallel.</summary>
    Concurrent
}