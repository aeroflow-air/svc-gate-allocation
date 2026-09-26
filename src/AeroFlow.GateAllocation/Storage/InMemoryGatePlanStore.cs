using AeroFlow.GateAllocation.Domain;

namespace AeroFlow.GateAllocation.Storage;

/// <summary>
/// Thread-safe in-memory store: one immutable <see cref="GatePlan"/> swapped with compare-and-swap.
/// Readers always see a consistent snapshot; writers retry on contention.
/// </summary>
public sealed class InMemoryGatePlanStore(GatePlan seed) : IGatePlanStore
{
    private GatePlan _plan = seed;

    public GatePlan Current() => Volatile.Read(ref _plan);

    public Result<GatePlan> Update(Func<GatePlan, Result<GatePlan>> transition)
    {
        while (true)
        {
            var snapshot = Volatile.Read(ref _plan);
            var outcome = transition(snapshot);
            if (outcome is not Result<GatePlan>.Ok ok)
            {
                return outcome;
            }

            if (ReferenceEquals(Interlocked.CompareExchange(ref _plan, ok.Value, snapshot), snapshot))
            {
                return outcome;
            }
        }
    }
}
