using AeroFlow.GateAllocation.Domain;

namespace AeroFlow.GateAllocation.Storage;

/// <summary>Edge adapter for the gate plan and its outbox of unsent gate events (ADR-0017). The domain never sees this.</summary>
public interface IGatePlanStore
{
    GatePlan Current();

    /// <summary>
    /// Atomically replaces the plan with the outcome of <paramref name="transition"/>.
    /// The transition must be pure: it may be re-run if another writer got there first.
    /// </summary>
    Result<GatePlan> Update(Func<GatePlan, Result<GatePlan>> transition);

    /// <summary>
    /// As <see cref="Update(Func{GatePlan, Result{GatePlan}})"/>, for a change caused by the event
    /// <paramref name="sourceEventId"/>. In the same atomic step it adds the events <paramref name="emit"/> works out
    /// from the old and new plan to the outbox and remembers the source id. If that id was already applied, nothing
    /// changes and the result is <c>Ok(None)</c> (idempotent redelivery), so callers can report it as ignored.
    /// Both functions must be pure.
    /// </summary>
    Result<Option<(GatePlan Plan, IReadOnlyList<GateEvent> Events)>> Update(
        Guid sourceEventId,
        Func<GatePlan, Result<GatePlan>> transition,
        Func<GatePlan, GatePlan, IReadOnlyList<GateEvent>> emit);

    /// <summary>Whether a change caused by <paramref name="sourceEventId"/> was already applied (bounded memory).</summary>
    bool HasProcessed(Guid sourceEventId);

    /// <summary>Gate events stored but not yet published, oldest first.</summary>
    IReadOnlyList<GateEvent> Pending();

    /// <summary>
    /// Removes <paramref name="sent"/> from the head of the outbox once it is published. Only that exact entry is
    /// removed, so another pending event can never be dropped by mistake. Returns false, changing nothing, if it is not the head.
    /// </summary>
    bool MarkPublished(GateEvent sent);
}
