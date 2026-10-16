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
    /// As <see cref="Update(Func{GatePlan, Result{GatePlan}})"/>, and in the same atomic step adds the events
    /// <paramref name="emit"/> works out from the old and new plan to the outbox. Both functions must be pure.
    /// Returns the new plan and the events that were queued.
    /// </summary>
    Result<(GatePlan Plan, IReadOnlyList<GateEvent> Events)> Update(
        Func<GatePlan, Result<GatePlan>> transition,
        Func<GatePlan, GatePlan, IReadOnlyList<GateEvent>> emit);

    /// <summary>Gate events stored but not yet published, oldest first.</summary>
    IReadOnlyList<GateEvent> Pending();

    /// <summary>Removes a published event from the outbox. Unknown ids are ignored.</summary>
    void MarkPublished(Guid eventId);
}
