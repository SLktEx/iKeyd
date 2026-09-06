using iKeyd.Core.Automation;
using iKeyd.Core.Chords;
using iKeyd.Core.State;

namespace iKeyd.Core.Behaviors;

/// <summary>
/// A bounded output tree used by conditional behaviors. It can contain existing
/// primitive BehaviorAction values or nested typed conditions; it is not a
/// general-purpose expression tree.
/// </summary>
public abstract record BehaviorOutputBranch
{
    internal abstract void Emit(
        ISystemQuerySnapshot systemQueries,
        IRuntimeStateSnapshot runtimeState,
        List<BehaviorAction> actions);
    internal abstract void CollectSystemQueries(ISet<string> queries);

    public static BehaviorOutputBranch Action(BehaviorAction action)
        => new PrimitiveBehaviorOutputBranch(action);

    public static BehaviorOutputBranch When(
        IBehaviorCondition condition,
        BehaviorOutputBranch thenBranch,
        BehaviorOutputBranch? elseBranch = null)
        => new ConditionalBehaviorOutputBranch(condition, thenBranch, elseBranch);
}

public sealed record PrimitiveBehaviorOutputBranch(BehaviorAction Effect) : BehaviorOutputBranch
{
    internal override void Emit(
        ISystemQuerySnapshot systemQueries,
        IRuntimeStateSnapshot runtimeState,
        List<BehaviorAction> actions)
        => actions.Add(Effect);

    internal override void CollectSystemQueries(ISet<string> queries)
    {
        if (Effect.Kind == BehaviorActionKind.Query && Effect.Name is not null)
            queries.Add(Effect.Name);
    }
}

public sealed record ConditionalBehaviorOutputBranch : BehaviorOutputBranch
{
    public ConditionalBehaviorOutputBranch(
        IBehaviorCondition condition,
        BehaviorOutputBranch thenBranch,
        BehaviorOutputBranch? elseBranch = null)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Then = thenBranch ?? throw new ArgumentNullException(nameof(thenBranch));
        Else = elseBranch;
    }

    public IBehaviorCondition Condition { get; }
    public BehaviorOutputBranch Then { get; }
    public BehaviorOutputBranch? Else { get; }

    internal override void Emit(
        ISystemQuerySnapshot systemQueries,
        IRuntimeStateSnapshot runtimeState,
        List<BehaviorAction> actions)
    {
        if (Condition.Evaluate(systemQueries, runtimeState))
            Then.Emit(systemQueries, runtimeState, actions);
        else
            Else?.Emit(systemQueries, runtimeState, actions);
    }

    internal override void CollectSystemQueries(ISet<string> queries)
    {
        Condition.CollectSystemQueries(queries);
        Then.CollectSystemQueries(queries);
        Else?.CollectSystemQueries(queries);
    }
}

internal sealed class ConditionalBehaviorDefinition(
    BehaviorOutputBranch branch,
    ISystemQuerySnapshot systemQueries,
    IRuntimeStateSnapshot runtimeState) : BehaviorDefinition
{
    private readonly BehaviorOutputBranch _branch = branch ?? throw new ArgumentNullException(nameof(branch));
    private readonly ISystemQuerySnapshot _systemQueries = systemQueries ?? throw new ArgumentNullException(nameof(systemQueries));
    private readonly IRuntimeStateSnapshot _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));

    internal override BehaviorInstance CreateInstance(KeyId sourceKey, long timestampMs)
        => new ConditionalBehaviorInstance(sourceKey, _branch, _systemQueries, _runtimeState);
}

internal sealed class ConditionalBehaviorInstance(
    KeyId sourceKey,
    BehaviorOutputBranch branch,
    ISystemQuerySnapshot systemQueries,
    IRuntimeStateSnapshot runtimeState) : BehaviorInstance(sourceKey)
{
    internal override void OnPress(long timestampMs, List<BehaviorAction> actions)
        => branch.Emit(systemQueries, runtimeState, actions);

    internal override void OnRelease(long timestampMs, List<BehaviorAction> actions)
    {
    }
}
