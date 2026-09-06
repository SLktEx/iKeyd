using iKeyd.Core.Automation;
using iKeyd.Core.Chords;

namespace iKeyd.Core.Behaviors;

/// <summary>
/// A bounded output tree used by conditional behaviors. It can contain existing
/// primitive BehaviorAction values or nested system-query conditions; it is not a
/// general-purpose expression tree.
/// </summary>
public abstract record BehaviorOutputBranch
{
    internal abstract void Emit(ISystemQuerySnapshot snapshot, List<BehaviorAction> actions);
    internal abstract void CollectSystemQueries(ISet<string> queries);

    public static BehaviorOutputBranch Action(BehaviorAction action)
        => new PrimitiveBehaviorOutputBranch(action);

    public static BehaviorOutputBranch When(
        SystemQueryCondition condition,
        BehaviorOutputBranch thenBranch,
        BehaviorOutputBranch? elseBranch = null)
        => new ConditionalBehaviorOutputBranch(condition, thenBranch, elseBranch);
}

public sealed record PrimitiveBehaviorOutputBranch(BehaviorAction Effect) : BehaviorOutputBranch
{
    internal override void Emit(ISystemQuerySnapshot snapshot, List<BehaviorAction> actions)
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
        SystemQueryCondition condition,
        BehaviorOutputBranch thenBranch,
        BehaviorOutputBranch? elseBranch = null)
    {
        Condition = condition ?? throw new ArgumentNullException(nameof(condition));
        Then = thenBranch ?? throw new ArgumentNullException(nameof(thenBranch));
        Else = elseBranch;
    }

    public SystemQueryCondition Condition { get; }
    public BehaviorOutputBranch Then { get; }
    public BehaviorOutputBranch? Else { get; }

    internal override void Emit(ISystemQuerySnapshot snapshot, List<BehaviorAction> actions)
    {
        if (Condition.Evaluate(snapshot))
            Then.Emit(snapshot, actions);
        else
            Else?.Emit(snapshot, actions);
    }

    internal override void CollectSystemQueries(ISet<string> queries)
    {
        queries.Add(Condition.Query);
        Then.CollectSystemQueries(queries);
        Else?.CollectSystemQueries(queries);
    }
}

internal sealed class ConditionalBehaviorDefinition(
    BehaviorOutputBranch branch,
    ISystemQuerySnapshot snapshot) : BehaviorDefinition
{
    private readonly BehaviorOutputBranch _branch = branch ?? throw new ArgumentNullException(nameof(branch));
    private readonly ISystemQuerySnapshot _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    internal override BehaviorInstance CreateInstance(KeyId sourceKey, long timestampMs)
        => new ConditionalBehaviorInstance(sourceKey, _branch, _snapshot);
}

internal sealed class ConditionalBehaviorInstance(
    KeyId sourceKey,
    BehaviorOutputBranch branch,
    ISystemQuerySnapshot snapshot) : BehaviorInstance(sourceKey)
{
    internal override void OnPress(long timestampMs, List<BehaviorAction> actions)
        => branch.Emit(snapshot, actions);

    internal override void OnRelease(long timestampMs, List<BehaviorAction> actions)
    {
    }
}
