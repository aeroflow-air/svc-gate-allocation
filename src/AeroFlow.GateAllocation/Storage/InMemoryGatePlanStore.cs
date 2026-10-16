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
    /// <summary>How many applied flight event ids are remembered for idempotency (oldest forgotten first).</summary>
    public const int ProcessedMemory = 10_000;

    private State _state = new(seed, [], [], []);

    public GatePlan Current() => Volatile.Read(ref _state).Plan;

    public bool HasProcessed(Guid sourceEventId) => Volatile.Read(ref _state).Processed.Contains(sourceEventId);

    public Result<GatePlan> Update(Func<GatePlan, Result<GatePlan>> transition) =>
        Swap(transition, static (_, _) => [], Option.None<Guid>()).Map(r => r.Match(c => c.Plan, Current));

    public Result<Option<(GatePlan Plan, IReadOnlyList<GateEvent> Events)>> Update(
        Guid sourceEventId,
        Func<GatePlan, Result<GatePlan>> transition,
        Func<GatePlan, GatePlan, IReadOnlyList<GateEvent>> emit) =>
        Swap(transition, emit, Option.Some(sourceEventId));

    public IReadOnlyList<GateEvent> Pending() => Volatile.Read(ref _state).Outbox;

    public bool MarkPublished(GateEvent sent)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref _state);
            if (snapshot.Outbox.IsEmpty || !ReferenceEquals(snapshot.Outbox[0], sent))
            {
                return false;
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _state, snapshot with { Outbox = snapshot.Outbox.RemoveAt(0) }, snapshot), snapshot))
            {
                return true;
            }
        }
    }

    private Result<Option<(GatePlan Plan, IReadOnlyList<GateEvent> Events)>> Swap(
        Func<GatePlan, Result<GatePlan>> transition,
        Func<GatePlan, GatePlan, IReadOnlyList<GateEvent>> emit,
        Option<Guid> source)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref _state);
            if (source is Option<Guid>.Some seen && snapshot.Processed.Contains(seen.Value))
            {
                return Option.None<(GatePlan, IReadOnlyList<GateEvent>)>();
            }

            var outcome = transition(snapshot.Plan);
            if (outcome is Result<GatePlan>.Failure failure)
            {
                return failure.Error;
            }

            var plan = ((Result<GatePlan>.Ok)outcome).Value;
            var events = emit(snapshot.Plan, plan);
            var next = new State(plan, snapshot.Outbox.AddRange(events), snapshot.Processed, snapshot.ProcessedOrder);
            if (source is Option<Guid>.Some applied)
            {
                next = next.Remember(applied.Value);
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _state, next, snapshot), snapshot))
            {
                return Option.Some<(GatePlan, IReadOnlyList<GateEvent>)>((plan, events));
            }
        }
    }

    private sealed record State(
        GatePlan Plan,
        ImmutableList<GateEvent> Outbox,
        ImmutableHashSet<Guid> Processed,
        ImmutableQueue<Guid> ProcessedOrder)
    {
        public State Remember(Guid id)
        {
            var processed = Processed.Add(id);
            var order = ProcessedOrder.Enqueue(id);
            while (processed.Count > ProcessedMemory)
            {
                order = order.Dequeue(out var oldest);
                processed = processed.Remove(oldest);
            }

            return this with { Processed = processed, ProcessedOrder = order };
        }
    }
}
