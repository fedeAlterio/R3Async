using System.Threading;

namespace R3Async.Internals;

// Single AsyncLocal chain of frames used for every reentrancy check. A FlowToken is its own frame, so each
// token must be entered exactly once.
internal static class ExecutionFlow
{
    static readonly AsyncLocal<FlowFrame?> s_frames = new();

    public static void Enter(object owner)
    {
        var parent = s_frames.Value;
        while (parent is not null && !parent.IsActive)
        {
            parent = parent.Parent;
        }

        var frame = owner as FlowToken ?? (FlowFrame)new OwnerFrame(owner);
        frame.Parent = parent;
        s_frames.Value = frame;
    }

    public static bool Contains(object? owner)
    {
        if (owner is null)
        {
            return false;
        }

        for (var frame = s_frames.Value; frame is not null; frame = frame.Parent)
        {
            if (ReferenceEquals(frame.Owner, owner))
            {
                return true;
            }
        }

        return false;
    }

    internal static int Depth
    {
        get
        {
            var depth = 0;
            for (var frame = s_frames.Value; frame is not null; frame = frame.Parent)
            {
                depth++;
            }

            return depth;
        }
    }

    sealed class OwnerFrame(object owner) : FlowFrame
    {
        public override object Owner => owner;
    }
}

// Parent is set once before the frame is published and never changes.
internal abstract class FlowFrame
{
    public FlowFrame? Parent;

    public abstract object Owner { get; }

    public virtual bool IsActive => true;
}

// Count only goes from 1 down to 0; at 0 the frame is dropped from the flow.
internal class FlowToken : FlowFrame
{
    public int Count = 1;

    public override object Owner => this;

    public override bool IsActive => Volatile.Read(ref Count) > 0;
}
