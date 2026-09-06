using iKeyd.Core.Configuration;

namespace iKeyd.App;

/// <summary>
/// Capability boundary for configured actions that must leave the keyboard-hook
/// thread (for example WinForms clipboard UI or asynchronous macros).
/// Implementations must return quickly and schedule the work elsewhere.
/// </summary>
internal interface IConfiguredHostActionSink
{
    void Post(KeyBehaviorAction action);
}

internal sealed class DelegateConfiguredHostActionSink(Action<KeyBehaviorAction> post) : IConfiguredHostActionSink
{
    private readonly Action<KeyBehaviorAction> _post = post ?? throw new ArgumentNullException(nameof(post));

    public void Post(KeyBehaviorAction action) => _post(action);
}
