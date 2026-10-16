using System.Collections.Immutable;
using AeroFlow.GateAllocation.Domain;

namespace AeroFlow.GateAllocation.Storage;

/// <summary>
/// Thread-safe in-memory store: one immutable snapshot of the plan and its outbox, swapped with compare-and-swap.
/// Readers always see a consistent snapshot; writers retry on contention. Because the plan and the outbox change in
/// one swap, a change can never be kept while its gate events are dropped (ADR-0017).
/// </summary>
public sealed class InMemoryGatePlanStore(GatePlan seed) : IGatePlanStore
{
    private State _state = new(seed, []);

    public GatePlan Current() => Volatile.Read(ref _state).Plan;

    public Result<GatePlan> Update(Func<GatePlan, Result<GatePlan>> transition) =>
        Update(transition, static (_, _) => []).Map(r => r.Plan);

    public Result<(GatePlan Plan, IReadOnlyList<GateEvent> Events)> Update(
        Func<GatePlan, Result<GatePlan>> transition,
        Func<GatePlan, GatePlan, IReadOnlyList<GateEvent>> emit)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref _state);
            var outcome = transition(snapshot.Plan);
            if (outcome is Result<GatePlan>.Failure failure)
            {
                return failure.Error;
            }

            var plan = ((Result<GatePlan>.Ok)outcome).Value;
            var events = emit(snapshot.Plan, plan);
            var next = new State(plan, snapshot.Outbox.AddRange(events));
            if (ReferenceEquals(Interlocked.CompareExchange(ref _state, next, snapshot), snapshot))
            {
                return (plan, events);
            }
        }
    }

    public IReadOnlyList<GateEvent> Pending() => Volatile.Read(ref _state).Outbox;

    public void MarkPublished(Guid eventId)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref _state);
            var outbox = snapshot.Outbox.RemoveAll(e => e.EventId == eventId);
            if (outbox.Count == snapshot.Outbox.Count
                || ReferenceEquals(Interlocked.CompareExchange(ref _state, snapshot with { Outbox = outbox }, snapshot), snapshot))
            {
                return;
            }
        }
    }

    private sealed record State(GatePlan Plan, ImmutableList<GateEvent> Outbox);
}
