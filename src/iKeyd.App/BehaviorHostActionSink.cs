using iKeyd.Core.Behaviors;

namespace iKeyd.App;

/// <summary>
/// Capability boundary for behavior actions that must leave the keyboard-hook
/// thread, such as clipboard UI and asynchronous macro execution. Implementations
/// must return quickly and schedule the work elsewhere.
/// </summary>
internal interface IBehaviorHostActionSink
{
    void Post(BehaviorAction action);
}

internal sealed class DelegateBehaviorHostActionSink(Action<BehaviorAction> post) : IBehaviorHostActionSink
{
    private readonly Action<BehaviorAction> _post = post ?? throw new ArgumentNullException(nameof(post));

    public void Post(BehaviorAction action) => _post(action);
}
