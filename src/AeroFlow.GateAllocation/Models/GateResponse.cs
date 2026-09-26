using AeroFlow.GateAllocation.Domain;

namespace AeroFlow.GateAllocation.Models;

/// <summary>
/// API shape for a gate and its allocations, earliest first. The <see cref="GateStatus"/> union is flattened
/// to a <c>status</c> string plus a <c>closedReason</c> that is only present for closed gates.
/// </summary>
public sealed record GateResponse(
    string Id,
    string MaxAircraftSize,
    string Status,
    string? ClosedReason,
    IReadOnlyList<AllocationResponse> Allocations)
{
    public static GateResponse From(GatePlan plan, Gate gate) => new(
        Id: gate.Id.Value,
        MaxAircraftSize: gate.MaxSize.ToString(),
        Status: gate.Status switch
        {
            GateStatus.Closed => "Closed",
            _ => "Open",
        },
        ClosedReason: gate.Status is GateStatus.Closed closed ? closed.Reason : null,
        Allocations: plan.AllocationsAt(gate.Id).ConvertAll(AllocationResponse.From));
}
