namespace R3Async.Subjects;

public abstract class BaseBehaviorSubject<T>(T startValue) : BaseReplayLatestSubject<T>(new(startValue));