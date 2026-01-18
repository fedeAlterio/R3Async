namespace R3Async.Subjects;

public abstract class BaseStatelessBehaviorSubject<T>(T startValue) : BaseStatelessReplayLastSubject<T>(new(startValue));