using AeroFlow.GateAllocation.Domain;

namespace AeroFlow.GateAllocation.Storage;

/// <summary>Edge adapter for the gate plan. The domain never sees this.</summary>
public interface IGatePlanStore
{
    GatePlan Current();

    /// <summary>
    /// Atomically replaces the plan with the outcome of <paramref name="transition"/>.
    /// The transition must be pure: it may be re-run if another writer got there first.
    /// </summary>
    Result<GatePlan> Update(Func<GatePlan, Result<GatePlan>> transition);
}
